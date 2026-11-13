using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared helper for opening URLs in the system default browser.
/// </summary>
public static class BrowserHelper
{
    /// <summary>
    /// Validates an http(s) URL and opens it in the system default browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    /// <param name="logger">The caller's logger.</param>
    /// <returns>True when the browser launch was accepted.</returns>
    public static bool TryOpenUrl(string? url, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            logger.LogWarning("Invalid or unsafe URL: {Url}", url);
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ObjectDisposedException or PlatformNotSupportedException)
        {
            logger.LogError(ex, "Failed to open release URL: {Url}", url);
            return false;
        }
    }
}
