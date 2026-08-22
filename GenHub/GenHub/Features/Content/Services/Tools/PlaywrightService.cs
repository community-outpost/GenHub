using System;
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
public class PlaywrightService(
    ILogger<PlaywrightService> logger,
    IConfigurationProviderService configurationProvider,
    IDialogService dialogService,
    INotificationService? notificationService = null) : IPlaywrightService, IDisposable, IAsyncDisposable
{
    private const string StealthInitScript = """
        // 1. Mask navigator.webdriver
        try {
            Object.defineProperty(navigator, 'webdriver', {
                get: () => undefined,
                configurable: true,
            });
        } catch (e) {}

        // 2. Ensure window.chrome runtime object exists
        try {
            if (!window.chrome) {
                window.chrome = {
                    app: {
                        isInstalled: false,
                        InstallState: { DISABLED: 'disabled', INSTALLED: 'installed', NOT_INSTALLED: 'not_installed' },
                        RunningState: { CANNOT_RUN: 'cannot_run', READY_TO_RUN: 'ready_to_run', RUNNING: 'running' }
                    },
                    runtime: {
                        OnInstalledReason: { CHROME_UPDATE: 'chrome_update', INSTALL: 'install', SHARED_MODULE_UPDATE: 'shared_module_update', UPDATE: 'update' },
                        OnRestartRequiredReason: { APP_UPDATE: 'app_update', OS_UPDATE: 'os_update', PERIODIC: 'periodic' },
                        PlatformArch: { ARM: 'arm', ARM64: 'arm64', MIPS: 'mips', MIPS64: 'mips64', X86_32: 'x86-32', X86_64: 'x86-64' },
                        PlatformNaclArch: { ARM: 'arm', MIPS: 'mips', MIPS64: 'mips64', X86_32: 'x86-32', X86_64: 'x86-64' },
                        PlatformOs: { ANDROID: 'android', CROS: 'cros', LINUX: 'linux', MAC: 'mac', OPENBSD: 'openbsd', WIN: 'win' },
                        RequestUpdateCheckStatus: { NO_UPDATE: 'no_update', THROTTLED: 'throttled', UPDATE_AVAILABLE: 'update_available' }
                    },
                    loadTimes: function() {},
                    csi: function() {}
                };
            }
        } catch (e) {}

        // 3. Ensure navigator.plugins is non-empty
        try {
            if (!navigator.plugins || navigator.plugins.length === 0) {
                const dummyPlugin = {
                    name: 'PDF Viewer',
                    filename: 'internal-pdf-viewer',
                    description: 'Portable Document Format'
                };
                Object.defineProperty(navigator, 'plugins', {
                    get: () => [dummyPlugin],
                    configurable: true,
                });
            }
        } catch (e) {}

        // 4. Ensure navigator.languages is properly populated
        try {
            if (!navigator.languages || navigator.languages.length === 0) {
                Object.defineProperty(navigator, 'languages', {
                    get: () => ['en-US', 'en'],
                    configurable: true,
                });
            }
        } catch (e) {}

        // 5. Mock notification permissions
        try {
            if (navigator.permissions && navigator.permissions.query) {
                const originalQuery = navigator.permissions.query;
                navigator.permissions.query = function(parameters) {
                    if (parameters && parameters.name === 'notifications') {
                        return Promise.resolve({ state: Notification.permission });
                    }
                    return originalQuery.apply(this, arguments);
                };
            }
        } catch (e) {}
        """;

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

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _persistentContext;
    private string? _persistentProfileName;
    private int _activePersistentSessions;

    private ManagedChromiumRuntime? managedChromiumRuntime;

    private bool _disposed;

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
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
            Locale = "en-US",
        };

        var context = await _browser.NewContextAsync(contextOptions);
        await context.AddInitScriptAsync(StealthInitScript);

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
            catch (PlaywrightException)
            {
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IPage> CreatePersistentPageAsync(string profileName, CancellationToken cancellationToken = default)
    {
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
            return;
        }

        await _persistentLock.WaitAsync();
        try
        {
            _inUsePersistentPages.Remove(page);

            if (_persistentContext != null && IsPersistentContextAlive())
            {
                try
                {
                    var otherPages = _persistentContext.Pages.Where(p => !p.IsClosed && p != page).ToList();

                    if (_activePersistentSessions > 0)
                    {
                        // inside an active session: if this is the last page, navigate to about:blank
                        // so chromium remains alive for subsequent steps within the session.
                        if (otherPages.Count == 0 && _inUsePersistentPages.Count == 0)
                        {
                            if (!page.IsClosed)
                            {
                                try
                                {
                                    await page.GotoAsync("about:blank");
                                }
                                catch (PlaywrightException)
                                {
                                }
                            }
                        }
                        else if (!page.IsClosed)
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
                    else
                    {
                        // outside an active session: close page and immediately shut down persistent context
                        // once all active pages finish.
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

                        if (_inUsePersistentPages.Count == 0)
                        {
                            await ClosePersistentContextCoreUnderLockAsync();
                        }
                    }
                }
                catch (PlaywrightException ex) when (IsContextClosedError(ex))
                {
                    _inUsePersistentPages.Clear();
                    _persistentContext = null;
                    _persistentProfileName = null;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to inspect persistent context after page close.");
                    _inUsePersistentPages.Clear();
                    _persistentContext = null;
                    _persistentProfileName = null;
                }
            }
            else
            {
                if (!page.IsClosed)
                {
                    try
                    {
                        await page.CloseAsync();
                    }
                    catch (PlaywrightException ex)
                    {
                        logger.LogDebug(ex, "Failed to close page cleanly");
                    }
                }
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
        try
        {
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
                    await page.CloseAsync();
                }
                finally
                {
                    if (context != null)
                    {
                        await context.CloseAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch HTML from {Url}", url);
            throw;
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

        var results = new Dictionary<string, IDocument>(StringComparer.Ordinal);
        if (urls.Count == 0)
        {
            return results;
        }

        // Preserve caller order while skipping duplicate URLs (section sweeps sometimes repeat a path).
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

        if (orderedUnique.Count == 0)
        {
            return results;
        }

        var isOuterSession = !_isInPersistentSession.Value;
        if (isOuterSession)
        {
            await _persistentFetchLock.WaitAsync(cancellationToken);
            _isInPersistentSession.Value = true;
        }

        try
        {
            logger.LogDebug(
                "Fetching {Count} URL(s) in parallel on persistent context (profile '{Profile}')",
                orderedUnique.Count,
                profileName);

            var profileDir = Path.Combine(
                configurationProvider.GetApplicationDataPath(),
                DirectoryNames.BrowserProfiles,
                profileName);

            await EnsurePersistentContextAsync(profileDir, cancellationToken);
            if (_persistentContext == null)
            {
                throw new InvalidOperationException("Persistent browser context not initialized");
            }

            var concurrentResults = new System.Collections.Concurrent.ConcurrentDictionary<string, IDocument>(StringComparer.Ordinal);

            // Limit parallel tabs to avoid overwhelming system resources (max 5 parallel tabs)
            using var tabSemaphore = new SemaphoreSlim(Math.Min(orderedUnique.Count, 5));

            var tasks = orderedUnique.Select(async url =>
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
            });

            await Task.WhenAll(tasks);

            foreach (var kvp in concurrentResults)
            {
                results[kvp.Key] = kvp.Value;
            }
        }
        finally
        {
            if (isOuterSession)
            {
                if (_activePersistentSessions == 0 && _inUsePersistentPages.Count == 0)
                {
                    await ClosePersistentContextCoreAsync();
                }

                _isInPersistentSession.Value = false;
                _persistentFetchLock.Release();
            }
        }

        return results;
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
        if (_disposed)
        {
            return;
        }

        Task.Run(() => DisposeAsync().AsTask()).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_persistentContext != null)
        {
            try
            {
                await _persistentContext.CloseAsync();
            }
            catch
            {
            }
            finally
            {
                _inUsePersistentPages.Clear();
                _persistentContext = null;
                _persistentProfileName = null;
            }
        }

        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        if (_playwright != null)
        {
            _playwright.Dispose();
            _playwright = null;
        }

        _disposed = true;
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadFileAsync(GenHub.Core.Models.Common.DownloadConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Starting Playwright download from {Url}", configuration.Url);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var usePersistentModDbProfile = configuration.Url.Host.Equals("moddb.com", StringComparison.OrdinalIgnoreCase) ||
                                            configuration.Url.Host.EndsWith(".moddb.com", StringComparison.OrdinalIgnoreCase);

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
                    if (_activePersistentSessions == 0 && _inUsePersistentPages.Count == 0)
                    {
                        await ClosePersistentContextCoreAsync();
                    }

                    _isInPersistentSession.Value = false;
                    _persistentFetchLock.Release();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Playwright download failed for {Url}", configuration.Url);
            var message = ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase)
                ? "The bundled Chromium runtime is unavailable. Install the application browser runtime and retry the ModDB download."
                : ex.Message;
            return DownloadResult.CreateFailure(message, bytesDownloaded: 0, elapsed: TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Builds headers safe to apply to a Playwright page.
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

    private static bool IsBlankStartupUrl(string url) =>
        string.IsNullOrWhiteSpace(url) ||
        url.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("chrome://newtab", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("chrome://new-tab", StringComparison.OrdinalIgnoreCase);

    private static bool IsModDbVerificationPage(string? title) =>
        !string.IsNullOrWhiteSpace(title) &&
        (title.Contains("Just a moment", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Attention Required", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Verify you are human", StringComparison.OrdinalIgnoreCase) ||
         title.Contains("Cloudflare", StringComparison.OrdinalIgnoreCase));

    private static async Task<IDocument> OpenDocumentAsync(string html, CancellationToken cancellationToken)
    {
        var browsingContext = BrowsingContext.New(Configuration.Default);
        return await browsingContext.OpenAsync(req => req.Content(html), cancellationToken);
    }

    /// <summary>
    /// A persistent context is only reusable while it is live and still owns at least one open
    /// page. A headed Chromium process exits once its final page closes, after which the cached
    /// IBrowserContext reference throws on every call. Touching <see cref="IBrowserContext.Pages"/>
    /// is the cheapest probe: it fails on a dead channel and succeeds on a live one.
    /// </summary>
    private bool IsPersistentContextAlive()
    {
        if (_persistentContext == null)
        {
            return false;
        }

        try
        {
            // Any property/method round-trip to the dead channel raises TargetClosedException.
            _ = _persistentContext.Pages;
            return true;
        }
        catch (PlaywrightException ex) when (IsContextClosedError(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Clears a closed persistent browser context so the next request launches a fresh one.
    /// </summary>
    private async Task InvalidatePersistentContextAsync()
    {
        await _persistentLock.WaitAsync();
        try
        {
            _inUsePersistentPages.Clear();
            _persistentContext = null;
            _persistentProfileName = null;
        }
        finally
        {
            _persistentLock.Release();
        }
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
                    catch (PlaywrightException ex)
                    {
                        logger.LogDebug(ex, "Failed to close open page during context teardown");
                    }
                }

                await _persistentContext.CloseAsync();
            }
            catch (PlaywrightException ex)
            {
                logger.LogDebug(ex, "Failed to close persistent context cleanly");
            }
            finally
            {
                _inUsePersistentPages.Clear();
                _persistentContext = null;
                _persistentProfileName = null;
            }
        }
    }

    /// <summary>
    /// Closes the persistent browser context and acquires _persistentLock.
    /// </summary>
    private async Task ClosePersistentContextCoreAsync()
    {
        await _persistentLock.WaitAsync();
        try
        {
            await ClosePersistentContextCoreUnderLockAsync();
        }
        finally
        {
            _persistentLock.Release();
        }
    }

    private async Task<DownloadResult> DownloadFileCoreAsync(
        GenHub.Core.Models.Common.DownloadConfiguration configuration,
        bool usePersistentModDbProfile,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var page = usePersistentModDbProfile
            ? await CreatePersistentPageAsync(ModDBConstants.BrowserProfileName, cancellationToken)
            : await CreatePageAsync(cancellationToken: cancellationToken);

        List<IPage> popups = [];
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
            await page.GotoAsync(configuration.Url.ToString(), new PageGotoOptions
            {
                Timeout = (float)configuration.Timeout.TotalMilliseconds,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });

            // Race the download TCS against a short delay to check if it auto-started. If not,
            // try to click a fallback download link on the page (mirrors, manual Download btn).
            var waitTask = Task.Delay(5000, cancellationToken);
            var completedTask = await Task.WhenAny(downloadTcs.Task, waitTask);

            if (completedTask != downloadTcs.Task)
            {
                logger.LogInformation("Download did not start automatically within 5s. Attempting to find fallback link...");

                const string FallbackSelector = "a[href*='media.moddb.com'], a[href*='files.moddb.com'], a:has-text('click here'), a:has-text('Click here'), a:has-text('here'), a#download, a.download, a.btn-download, a.buttondownload, a[href*='/mirror/'], a[href*='/downloads/start/'], a[href*='/addons/start/'], a:has-text('Download Now'), a:has-text('download now'), a:has-text('Download'), a:has-text('download'), a:has-text('mirror')";

                var fallbackLink = await page.QuerySelectorAsync(FallbackSelector);

                if (fallbackLink != null)
                {
                    var text = await fallbackLink.InnerTextAsync();
                    logger.LogInformation("Found fallback link '{Text}', clicking...", text);
                    try
                    {
                        await fallbackLink.ClickAsync(new ElementHandleClickOptions { Timeout = 5000 });

                        // If clicking navigated to a /start/ page, wait briefly and check for direct mirror link
                        var secondWait = Task.Delay(4000, cancellationToken);
                        var secondCompleted = await Task.WhenAny(downloadTcs.Task, secondWait);
                        if (secondCompleted != downloadTcs.Task && !string.IsNullOrWhiteSpace(page.Url) && page.Url.Contains("/start/", StringComparison.OrdinalIgnoreCase))
                        {
                            var startPageFallback = await page.QuerySelectorAsync("a:has-text('click here'), a:has-text('Click here'), a[href*='media.moddb.com'], a[href*='files.moddb.com'], a[href*='/mirror/']");
                            if (startPageFallback != null)
                            {
                                var startText = await startPageFallback.InnerTextAsync();
                                logger.LogInformation("Found start-page mirror link '{Text}', clicking...", startText);
                                await startPageFallback.ClickAsync(new ElementHandleClickOptions { Timeout = 5000 });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to click fallback link.");
                    }
                }
                else
                {
                    logger.LogWarning("No fallback download link found. Continuing to wait for download event...");
                }
            }

            // Wait for the download to START (not finish) with a generous timeout. ModDB's
            // redirect chain plus Cloudflare can take a while before the binary begins streaming.
            var waitTimeout = TimeSpan.FromMilliseconds(Math.Max(60000, configuration.Timeout.TotalMilliseconds));
            var download = await downloadTcs.Task.WaitAsync(waitTimeout, cancellationToken);

            if (download == null)
            {
                return DownloadResult.CreateFailure("Download failed to initialize (null download object).");
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

            // Wait for the full byte stream to land on disk before reporting success. SaveAsAsync
            // streams the download into the destination; without awaiting it the file would be
            // incomplete (or missing) and downstream extraction would fail.
            await download.SaveAsAsync(configuration.DestinationPath);

            var fileInfo = new FileInfo(configuration.DestinationPath);
            logger.LogInformation("Playwright download completed: {Path}, Size: {Size}", configuration.DestinationPath, fileInfo.Length);

            return DownloadResult.CreateSuccess(
                configuration.DestinationPath,
                fileInfo.Length,
                stopwatch.Elapsed,
                hashVerified: false);
        }
        finally
        {
            page.Popup -= OnPopup;
            page.Download -= OnDownload;
            foreach (var popup in popups)
            {
                popup.Download -= OnDownload;
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

    /// <summary>
    /// Ensures Playwright is initialized with a browser instance.
    /// </summary>
    private async Task EnsurePlaywrightInitializedAsync(CancellationToken cancellationToken)
    {
        if (_browser != null) return;

        await _browserLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser != null) return;

            await EnsureManagedPlaywrightAsync(cancellationToken);
            if (_playwright == null)
            {
                throw new InvalidOperationException("Playwright is not initialized");
            }

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
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
                _inUsePersistentPages.Clear();
                _persistentContext = null;
                _persistentProfileName = null;
            }
            else if (string.Equals(_persistentProfileName, profileDir, StringComparison.Ordinal))
            {
                return;
            }
            else if (_persistentContext != null)
            {
                await ClosePersistentContextCoreUnderLockAsync();
            }

            await EnsureManagedPlaywrightAsync(cancellationToken);
            if (_playwright == null)
            {
                throw new InvalidOperationException("Playwright is not initialized");
            }

            Directory.CreateDirectory(profileDir);

            var context = await _playwright.Chromium.LaunchPersistentContextAsync(
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
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                    ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
                    Locale = "en-US",
                    IgnoreDefaultArgs = ["--enable-automation"],
                });

            await context.AddInitScriptAsync(StealthInitScript);

            var ctx = context;
            context.Close += (_, _) =>
            {
                if (ReferenceEquals(_persistentContext, ctx))
                {
                    _inUsePersistentPages.Clear();
                    _persistentContext = null;
                    _persistentProfileName = null;
                }
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
            logger.LogInformation("Persistent browser context was closed; recreating profile {ProfileName}", Path.GetFileName(profileDir));
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
                _inUsePersistentPages.Add(existing);
                await CloseOrphanStartupPagesAsync(existing);
                return existing;
            }
        }

        var newPage = await _persistentContext.NewPageAsync();
        _inUsePersistentPages.Add(newPage);
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

        List<IPage> pages = [];
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
    private async Task EnsureManagedPlaywrightAsync(CancellationToken cancellationToken)
    {
        await _playwrightLock.WaitAsync(cancellationToken);
        try
        {
            var runtime = GetOrCreateManagedChromiumRuntime();
            runtime.ConfigureEnvironment();
            _playwright ??= await Playwright.CreateAsync();
            await runtime.EnsureInstalledAsync(_playwright.Chromium, cancellationToken);
        }
        finally
        {
            _playwrightLock.Release();
        }
    }

    private ManagedChromiumRuntime GetOrCreateManagedChromiumRuntime()
    {
        return managedChromiumRuntime ??= new ManagedChromiumRuntime(
            Path.Combine(configurationProvider.GetApplicationDataPath(), DirectoryNames.BrowserRuntime),
            Microsoft.Playwright.Program.Main,
            RequestManagedChromiumInstallConsentAsync,
            logger,
            notificationService);
    }

    /// <summary>
    /// Asks the user (via the same confirmation dialog used for profile deletion) whether to
    /// download GenHub's managed Playwright Chromium runtime for ModDB. System Chrome/Edge is
    /// not used — Playwright requires its own patched build under the app data directory.
    /// </summary>
    private async Task<bool> RequestManagedChromiumInstallConsentAsync(string runtimeDirectory)
    {
        var message =
            "ModDB requires GenHub's managed Chromium runtime (Playwright). " +
            "This is separate from any Chrome or Edge browser already installed on your PC — those cannot be used.\n\n" +
            "What will be installed (~240 MB download):\n" +
            "• Playwright Chromium browser\n" +
            "• Chromium Headless Shell\n" +
            "• FFmpeg and Windows helper binaries\n\n" +
            $"Install location:\n{runtimeDirectory}";

        async Task<bool> ShowAsync() =>
            await dialogService.ShowConfirmationAsync(
                "Install Chromium Runtime for ModDB",
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
        logger.LogDebug("Fetching HTML (persistent profile '{Profile}') from {Url}", profileName, url);

        var isOuterSession = !_isInPersistentSession.Value;
        if (isOuterSession)
        {
            await _persistentFetchLock.WaitAsync(cancellationToken);
            _isInPersistentSession.Value = true;
        }

        try
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

        if (url.Contains("moddb.com", StringComparison.OrdinalIgnoreCase))
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
                if (await CheckModDbContentReadyAsync(page, url, verificationObserved))
                {
                    return;
                }

                var title = await page.TitleAsync();
                if (IsModDbVerificationPage(title) && !verificationObserved)
                {
                    logger.LogInformation(
                        "ModDB verification is open in Chromium for {Url}. Waiting for the user to complete it.",
                        url);
                    verificationObserved = true;
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

    private async Task<bool> CheckModDbContentReadyAsync(IPage page, string url, bool verificationObserved)
    {
        var title = await page.TitleAsync();
        if (IsModDbVerificationPage(title))
        {
            return false;
        }

        var contentMarker = await page.QuerySelectorAsync("#downloadsinfo, .row.rowcontent, .headerbox, #articlebrowse, #profile");
        if (contentMarker == null)
        {
            return false;
        }

        if (verificationObserved)
        {
            logger.LogInformation("ModDB verification completed; parsing content from {Url}", url);
        }

        return true;
    }
}
