using AngleSharp.Dom;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Service for managing Playwright browser instances and fetching web content.
/// Provides shared browser resources across the application.
/// </summary>
public interface IPlaywrightService
{
    /// <summary>
    /// Creates a new browser page with optional context options.
    /// </summary>
    /// <param name="options">Browser context options (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new IPage instance.</returns>
    Task<IPage> CreatePageAsync(BrowserNewContextOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a page in a persistent, headed browser context whose cookies and storage survive
    /// across calls. Use this for bot-protected sites (e.g. ModDB's Cloudflare): the user solves
    /// the challenge once, the resulting clearance cookie is persisted to disk, and subsequent
    /// pages in the same session (and across app restarts, until the cookie expires) load without
    /// another challenge. A real browser window is shown while the challenge is pending.
    /// </summary>
    /// <param name="profileName">The on-disk profile name (scoped under the app data browser-profile root).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new <see cref="IPage"/> in the persistent context.</returns>
    Task<IPage> CreatePersistentPageAsync(string profileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a page from <see cref="CreatePersistentPageAsync"/> and shuts down the headed Chromium
    /// window when no active pages remain. Prefer this over <c>page.CloseAsync</c> alone so
    /// callers do not leave an about:blank window open after a successful ModDB scrape.
    /// </summary>
    /// <param name="page">The persistent-context page to close.</param>
    /// <param name="keepOpen">
    /// When <see langword="true"/>, leaves the page open (e.g. so the user can finish a Cloudflare
    /// challenge) without closing the browser.
    /// </param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ClosePersistentPageAsync(IPage page, bool keepOpen = false);

    /// <summary>
    /// Fetches HTML content from a URL using Playwright.
    /// </summary>
    /// <param name="url">The URL to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The HTML content of the page.</returns>
    Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and parses a web page using AngleSharp.
    /// </summary>
    /// <param name="url">The URL to fetch and parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A parsed AngleSharp IDocument.</returns>
    Task<IDocument> FetchAndParseAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and parses a web page in a persistent, headed browser context whose cookies survive
    /// across calls. Use this for bot-protected URLs (e.g. ModDB) so the Cloudflare clearance cookie
    /// obtained from a single manual challenge solve is reused.
    /// </summary>
    /// <param name="profileName">The on-disk profile name (scoped under the app data browser-profile root).</param>
    /// <param name="url">The URL to fetch and parse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A parsed AngleSharp IDocument.</returns>
    Task<IDocument> FetchAndParsePersistentAsync(string profileName, string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches and parses multiple URLs within an active persistent headed browser context, using
    /// bounded parallel tabs (up to 5 concurrent tabs) so Chromium does not churn processes, and
    /// so context teardown cannot occur mid-batch.
    /// </summary>
    /// <param name="profileName">The on-disk profile name (scoped under the app data browser-profile root).</param>
    /// <param name="urls">URLs to fetch. Duplicates are fetched once; order of first occurrence is preserved in results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A map of URL → parsed document for every URL that loaded successfully. Failed URLs are omitted;
    /// callers should treat a missing key as a soft failure for that section.
    /// </returns>
    Task<IReadOnlyDictionary<string, IDocument>> FetchAndParsePersistentManyAsync(
        string profileName,
        IReadOnlyList<string> urls,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a file using Playwright to handle complex scenarios (like anti-bot protections).
    /// </summary>
    /// <param name="configuration">The download configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A DownloadResult indicating success or failure.</returns>
    Task<DownloadResult> DownloadFileAsync(DownloadConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an operation within a scoped persistent browser context session.
    /// The persistent browser window stays open for the duration of the operation and closes
    /// immediately when the operation completes, avoiding multiple window launches and idle delays.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="profileName">The on-disk profile name.</param>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    Task<T> ExecuteInPersistentContextAsync<T>(
        string profileName,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously pre-warms the Playwright driver runtime in the background so subsequent
    /// browser operations launch with minimal latency.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the background warmup operation.</returns>
    Task WarmupAsync(CancellationToken cancellationToken = default);
}
