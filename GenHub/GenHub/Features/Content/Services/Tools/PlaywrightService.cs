using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GenHub.Features.Content.Services.Tools;

/// <summary>
/// Service for managing Playwright browser instances and fetching web content.
/// Provides shared browser resources across the application.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PlaywrightService"/> class.
/// </remarks>
/// <param name="logger">Logger instance.</param>
/// <param name="configurationProvider">Application configuration provider.</param>
/// <param name="dialogService">Dialog service used to confirm managed Chromium installation.</param>
/// <param name="notificationService">Optional notifications shown before a headed browser window opens.</param>
public sealed class PlaywrightService(
    ILogger<PlaywrightService> logger,
    IConfigurationProviderService configurationProvider,
    IDialogService dialogService,
    INotificationService? notificationService = null) : IPlaywrightService, IDisposable, IAsyncDisposable
{
    private static readonly HashSet<string> UnsafeExtraHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Content-Length",
        "Cookie",
        "Host",
        "Proxy-Connection",
        "Transfer-Encoding",
        "Upgrade",
    };

    private static readonly TimeSpan KeptOpenChallengePageTimeout = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private readonly SemaphoreSlim _persistentLock = new(1, 1);
    private readonly SemaphoreSlim _playwrightLock = new(1, 1);

    /// <summary>
    /// Serializes all headed persistent-profile operations (single fetch, multi-URL sweep, ModDB
    /// download).
    /// </summary>
    private readonly SemaphoreSlim _persistentFetchLock = new(1, 1);
    private readonly AsyncLocal<bool> _isInPersistentSession = new();
    private readonly HashSet<IPage> _inUsePersistentPages = [];
    private readonly ConcurrentDictionary<IPage, CancellationTokenSource> _keptOpenCleanupTokens = new();
    private readonly CancellationTokenSource _cleanupCts = new();

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _persistentContext;
    private string? _persistentProfileName;
    private int _activePersistentSessions;

    private ManagedChromiumRuntime? managedChromiumRuntime;

    private int _disposeState;

    /// <inheritdoc />
    public async Task<IPage> CreatePageAsync(BrowserNewContextOptions? options = null, CancellationToken cancellationToken = default)
    {
        await EnsurePlaywrightInitializedAsync(cancellationToken);

        if (_browser == null)
        {
            throw new InvalidOperationException("Browser not initialized");
        }

        var contextOptions = options ?? new BrowserNewContextOptions
        {
            UserAgent = ModDBConstants.BrowserUserAgent,
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
            Locale = "en-US",
        };

        var context = await _browser.NewContextAsync(contextOptions);

        try
        {
            return await context.NewPageAsync();
        }
        catch
        {
            try
            {
                await context.CloseAsync();
            }
            catch (PlaywrightException ex)
            {
                logger.LogDebug(ex, "Failed to close context during page creation failure.");
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IPage> CreatePersistentPageAsync(string profileName, CancellationToken cancellationToken = default)
    {
        ValidateProfileName(profileName);
        var profileDir = Path.Combine(
            configurationProvider.GetApplicationDataPath(),
            DirectoryNames.BrowserProfiles,
            profileName);

        // Closing the last page of a headed persistent context also terminates its browser process,
        // leaving the cached _persistentContext pointing at a dead channel. Detect that up front so
        // the first operation after a previous page-close relaunches the profile instead of throwing
        // TargetClosedException deep inside EnsurePersistentContextAsync/NewPageAsync.
        await EnsurePersistentContextAsync(profileDir, cancellationToken);

        if (_persistentContext == null)
        {
            throw new InvalidOperationException("Persistent browser context not initialized");
        }

        return await CreatePersistentPageWithRecoveryAsync(profileDir, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ClosePersistentPageAsync(IPage page, bool keepOpen = false)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (keepOpen)
        {
            ScheduleKeptOpenPageCleanup(page);
            return;
        }

        await _persistentLock.WaitAsync(CancellationToken.None);
        try
        {
            UntrackPersistentPage(page);

            if (_persistentContext != null && IsPersistentContextAlive())
            {
                await HandlePageCloseUnderActiveContextAsync(page);
            }
            else
            {
                await SafeClosePageAsync(page);
            }
        }
        finally
        {
            _persistentLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Invalid or unsupported URL scheme: {url}", nameof(url));
        }

        logger.LogDebug("Fetching HTML from {Url}", url);

        var page = await CreatePageAsync(cancellationToken: cancellationToken);
        try
        {
            await page.GotoAsync(url, new PageGotoOptions
            {
                Timeout = 30000,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });

            // Wait a bit for dynamic content to load
            await Task.Delay(500, cancellationToken);

            return await page.ContentAsync();
        }
        finally
        {
            var context = page.Context;
            try
            {
                if (!page.IsClosed)
                {
                    try
                    {
                        await page.CloseAsync();
                    }
                    catch (PlaywrightException ex)
                    {
                        logger.LogDebug(ex, "Failed to close page during cleanup.");
                    }
                }
            }
            finally
            {
                if (context != null)
                {
                    try
                    {
                        await context.CloseAsync();
                    }
                    catch (PlaywrightException ex)
                    {
                        logger.LogDebug(ex, "Failed to close context during cleanup.");
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task<IDocument> FetchAndParseAsync(string url, CancellationToken cancellationToken = default)
    {
        var html = await FetchHtmlAsync(url, cancellationToken);
        var browsingContext = BrowsingContext.New(Configuration.Default);
        return await browsingContext.OpenAsync(req => req.Content(html), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IDocument> FetchAndParsePersistentAsync(string profileName, string url, CancellationToken cancellationToken = default)
    {
        var html = await FetchPersistentHtmlAsync(profileName, url, cancellationToken);
        return await OpenDocumentAsync(html, cancellationToken);
    }

    /// <summary>
    /// Executes an operation within a scoped persistent browser context session.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="profileName">The on-disk profile name.</param>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<T> ExecuteInPersistentContextAsync<T>(
        string profileName,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidateProfileName(profileName);

        var profileDir = Path.Combine(
            configurationProvider.GetApplicationDataPath(),
            DirectoryNames.BrowserProfiles,
            profileName);

        var isOuterSession = !_isInPersistentSession.Value;
        if (isOuterSession)
        {
            await _persistentFetchLock.WaitAsync(cancellationToken);
            _isInPersistentSession.Value = true;
        }

        try
        {
            Interlocked.Increment(ref _activePersistentSessions);
            try
            {
                await EnsurePersistentContextAsync(profileDir, cancellationToken);
                return await operation();
            }
            finally
            {
                if (Interlocked.Decrement(ref _activePersistentSessions) == 0)
                {
                    await ClosePersistentContextCoreAsync();
                }
            }
        }
        finally
        {
            if (isOuterSession)
            {
                _isInPersistentSession.Value = false;
                _persistentFetchLock.Release();
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, IDocument>> FetchAndParsePersistentManyAsync(
        string profileName,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(urls);
        ValidateProfileName(profileName);

        var results = new Dictionary<string, IDocument>(StringComparer.Ordinal);
        if (urls.Count == 0)
        {
            return results;
        }

        ValidatePersistentUrls(profileName, urls);

        var orderedUnique = FilterUniqueUrls(urls);
        if (orderedUnique.Count == 0)
        {
            return results;
        }

        return await ExecuteInPersistentContextAsync(
            profileName,
            async () =>
            {
                logger.LogDebug(
                    "Fetching {Count} URL(s) in parallel on persistent context (profile '{Profile}')",
                    orderedUnique.Count,
                    profileName);

                var concurrentResults = new System.Collections.Concurrent.ConcurrentDictionary<string, IDocument>(StringComparer.Ordinal);

                // Limit parallel tabs to avoid overwhelming system resources (max 5 parallel tabs)
                using var tabSemaphore = new SemaphoreSlim(Math.Min(orderedUnique.Count, 5));

                var tasks = orderedUnique.Select(url =>
                    FetchSinglePersistentDocumentAsync(profileName, url, concurrentResults, tabSemaphore, cancellationToken));

                await Task.WhenAll(tasks);

                foreach (var url in orderedUnique)
                {
                    if (concurrentResults.TryGetValue(url, out var doc))
                    {
                        results[url] = doc;
                    }
                }

                return (IReadOnlyDictionary<string, IDocument>)results;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task WarmupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureManagedPlaywrightAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Background Playwright warmup did not complete.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        Task.Run(() => DisposeCoreAsync(), CancellationToken.None).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        await DisposeCoreAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadFileAsync(GenHub.Core.Models.Common.DownloadConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Starting Playwright download from {Url}", configuration.Url);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var isModDb = IsModDbHost(configuration.Url);
            var usePersistentModDbProfile = isModDb && configuration.Url.Scheme == Uri.UriSchemeHttps;

            if (isModDb && !usePersistentModDbProfile)
            {
                logger.LogDebug("Download URL {Url} uses HTTP; persistent ModDB profile will not be used.", configuration.Url);
            }

            // Headed ModDB profile is shared with FetchPersistentHtmlAsync / multi-URL sweeps —
            // serialize so a download cannot relaunch Chromium while a section page is mid-Goto.
            var isOuterSession = usePersistentModDbProfile && !_isInPersistentSession.Value;
            if (isOuterSession)
            {
                await _persistentFetchLock.WaitAsync(cancellationToken);
                _isInPersistentSession.Value = true;
            }

            try
            {
                return await DownloadFileCoreAsync(configuration, usePersistentModDbProfile, stopwatch, cancellationToken);
            }
            finally
            {
                if (isOuterSession)
                {
                    await ClosePersistentContextCoreAsync().ConfigureAwait(false);

                    _isInPersistentSession.Value = false;
                    _persistentFetchLock.Release();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Playwright download failed for {Url}", configuration.Url);
            var message = ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
                ? "The bundled Chromium runtime is unavailable. Install the application browser runtime and retry the ModDB download."
                : ex.Message;
            return DownloadResult.CreateFailure(message, bytesDownloaded: 0, elapsed: TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Builds browser-safe request headers from a download configuration. Headers which Chromium
    /// owns itself are ignored, while site-required headers such as <c>Referer</c> are preserved.
    /// </summary>
    /// <param name="configuration">Download configuration containing optional request headers.</param>
    /// <returns>Headers safe to apply to a Playwright page.</returns>
    internal static IReadOnlyDictionary<string, string> BuildSafeDownloadHeaders(
        GenHub.Core.Models.Common.DownloadConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in configuration.Headers)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value) || UnsafeExtraHeaders.Contains(name))
            {
                continue;
            }

            headers[name] = value;
        }

        if (!string.IsNullOrWhiteSpace(configuration.UserAgent) && !headers.ContainsKey("User-Agent"))
        {
            headers["User-Agent"] = configuration.UserAgent;
        }

        return headers;
    }

    /// <summary>
    /// Determines whether a Playwright exception represents a transient navigation or closed-target
    /// condition that a polling loop can safely retry (Cloudflare interstitial redirecting to the
    /// real document, a frame detaching mid-probe, or the persistent context dying between pages).
    /// Shared with <see cref="ContentDiscoverers.ModDBDiscoverer"/> for its listing-wait loop.
    /// </summary>
    /// <param name="ex">The Playwright exception to classify.</param>
    /// <returns><see langword="true"/> if the caller should retry rather than propagate.</returns>
    internal static bool IsContextClosedError(PlaywrightException ex) =>
        ex.Message.Contains("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Browser has been closed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Target closed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Target frame was detached", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Target.createTarget", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether a navigation exception from Playwright indicates that navigation was aborted
    /// because a direct file download started.
    /// </summary>
    /// <param name="ex">The navigation exception to classify.</param>
    /// <param name="downloadTcs">The task completion source tracking whether a download event has fired.</param>
    /// <returns><see langword="true"/> if the exception was caused by download initiation; otherwise <see langword="false"/>.</returns>
    internal static bool IsDownloadNavigationException(PlaywrightException ex, TaskCompletionSource<IDownload> downloadTcs)
    {
        if (ex.Message.Contains("Download is starting", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (downloadTcs.Task.IsCompleted && ex.Message.Contains("net::ERR_ABORTED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsBlankStartupUrl(string url) =>
        string.IsNullOrWhiteSpace(url) ||
        url.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("chrome://newtab", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("chrome://new-tab", StringComparison.OrdinalIgnoreCase);

    private static void ValidateProfileName(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);
        if (profileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            profileName.Contains("..", StringComparison.Ordinal) ||
            profileName.Contains('/') ||
            profileName.Contains('\\'))
        {
            throw new ArgumentException($"Invalid profile name '{profileName}': contains invalid path characters.", nameof(profileName));
        }
    }

    private static bool IsModDbVerificationPage(string? title) =>
        !string.IsNullOrWhiteSpace(title) &&
        (title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Attention Required", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Verify you are human", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase));

    private static bool IsModDbHost(Uri uri) =>
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        (uri.Host.Equals("moddb.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".moddb.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsModDbHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsModDbHost(uri);

    private static bool IsHttpsModDbUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        IsModDbHost(uri);

    private static async Task<IDocument> OpenDocumentAsync(string html, CancellationToken cancellationToken)
    {
        var browsingContext = BrowsingContext.New(Configuration.Default);
        return await browsingContext.OpenAsync(req => req.Content(html), cancellationToken);
    }

    private static async Task<(bool Ready, bool IsVerification)> ProbeForModDbContentOrVerificationAsync(IPage page)
    {
        var title = await page.TitleAsync();
        if (IsModDbVerificationPage(title))
        {
            return (false, true);
        }

        var contentMarker = await page.QuerySelectorAsync(
            "#downloadsinfo, .row.rowcontent, .headerbox, #articlebrowse, #profile");
        return (contentMarker != null, false);
    }

    private static void ValidatePersistentUrls(string profileName, IReadOnlyList<string> urls)
    {
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"Invalid URL (must be absolute HTTP/HTTPS): {url}", nameof(urls));
            }

            if (string.Equals(profileName, ModDBConstants.BrowserProfileName, StringComparison.OrdinalIgnoreCase) && !IsHttpsModDbUrl(url))
            {
                throw new ArgumentException($"URL is not permitted for profile '{profileName}' (must be HTTPS ModDB): {url}", nameof(urls));
            }
        }
    }

    private static List<string> FilterUniqueUrls(IReadOnlyList<string> urls)
    {
        var orderedUnique = new List<string>(urls.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url) || !seen.Add(url))
            {
                continue;
            }

            orderedUnique.Add(url);
        }

        return orderedUnique;
    }

    /// <summary>
    /// A persistent context is only reusable while it is live and still owns at least one open
    /// page. A headed Chromium process exits once its final page closes, after which the cached
    /// IBrowserContext reference throws on every call. Touching <see cref="IBrowserContext.Pages"/>
    /// is the cheapest probe: it fails on a dead channel and succeeds on a live one.
    /// </summary>
    private bool IsPersistentContextAlive()
    {
        var context = _persistentContext;
        if (context == null)
        {
            return false;
        }

        try
        {
            // Any property/method round-trip to the dead channel raises TargetClosedException.
            _ = context.Pages;
            return true;
        }
        catch (PlaywrightException ex) when (IsContextClosedError(ex))
        {
            return false;
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _cleanupCts.CancelAsync().ConfigureAwait(false);
            _cleanupCts.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to cancel or dispose cleanup token source.");
        }

        await _persistentLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_persistentContext != null)
            {
                try
                {
                    await _persistentContext.CloseAsync().ConfigureAwait(false);
                }
                catch (PlaywrightException ex)
                {
                    logger.LogDebug(ex, "Persistent context closed during disposal.");
                }
                finally
                {
                    ResetPersistentContextState();
                }
            }
        }
        finally
        {
            _persistentLock.Release();
        }

        await _browserLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_browser != null)
            {
                try
                {
                    await _browser.CloseAsync().ConfigureAwait(false);
                }
                catch (PlaywrightException ex)
                {
                    logger.LogDebug(ex, "Browser closed during disposal.");
                }
                finally
                {
                    _browser = null;
                }
            }
        }
        finally
        {
            _browserLock.Release();
        }

        await _playwrightLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_playwright != null)
            {
                try
                {
                    _playwright.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Playwright disposed during disposal.");
                }
                finally
                {
                    _playwright = null;
                }
            }
        }
        finally
        {
            _playwrightLock.Release();
        }
    }

    /// <summary>
    /// Clears a closed persistent browser context so the next request launches a fresh one.
    /// </summary>
    private async Task InvalidatePersistentContextAsync()
    {
        await _persistentLock.WaitAsync(CancellationToken.None);
        try
        {
            ResetPersistentContextState();
        }
        finally
        {
            _persistentLock.Release();
        }
    }

    private void TrackPersistentPage(IPage page)
    {
        if (_inUsePersistentPages.Add(page))
        {
            page.Close += OnPersistentPageClosed;
        }
    }

    private void UntrackPersistentPage(IPage page)
    {
        page.Close -= OnPersistentPageClosed;
        _inUsePersistentPages.Remove(page);

        if (_keptOpenCleanupTokens.TryRemove(page, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private void OnPersistentPageClosed(object? sender, IPage page)
    {
        _ = Task.Run(
            async () =>
            {
                await _persistentLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    UntrackPersistentPage(page);

                    if (Volatile.Read(ref _activePersistentSessions) == 0 && _inUsePersistentPages.Count == 0)
                    {
                        await ClosePersistentContextCoreUnderLockAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error handling persistent page close event.");
                }
                finally
                {
                    _persistentLock.Release();
                }
            },
            CancellationToken.None);
    }

    private void ScheduleKeptOpenPageCleanup(IPage page)
    {
        if (_disposeState != 0)
        {
            return;
        }

        var pageCts = CancellationTokenSource.CreateLinkedTokenSource(_cleanupCts.Token);
        if (!_keptOpenCleanupTokens.TryAdd(page, pageCts))
        {
            pageCts.Dispose();
            return;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(KeptOpenChallengePageTimeout, pageCts.Token);
                    await ClosePersistentPageAsync(page, keepOpen: false);
                }
                catch (OperationCanceledException)
                {
                    // Disposed or manually closed earlier
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to automatically clean up kept-open persistent page.");
                }
                finally
                {
                    if (_keptOpenCleanupTokens.TryRemove(page, out var cts))
                    {
                        cts.Dispose();
                    }
                }
            },
            CancellationToken.None);
    }

    /// <summary>
    /// Resets tracked persistent pages, unhooks page close events, cancels pending cleanup timers,
    /// and resets cached context state.
    /// </summary>
    private void ResetPersistentContextState()
    {
        foreach (var page in _inUsePersistentPages)
        {
            page.Close -= OnPersistentPageClosed;
        }

        foreach (var cts in _keptOpenCleanupTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _keptOpenCleanupTokens.Clear();
        _inUsePersistentPages.Clear();
        _persistentContext = null;
        _persistentProfileName = null;
    }

    /// <summary>
    /// Closes the persistent browser context when called under _persistentLock.
    /// </summary>
    private async Task ClosePersistentContextCoreUnderLockAsync()
    {
        if (_persistentContext != null)
        {
            try
            {
                var nonClosedPages = _persistentContext.Pages.Where(p => !p.IsClosed).ToList();
                foreach (var page in nonClosedPages)
                {
                    try
                    {
                        await page.CloseAsync();
                    }
                    catch (PlaywrightException)
                    {
                        // Ignore errors if the page was already closed or detached.
                    }
                }

                await _persistentContext.CloseAsync();
            }
            catch (PlaywrightException)
            {
                // Ignore errors if the persistent context was already closed or disposed.
            }
            finally
            {
                ResetPersistentContextState();
            }
        }
    }

    /// <summary>
    /// Closes the persistent browser context under _persistentLock if no active sessions or leased pages remain.
    /// </summary>
    private async Task ClosePersistentContextCoreAsync()
    {
        await _persistentLock.WaitAsync(CancellationToken.None);
        try
        {
            if (Volatile.Read(ref _activePersistentSessions) == 0 && _inUsePersistentPages.Count == 0)
            {
                await ClosePersistentContextCoreUnderLockAsync();
            }
        }
        finally
        {
            _persistentLock.Release();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("csharpsquid", "S4158", Justification = "Populated asynchronously via page.Popup event handler.")]
    private async Task<DownloadResult> DownloadFileCoreAsync(
        GenHub.Core.Models.Common.DownloadConfiguration configuration,
        bool usePersistentModDbProfile,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var page = usePersistentModDbProfile
            ? await CreatePersistentPageAsync(ModDBConstants.BrowserProfileName, cancellationToken)
            : await CreatePageAsync(cancellationToken: cancellationToken);

        System.Collections.Concurrent.ConcurrentBag<IPage> popups = [];
        var downloadTcs = new TaskCompletionSource<IDownload>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnDownload(object? sender, IDownload download) => downloadTcs.TrySetResult(download);
        void OnPopup(object? sender, IPage popup)
        {
            popups.Add(popup);
            popup.Download += OnDownload;
        }

        try
        {
            var requestHeaders = BuildSafeDownloadHeaders(configuration);
            if (requestHeaders.Count > 0)
            {
                await page.SetExtraHTTPHeadersAsync(requestHeaders);
            }

            if (usePersistentModDbProfile)
            {
                logger.LogInformation("Using persistent ModDB browser profile for protected download {Url}", configuration.Url);
                NotifyBrowserWindowOpening(
                    "ModDB download starting",
                    "A browser window is opening to download this file. Wait for the download to finish and do not click anything in that window.");
            }

            // ModDB frequently hands the actual binary off to a new tab/popup rather than the page
            // that performed the navigation. A page-level Download handler therefore misses the
            // event and the wait times out, surfacing as a download that "never completes". The
            // context-level handler catches downloads started in ANY page of the persistent context,
            // which is what actually fires for ModDB's popup-style mirrors.
            page.Download += OnDownload;
            page.Popup += OnPopup;

            // Trigger the download by navigating to the URL.
            try
            {
                await page.GotoAsync(configuration.Url.ToString(), new PageGotoOptions
                {
                    Timeout = (float)configuration.Timeout.TotalMilliseconds,
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                });
            }
            catch (PlaywrightException ex) when (IsDownloadNavigationException(ex, downloadTcs))
            {
                logger.LogDebug(ex, "Navigation initiated a direct download for {Url}", configuration.Url);
            }

            await TryTriggerFallbackDownloadAsync(page, downloadTcs, cancellationToken);

            // Wait for the download to START (not finish) with a generous timeout. ModDB's
            // redirect chain plus Cloudflare can take a while before the binary begins streaming.
            var waitTimeout = TimeSpan.FromMilliseconds(Math.Max(ValidationLimits.MinDownloadSaveTimeoutMs, configuration.Timeout.TotalMilliseconds));
            var download = await downloadTcs.Task.WaitAsync(waitTimeout, cancellationToken);

            if (download == null)
            {
                return DownloadResult.CreateFailure("Download failed to initialize (null download object).");
            }

            return await SaveDownloadFileAsync(download, configuration, usePersistentModDbProfile, stopwatch, cancellationToken);
        }
        finally
        {
            page.Popup -= OnPopup;
            page.Download -= OnDownload;
            await CleanupPopupsAsync(popups, OnDownload);
            await CleanupDownloadPageAsync(page, usePersistentModDbProfile);
        }
    }

    private async Task TryTriggerFallbackDownloadAsync(IPage page, TaskCompletionSource<IDownload> downloadTcs, CancellationToken cancellationToken)
    {
        var waitTask = Task.Delay(5000, cancellationToken);
        var completedTask = await Task.WhenAny(downloadTcs.Task, waitTask);

        if (completedTask == downloadTcs.Task)
        {
            return;
        }

        logger.LogInformation("Download did not start automatically within 5s. Attempting to find fallback link...");

        const string FallbackSelector = "a[href*='media.moddb.com'], a[href*='files.moddb.com'], a#download, a.download, a.btn-download, a.buttondownload, a[href*='/mirror/'], a[href*='/downloads/start/'], a[href*='/addons/start/']";

        IElementHandle? fallbackLink = null;
        try
        {
            fallbackLink = await page.QuerySelectorAsync(FallbackSelector);
        }
        catch (PlaywrightException ex)
        {
            logger.LogDebug(ex, "Failed to query fallback download selector.");
        }

        if (fallbackLink == null)
        {
            logger.LogWarning("No fallback download link found. Continuing to wait for download event...");
            return;
        }

        var text = await fallbackLink.InnerTextAsync();
        logger.LogInformation("Found fallback link '{Text}', clicking...", text);
        try
        {
            await fallbackLink.ClickAsync(new ElementHandleClickOptions { Timeout = 5000 });
            await CheckStartPageFallbackAsync(page, downloadTcs, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to click fallback link.");
        }
    }

    private async Task CheckStartPageFallbackAsync(IPage page, TaskCompletionSource<IDownload> downloadTcs, CancellationToken cancellationToken)
    {
        var secondWait = Task.Delay(4000, cancellationToken);
        var secondCompleted = await Task.WhenAny(downloadTcs.Task, secondWait);
        if (secondCompleted != downloadTcs.Task && !string.IsNullOrWhiteSpace(page.Url) && page.Url.Contains("/start/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var startPageFallback = await page.QuerySelectorAsync("a:has-text('click here'), a:has-text('Click here'), a[href*='media.moddb.com'], a[href*='files.moddb.com'], a[href*='/mirror/']");
                if (startPageFallback != null)
                {
                    var startText = await startPageFallback.InnerTextAsync();
                    logger.LogInformation("Found start-page mirror link '{Text}', clicking...", startText);
                    await startPageFallback.ClickAsync(new ElementHandleClickOptions { Timeout = 5000 });
                }
            }
            catch (PlaywrightException ex)
            {
                logger.LogDebug(ex, "Failed to query or click start-page fallback link.");
            }
        }
    }

    private async Task CleanupPopupsAsync(System.Collections.Concurrent.ConcurrentBag<IPage> popups, EventHandler<IDownload> onDownload)
    {
        foreach (var popup in popups)
        {
            popup.Download -= onDownload;
            if (!popup.IsClosed)
            {
                try
                {
                    await popup.CloseAsync();
                }
                catch (PlaywrightException ex)
                {
                    logger.LogDebug(ex, "Popup already closed during download cleanup.");
                }
            }
        }
    }

    /// <summary>
    /// Ensures Playwright is initialized with a browser instance.
    /// </summary>
    private async Task EnsurePlaywrightInitializedAsync(CancellationToken cancellationToken)
    {
        if (_browser != null && _browser.IsConnected) return;

        await _browserLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser != null && _browser.IsConnected) return;

            if (_browser != null)
            {
                try
                {
                    await _browser.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Disposing disconnected browser instance.");
                }
                finally
                {
                    _browser = null;
                }
            }

            var playwright = await EnsureManagedPlaywrightAsync(cancellationToken);
            _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                // The shared browser is headless for ordinary parsing/fetching. Bot-protected
                // sites that need a visible window use the persistent context instead.
                Headless = true,
                Args =
                [
                    "--disable-blink-features=AutomationControlled",
                    "--disable-dev-shm-usage",
                    "--no-default-browser-check",
                    "--no-first-run",
                    "--disable-background-networking",
                    "--disable-component-update",
                    "--disable-sync",
                ],
            });
        }
        finally
        {
            _browserLock.Release();
        }
    }

    /// <summary>
    /// Lazily creates (or reuses) a persistent, headed browser context for the given profile path.
    /// Cookies and storage are written to disk, so a Cloudflare clearance cookie obtained via a
    /// single manual challenge solve is reused for every subsequent page in the profile — across
    /// pages and across app restarts until it expires.
    /// </summary>
    /// <param name="profileDir">Absolute on-disk directory for the persistent profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task EnsurePersistentContextAsync(string profileDir, CancellationToken cancellationToken)
    {
        if (IsPersistentContextAlive() && string.Equals(_persistentProfileName, profileDir, StringComparison.Ordinal))
        {
            return;
        }

        await _persistentLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check under the lock: another caller may have just recreated the context. Also
            // drop a cached-but-dead context so the relaunch below actually runs.
            if (!IsPersistentContextAlive())
            {
                ResetPersistentContextState();
            }
            else if (string.Equals(_persistentProfileName, profileDir, StringComparison.Ordinal))
            {
                return;
            }
            else if (_persistentContext != null)
            {
                await ClosePersistentContextCoreUnderLockAsync();
            }

            var playwright = await EnsureManagedPlaywrightAsync(cancellationToken);
            Directory.CreateDirectory(profileDir);

            var context = await playwright.Chromium.LaunchPersistentContextAsync(
                profileDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    Args =
                    [
                        "--disable-blink-features=AutomationControlled",
                        "--disable-dev-shm-usage",
                        "--no-default-browser-check",
                        "--no-first-run",
                        "--disable-background-networking",
                        "--disable-background-timer-throttling",
                        "--disable-backgrounding-occluded-windows",
                        "--disable-breakpad",
                        "--disable-component-update",
                        "--disable-domain-reliability",
                        "--disable-features=Translate,OptimizationHints,MediaRouter,DialMediaRouteProvider",
                        "--disable-ipc-flooding-protection",
                        "--disable-renderer-backgrounding",
                        "--disable-sync",
                        "--metrics-recording-only",
                        "--no-pings",
                        "--password-store=basic",
                        "--use-mock-keychain",
                    ],
                    UserAgent = ModDBConstants.BrowserUserAgent,
                    ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
                    Locale = "en-US",
                    IgnoreDefaultArgs = ["--enable-automation"],
                });

            var ctx = context;
            context.Close += (_, _) =>
            {
                Task.Run(
                    async () =>
                    {
                        await _persistentLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                        try
                        {
                            if (ReferenceEquals(_persistentContext, ctx))
                            {
                                ResetPersistentContextState();
                            }
                        }
                        finally
                        {
                            _persistentLock.Release();
                        }
                    },
                    CancellationToken.None);
            };

            _persistentContext = context;
            _persistentProfileName = profileDir;
        }
        finally
        {
            _persistentLock.Release();
        }
    }

    /// <summary>
    /// Opens a new page in the persistent context, recovering once if the cached context or its
    /// browser process has died (the headed Chromium exits when its last page closes). The recovery
    /// recreates the profile — the Cloudflare clearance cookie persists on disk, so the user does
    /// not need to re-solve any challenge.
    /// </summary>
    private async Task<IPage> CreatePersistentPageWithRecoveryAsync(string profileDir, CancellationToken cancellationToken)
    {
        IPage page = null!;
        try
        {
            await _persistentLock.WaitAsync(cancellationToken);
            try
            {
                page = await GetOrCreatePersistentPageCoreAsync();
            }
            finally
            {
                _persistentLock.Release();
            }
        }
        catch (PlaywrightException ex) when (IsContextClosedError(ex))
        {
            logger.LogInformation(ex, "Persistent browser context was closed; recreating profile {ProfileName}", Path.GetFileName(profileDir));
            await InvalidatePersistentContextAsync();
            await EnsurePersistentContextAsync(profileDir, cancellationToken);

            if (_persistentContext == null)
            {
                throw new InvalidOperationException("Persistent browser context could not be recreated");
            }

            await _persistentLock.WaitAsync(cancellationToken);
            try
            {
                page = await GetOrCreatePersistentPageCoreAsync();
            }
            finally
            {
                _persistentLock.Release();
            }
        }

        return page;
    }

    private async Task<IPage> GetOrCreatePersistentPageCoreAsync()
    {
        if (_persistentContext == null)
        {
            throw new InvalidOperationException("Persistent browser context not initialized");
        }

        foreach (var existing in _persistentContext.Pages)
        {
            if (!existing.IsClosed && IsBlankStartupUrl(existing.Url) && !_inUsePersistentPages.Contains(existing))
            {
                TrackPersistentPage(existing);
                await CloseOrphanStartupPagesAsync(existing);
                return existing;
            }
        }

        var newPage = await _persistentContext.NewPageAsync();
        TrackPersistentPage(newPage);
        await CloseOrphanStartupPagesAsync(newPage);
        return newPage;
    }

    /// <summary>
    /// Manages pre-existing blank pages opened at persistent-context launch.
    /// Closes startup blank tabs so about:blank does not linger on screen after operations complete.
    /// </summary>
    private async Task CloseOrphanStartupPagesAsync(IPage keepPage)
    {
        if (_persistentContext == null)
        {
            return;
        }

        List<IPage> pages;
        try
        {
            pages = [.. _persistentContext.Pages];
        }
        catch (PlaywrightException ex) when (IsContextClosedError(ex))
        {
            logger.LogDebug(ex, "Persistent context closed while collecting orphan startup pages.");
            return;
        }

        var blankPages = new List<IPage>();
        foreach (var existing in pages)
        {
            if (existing == keepPage || existing.IsClosed || _inUsePersistentPages.Contains(existing))
            {
                continue;
            }

            string url = string.Empty;
            try
            {
                url = existing.Url ?? string.Empty;
            }
            catch (PlaywrightException ex) when (IsContextClosedError(ex))
            {
                continue;
            }

            if (IsBlankStartupUrl(url))
            {
                blankPages.Add(existing);
            }
        }

        // Close all orphan blank pages.
        foreach (var blankPage in blankPages)
        {
            try
            {
                await blankPage.CloseAsync();
            }
            catch (PlaywrightException ex)
            {
                logger.LogDebug(ex, "Could not close orphan startup page; it may have closed already.");
            }
        }
    }

    /// <summary>
    /// Keeps Playwright's browser lookup app-owned, then installs Chromium only for a clean
    /// runtime directory. This is shared by the headed ModDB profile and headless parsing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the browser runtime initialization.</returns>
    private async Task<IPlaywright> EnsureManagedPlaywrightAsync(CancellationToken cancellationToken)
    {
        var runtime = GetOrCreateManagedChromiumRuntime();
        runtime.ConfigureEnvironment();

        IPlaywright playwright;
        await _playwrightLock.WaitAsync(cancellationToken);
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            playwright = _playwright;
        }
        finally
        {
            _playwrightLock.Release();
        }

        await runtime.EnsureInstalledAsync(playwright.Chromium, cancellationToken);
        return playwright;
    }

    private ManagedChromiumRuntime GetOrCreateManagedChromiumRuntime()
    {
        if (managedChromiumRuntime != null)
        {
            return managedChromiumRuntime;
        }

        var newRuntime = new ManagedChromiumRuntime(
            Path.Combine(configurationProvider.GetApplicationDataPath(), DirectoryNames.BrowserRuntime),
            Microsoft.Playwright.Program.Main,
            RequestManagedChromiumInstallConsentAsync,
            logger);

        return Interlocked.CompareExchange(ref managedChromiumRuntime, newRuntime, null) ?? newRuntime;
    }

    /// <summary>
    /// Asks the user whether to download GenHub's managed Playwright Chromium runtime for web scraping
    /// and bot-protected content. System Chrome/Edge is not used — Playwright requires its own patched
    /// build under the app data directory.
    /// </summary>
    private async Task<bool> RequestManagedChromiumInstallConsentAsync(string runtimeDirectory)
    {
        var message =
            "GenHub requires a managed Chromium runtime (Playwright) for web scraping and downloads. " +
            "This is separate from any Chrome or Edge browser already installed on your PC — those cannot be used.\n\n" +
            "What will be installed (~240 MB download):\n" +
            "• Playwright Chromium browser\n" +
            "• Chromium Headless Shell\n" +
            "• FFmpeg and Windows helper binaries\n\n" +
            $"Install location:\n{runtimeDirectory}";

        async Task<bool> ShowAsync() =>
            await dialogService.ShowConfirmationAsync(
                "Install Web Browser Runtime",
                message,
                confirmText: "Install",
                cancelText: "Cancel");

        if (Dispatcher.UIThread.CheckAccess())
        {
            return await ShowAsync();
        }

        return await Dispatcher.UIThread.InvokeAsync(ShowAsync);
    }

    /// <summary>
    /// Fetches HTML in the persistent headed context, reusing the on-disk Cloudflare clearance
    /// cookie so bot-protected pages load without another manual challenge.
    /// </summary>
    /// <param name="profileName">The on-disk profile name.</param>
    /// <param name="url">The URL to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page HTML.</returns>
    private async Task<string> FetchPersistentHtmlAsync(string profileName, string url, CancellationToken cancellationToken)
    {
        ValidateProfileName(profileName);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Invalid URL (must be absolute HTTP/HTTPS): {url}", nameof(url));
        }

        if (string.Equals(profileName, ModDBConstants.BrowserProfileName, StringComparison.OrdinalIgnoreCase) && !IsHttpsModDbUrl(url))
        {
            throw new ArgumentException($"URL is not permitted for profile '{profileName}' (must be HTTPS ModDB): {url}", nameof(url));
        }

        logger.LogDebug("Fetching HTML (persistent profile '{Profile}') from {Url}", profileName, url);

        return await ExecuteInPersistentContextAsync(
            profileName,
            async () =>
            {
                var page = await CreatePersistentPageAsync(profileName, cancellationToken);
                try
                {
                    return await NavigatePersistentPageAsync(page, url, cancellationToken);
                }
                finally
                {
                    await ClosePersistentPageAsync(page);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Navigates an already-open persistent page and returns its HTML after ModDB (or generic)
    /// content is ready. Does not open or close the page — callers own the page lifetime.
    /// </summary>
    private async Task<string> NavigatePersistentPageAsync(IPage page, string url, CancellationToken cancellationToken)
    {
        await page.GotoAsync(url, new PageGotoOptions
        {
            Timeout = ModDBConstants.DefaultGotoTimeout,
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });

        if (IsModDbHost(url))
        {
            await WaitForModDbContentAsync(page, url, cancellationToken);
        }
        else
        {
            await Task.Delay(500, cancellationToken);
        }

        return await page.ContentAsync();
    }

    private void NotifyBrowserWindowOpening(string title, string message)
    {
        try
        {
            notificationService?.ShowInfo(title, message, NotificationDurations.VeryLong);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to show browser-window notification.");
        }
    }

    /// <summary>
    /// Waits for real ModDB content instead of snapshotting the Cloudflare interstitial. This is
    /// state-based waiting: a user can solve verification in the headed Chromium window and the
    /// parser resumes as soon as the actual document is available.
    /// </summary>
    private async Task WaitForModDbContentAsync(IPage page, string url, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        var verificationObserved = false;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var (ready, isVerification) = await ProbeForModDbContentOrVerificationAsync(page);
                if (isVerification)
                {
                    if (!verificationObserved)
                    {
                        logger.LogInformation(
                            "ModDB verification is open in Chromium for {Url}. Waiting for the user to complete it.",
                            url);
                        verificationObserved = true;
                    }
                }
                else if (ready)
                {
                    if (verificationObserved)
                    {
                        logger.LogInformation("ModDB verification completed; parsing content from {Url}", url);
                    }

                    return;
                }
            }
            catch (PlaywrightException ex) when (IsContextClosedError(ex))
            {
                // The page is navigating (e.g. the user just solved the Cloudflare challenge, or
                // the interstitial redirected to the real document). The in-flight title/selector
                // probe dies mid-navigation; retry on the next tick once the new document settles.
                logger.LogDebug(ex, "Transient navigation during ModDB content wait for {Url}; retrying", url);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new TimeoutException(
            verificationObserved
                ? "ModDB verification was not completed in the Chromium window. Complete the check and open the content again."
                : $"ModDB did not expose parseable content for {url}.");
    }

    private async Task HandlePageCloseUnderActiveContextAsync(IPage page)
    {
        try
        {
            var otherPages = _persistentContext!.Pages.Where(p => !p.IsClosed && p != page).ToList();

            if (_activePersistentSessions > 0)
            {
                await HandlePageCloseInActiveSessionAsync(page, otherPages.Count);
            }
            else
            {
                await HandlePageCloseOutsideActiveSessionAsync(page);
            }
        }
        catch (PlaywrightException ex) when (IsContextClosedError(ex))
        {
            ResetPersistentContextState();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to inspect persistent context after page close.");
            ResetPersistentContextState();
        }
    }

    private async Task HandlePageCloseInActiveSessionAsync(IPage page, int otherPageCount)
    {
        if (otherPageCount == 0 && _inUsePersistentPages.Count == 0)
        {
            if (!page.IsClosed)
            {
                try
                {
                    await page.GotoAsync("about:blank");
                }
                catch (PlaywrightException ex)
                {
                    logger.LogDebug(ex, "Failed navigating persistent page to blank.");
                }
            }
        }
        else
        {
            await SafeClosePageAsync(page);
        }
    }

    private async Task HandlePageCloseOutsideActiveSessionAsync(IPage page)
    {
        await SafeClosePageAsync(page);

        if (_inUsePersistentPages.Count == 0)
        {
            await ClosePersistentContextCoreUnderLockAsync();
        }
    }

    private async Task SafeClosePageAsync(IPage page)
    {
        if (!page.IsClosed)
        {
            try
            {
                await page.CloseAsync();
            }
            catch (PlaywrightException ex)
            {
                logger.LogDebug(ex, "Persistent page already closed during cleanup.");
            }
        }
    }

    private async Task FetchSinglePersistentDocumentAsync(
        string profileName,
        string url,
        System.Collections.Concurrent.ConcurrentDictionary<string, IDocument> concurrentResults,
        SemaphoreSlim tabSemaphore,
        CancellationToken cancellationToken)
    {
        await tabSemaphore.WaitAsync(cancellationToken);
        IPage? page = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            page = await CreatePersistentPageAsync(profileName, cancellationToken);

            var html = await NavigatePersistentPageAsync(page, url, cancellationToken);
            var doc = await OpenDocumentAsync(html, cancellationToken);
            concurrentResults[url] = doc;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Soft-fail per URL so one dead section does not abort the rest of the sweep.
            logger.LogWarning(ex, "Failed to fetch persistent URL in parallel batch: {Url}", url);
        }
        finally
        {
            if (page != null)
            {
                await ClosePersistentPageAsync(page);
            }

            tabSemaphore.Release();
        }
    }

    private async Task<DownloadResult> SaveDownloadFileAsync(
        IDownload download,
        GenHub.Core.Models.Common.DownloadConfiguration configuration,
        bool usePersistentModDbProfile,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        if (usePersistentModDbProfile && !IsHttpsModDbUrl(download.Url))
        {
            logger.LogWarning("Download URL {DownloadUrl} is not a valid HTTPS ModDB URL. Aborting download.", download.Url);
            throw new InvalidOperationException($"Download URL '{download.Url}' must be an HTTPS ModDB URL for persistent profile.");
        }

        if (File.Exists(configuration.DestinationPath) && configuration.OverwriteExisting)
        {
            File.Delete(configuration.DestinationPath);
        }

        var dir = Path.GetDirectoryName(configuration.DestinationPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var saveTimeout = configuration.Timeout > TimeSpan.Zero && configuration.Timeout != Timeout.InfiniteTimeSpan
            ? TimeSpan.FromMilliseconds(Math.Max(ValidationLimits.MinDownloadSaveTimeoutMs, configuration.Timeout.TotalMilliseconds))
            : Timeout.InfiniteTimeSpan;

        if (saveTimeout != Timeout.InfiniteTimeSpan)
        {
            linkedCts.CancelAfter(saveTimeout);
        }

        var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = linkedCts.Token.Register(() => cancelTcs.TrySetResult(true));

        var saveTask = download.SaveAsAsync(configuration.DestinationPath);
        var completedTask = await Task.WhenAny(saveTask, cancelTcs.Task);

        if (completedTask != saveTask)
        {
            try
            {
                await download.CancelAsync();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to cancel download after timeout or cancellation.");
            }

            CleanPartialOutputFile(configuration.DestinationPath);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Download timed out after {saveTimeout.TotalSeconds} seconds.");
        }

        try
        {
            await saveTask;
        }
        catch (Exception)
        {
            CleanPartialOutputFile(configuration.DestinationPath);
            throw;
        }

        var fileInfo = new FileInfo(configuration.DestinationPath);
        logger.LogInformation("Playwright download completed: {Path}, Size: {Size}", configuration.DestinationPath, fileInfo.Length);

        return DownloadResult.CreateSuccess(
            configuration.DestinationPath,
            fileInfo.Length,
            stopwatch.Elapsed,
            hashVerified: false);
    }

    private void CleanPartialOutputFile(string destinationPath)
    {
        try
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to clean partial output file at {DestinationPath}", destinationPath);
        }
    }

    private async Task CleanupDownloadPageAsync(IPage page, bool usePersistentModDbProfile)
    {
        if (usePersistentModDbProfile)
        {
            await ClosePersistentPageAsync(page);
        }
        else
        {
            var context = page.Context;
            try
            {
                if (!page.IsClosed)
                {
                    await page.CloseAsync();
                }
            }
            finally
            {
                if (context != null)
                {
                    try
                    {
                        await context.CloseAsync();
                    }
                    catch (PlaywrightException ex)
                    {
                        logger.LogDebug(ex, "Context already closed during download cleanup.");
                    }
                }
            }
        }
    }
}
