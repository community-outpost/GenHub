using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;

namespace GenHub.Features.Content.Services.Tools;

/// <summary>
/// Optional dependencies for <see cref="ManagedPlaywrightDriver"/>.
/// </summary>
internal sealed record ManagedPlaywrightDriverOptions
{
    /// <summary>
    /// Gets the optional notification service for install progress.
    /// </summary>
    public INotificationService? NotificationService { get; init; }

    /// <summary>
    /// Gets the optional localization service for user-facing strings.
    /// </summary>
    public ILocalizationService? LocalizationService { get; init; }

    /// <summary>
    /// Gets the optional lifecycle callbacks and scope factory.
    /// </summary>
    public ManagedChromiumRuntimeCallbacks? Callbacks { get; init; }

    /// <summary>
    /// Gets the optional expected package hash override for tests.
    /// Production code always uses the pinned constant.
    /// </summary>
    public string? ExpectedPackageSha256 { get; init; }
}
