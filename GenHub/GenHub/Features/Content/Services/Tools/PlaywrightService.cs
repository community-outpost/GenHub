using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace GenHub.Features.Content.Services.Tools;

/// <summary>
/// Service for managing Playwright browser instances and fetching web content.
/// Provides shared browser resources across the application.
/// </summary>
public class PlaywrightService(ILogger<PlaywrightService> logger) : IPlaywrightService, IAsyncDisposable
{
    private static readonly SemaphoreSlim _browserLock = new(1, 1);
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;

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
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        };

        var context = await _browser.NewContextAsync(contextOptions);
        return await context.NewPageAsync();
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
                await page.CloseAsync();
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
    public async ValueTask DisposeAsync()
    {
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

        _browserLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadFileAsync(GenHub.Core.Models.Common.DownloadConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Starting Playwright download from {Url}", configuration.Url);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var page = await CreatePageAsync(cancellationToken: cancellationToken);
            var context = page.Context;

            // Use a TaskCompletionSource to capture the download from the context level.
            // This handles cases where the download is triggered in a new tab/popup.
            var downloadTcs = new TaskCompletionSource<IDownload>();

            // Subscribe to the Download event on the page
            void DownloadHandler(object? sender, IDownload download)
            {
                downloadTcs.TrySetResult(download);
            }

            page.Download += DownloadHandler;

            try
            {
                // Trigger the download by navigating to the URL
                await page.GotoAsync(configuration.Url.ToString(), new PageGotoOptions
                {
                    Timeout = (float)configuration.Timeout.TotalMilliseconds,
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                });

                // Race the download TCS against a set delay to check if it auto-started
                // If it doesn't start quickly, we try to click a fallback link
                var waitTask = Task.Delay(5000, cancellationToken);
                var completedTask = await Task.WhenAny(downloadTcs.Task, waitTask);

                if (completedTask != downloadTcs.Task)
                {
                    logger.LogInformation("Download did not start automatically within 5s. Attempting to find fallback link...");

                    // Try to find a download link
                    var fallbackLink = await page.QuerySelectorAsync("a#download, a.download, a:has-text('Download'), a:has-text('download'), a:has-text('mirror')");

                    if (fallbackLink != null)
                    {
                        var text = await fallbackLink.InnerTextAsync();
                        logger.LogInformation("Found fallback link '{Text}', clicking...", text);
                        try
                        {
                            // Click with a short timeout, just to trigger the action
                            await fallbackLink.ClickAsync(new ElementHandleClickOptions { Timeout = 5000 });
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

                // Wait for the download to start with a generous timeout (60s or config timeout if larger)
                var waitTimeout = TimeSpan.FromMilliseconds(Math.Max(60000, configuration.Timeout.TotalMilliseconds));

                // We use WaitAsync (available in .NET 6+) or a custom timeout logic
                var download = await downloadTcs.Task.WaitAsync(waitTimeout, cancellationToken);

                if (download == null)
                {
                    return DownloadResult.CreateFailure("Download failed to initialize (null download object).");
                }

                var path = await download.PathAsync();

                if (File.Exists(configuration.DestinationPath) && configuration.OverwriteExisting)
                {
                    File.Delete(configuration.DestinationPath);
                }

                // Create directory if it doesn't exist
                var dir = Path.GetDirectoryName(configuration.DestinationPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // Save to destination
                await download.SaveAsAsync(configuration.DestinationPath);

                var fileInfo = new FileInfo(configuration.DestinationPath);

                logger.LogInformation("Playwright download completed: {Path}, Size: {Size}", configuration.DestinationPath, fileInfo.Length);

                return DownloadResult.CreateSuccess(
                    configuration.DestinationPath,
                    fileInfo.Length,
                    stopwatch.Elapsed,
                    hashVerified: false); // Hash verification typically happens in the service layer if needed
            }
            finally
            {
                // Unsubscribe and close
                page.Download -= DownloadHandler;
                await page.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Playwright download failed for {Url}", configuration.Url);
            return DownloadResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Ensures Playwright is initialized with a browser instance.
    /// </summary>
    private static async Task EnsurePlaywrightInitializedAsync(CancellationToken cancellationToken)
    {
        if (_browser != null) return;

        await _browserLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser != null) return;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--disable-blink-features=AutomationControlled"],
            });
        }
        finally
        {
            _browserLock.Release();
        }
    }
}