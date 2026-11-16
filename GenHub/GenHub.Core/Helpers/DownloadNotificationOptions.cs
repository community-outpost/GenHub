namespace GenHub.Core.Helpers;

/// <summary>
/// Options for <see cref="DownloadNotificationScope"/>.
/// </summary>
/// <param name="StartTitle">Optional pinned toast title override. Defaults to the localized downloading title.</param>
/// <param name="StartMessage">Optional pinned toast message override. Defaults to the localized connecting message.</param>
/// <param name="ShowStartToast">Whether to show the pinned start toast and live updates. When false, reports only reach the chained sink.</param>
/// <param name="ShowTerminalToast">Whether completion methods show a terminal toast. Set false when the caller owns terminal messaging.</param>
public sealed record DownloadNotificationOptions(
    string? StartTitle = null,
    string? StartMessage = null,
    bool ShowStartToast = true,
    bool ShowTerminalToast = true);
