namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the standardized download notification lifecycle.
/// Every user-visible download follows the same three phases: pinned start toast,
/// throttled progress updates, and guaranteed dismissal plus a single terminal toast.
/// </summary>
public static class DownloadNotificationConstants
{
    /// <summary>
    /// Localization key for the pinned download title, formatted with the content name.
    /// </summary>
    public const string DownloadingTitleKey = "Downloads.Notification.Downloading.Title";

    /// <summary>
    /// Fallback pinned download title, formatted with the content name.
    /// </summary>
    public const string DownloadingTitleFormat = "Downloading {0}";

    /// <summary>
    /// Localization key for the initial pinned download message shown before progress arrives.
    /// </summary>
    public const string ConnectingMessageKey = "Downloads.Notification.Connecting.Message";

    /// <summary>
    /// Fallback initial pinned download message shown before progress arrives.
    /// </summary>
    public const string ConnectingMessage = "Connecting...";

    /// <summary>
    /// Localization key for live progress updates, formatted with percentage and status.
    /// </summary>
    public const string ProgressMessageKey = "Downloads.Notification.Progress.Message";

    /// <summary>
    /// Fallback live progress message, formatted with percentage and status.
    /// </summary>
    public const string ProgressMessageFormat = "{0}% - {1}";

    /// <summary>
    /// Localization key for the success terminal toast title.
    /// </summary>
    public const string CompleteTitleKey = "Downloads.Notification.Complete.Title";

    /// <summary>
    /// Fallback success terminal toast title.
    /// </summary>
    public const string CompleteTitle = "Download Complete";

    /// <summary>
    /// Localization key for the success terminal toast message, formatted with the content name.
    /// </summary>
    public const string CompleteMessageKey = "Downloads.Notification.Complete.Message";

    /// <summary>
    /// Fallback success terminal toast message, formatted with the content name.
    /// </summary>
    public const string CompleteMessageFormat = "Downloaded {0}";

    /// <summary>
    /// Localization key for the failure terminal toast title, formatted with the content name.
    /// </summary>
    public const string FailedTitleKey = "Downloads.Notification.Failed.Title";

    /// <summary>
    /// Fallback failure terminal toast title, formatted with the content name.
    /// </summary>
    public const string FailedTitleFormat = "Download Failed: {0}";

    /// <summary>
    /// Localization key for the fallback failure detail when no error message is available.
    /// </summary>
    public const string UnknownErrorKey = "Downloads.Notification.UnknownError";

    /// <summary>
    /// Fallback failure detail when no error message is available.
    /// </summary>
    public const string UnknownErrorMessage = "An unknown error occurred during download.";

    /// <summary>
    /// Localization key for the cancellation terminal toast title.
    /// </summary>
    public const string CanceledTitleKey = "Downloads.Notification.Canceled.Title";

    /// <summary>
    /// Fallback cancellation terminal toast title.
    /// </summary>
    public const string CanceledTitle = "Download Canceled";

    /// <summary>
    /// Localization key for the cancellation terminal toast message, formatted with the content name.
    /// </summary>
    public const string CanceledMessageKey = "Downloads.Notification.Canceled.Message";

    /// <summary>
    /// Fallback cancellation terminal toast message, formatted with the content name.
    /// </summary>
    public const string CanceledMessageFormat = "Canceled download for {0}.";
}
