using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Notifications.ViewModels;

/// <summary>
/// Shared icon and brush mapping for notification view models.
/// </summary>
public static class NotificationBrushes
{
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse(NotificationConstants.InfoColor));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse(NotificationConstants.SuccessColor));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse(NotificationConstants.WarningColor));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse(NotificationConstants.ErrorColor));
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Colors.Gray);

    /// <summary>
    /// Gets the icon path data for a notification type.
    /// </summary>
    /// <param name="type">The notification type.</param>
    /// <returns>The icon path data.</returns>
    public static string GetIconPath(NotificationType type)
    {
        return type switch
        {
            NotificationType.Info => NotificationConstants.InfoIconPath,
            NotificationType.Success => NotificationConstants.SuccessIconPath,
            NotificationType.Warning => NotificationConstants.WarningIconPath,
            NotificationType.Error => NotificationConstants.ErrorIconPath,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Gets the background brush for a notification type.
    /// </summary>
    /// <param name="type">The notification type.</param>
    /// <returns>The background brush.</returns>
    public static IBrush GetBackgroundBrush(NotificationType type)
    {
        return type switch
        {
            NotificationType.Info => InfoBrush,
            NotificationType.Success => SuccessBrush,
            NotificationType.Warning => WarningBrush,
            NotificationType.Error => ErrorBrush,
            _ => DefaultBrush,
        };
    }
}
