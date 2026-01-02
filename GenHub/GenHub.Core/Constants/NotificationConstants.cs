namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the notification system.
/// </summary>
public static class NotificationConstants
{
    /// <summary>
    /// Default auto-dismiss timeout in milliseconds.
    /// </summary>
    public const int DefaultAutoDismissMs = 5000;

    /// <summary>
    /// Maximum number of notifications to keep in history.
    /// </summary>
    public const int MaxHistorySize = 100;

    /// <summary>
    /// Animation duration for fade-in in seconds.
    /// </summary>
    public const double FadeInDurationSeconds = 0.3;

    /// <summary>
    /// Animation duration for fade-out in seconds.
    /// </summary>
    public const double FadeOutDurationSeconds = 0.2;

    /// <summary>
    /// Main icon size in pixels.
    /// </summary>
    public const double MainIconSize = 24;

    /// <summary>
    /// Close button icon size in pixels.
    /// </summary>
    public const double CloseIconSize = 16;

    /// <summary>
    /// Drop shadow blur radius.
    /// </summary>
    public const double ShadowBlurRadius = 16;

    /// <summary>
    /// Drop shadow offset Y.
    /// </summary>
    public const double ShadowOffsetY = 4;

    /// <summary>
    /// Drop shadow opacity.
    /// </summary>
    public const double ShadowOpacity = 0.3;

    /// <summary>
    /// Info notification color.
    /// </summary>
    public const string InfoColor = "#3498db";

    /// <summary>
    /// Success notification color.
    /// </summary>
    public const string SuccessColor = "#27ae60";

    /// <summary>
    /// Warning notification color.
    /// </summary>
    public const string WarningColor = "#f39c12";

    /// <summary>
    /// Error notification color.
    /// </summary>
    public const string ErrorColor = "#e74c3c";

    /// <summary>
    /// Info icon path data.
    /// </summary>
    public const string InfoIconPath = "M13,9H11V7H13M13,17H11V11H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z";

    /// <summary>
    /// Success icon path data.
    /// </summary>
    public const string SuccessIconPath = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M11,16.5L6.5,12L7.91,10.59L11,13.67L16.59,8.09L18,9.5L11,16.5";

    /// <summary>
    /// Warning icon path data.
    /// </summary>
    public const string WarningIconPath = "M12,2L1,21H23M12,6L19.53,19H4.47M11,10V14H13V10M11,16V18H13V16";

    /// <summary>
    /// Error icon path data.
    /// </summary>
    public const string ErrorIconPath = "M12,2C17.53,2 22,6.47 22,12C22,17.53 17.53,22 12,22C6.47,22 2,17.53 2,12C2,6.47 6.47,2 12,2M15.59,7L12,10.59L8.41,7L7,8.41L10.59,12L7,15.59L8.41,17L12,13.41L15.59,17L17,15.59L13.41,12L17,8.41L15.59,7Z";

    /// <summary>
    /// Close button icon path data.
    /// </summary>
    public const string CloseIconPath = "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z";
}