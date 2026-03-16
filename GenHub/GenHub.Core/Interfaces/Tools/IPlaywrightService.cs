using AngleSharp.Dom;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results;
using Microsoft.Playwright;
using System;
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
    /// Downloads a file using Playwright to handle complex scenarios (like anti-bot protections).
    /// </summary>
    /// <param name="configuration">The download configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A DownloadResult indicating success or failure.</returns>
    Task<DownloadResult> DownloadFileAsync(DownloadConfiguration configuration, CancellationToken cancellationToken = default);
}
