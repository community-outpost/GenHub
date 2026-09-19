using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.GameProfile;
using System.Globalization;
using System.Resources;

namespace GenHub.Features.Launching;

/// <summary>Shared localized messages for a process that exits before launch completes.</summary>
internal static class LaunchExitMessages
{
    private static readonly ResourceManager Resources = new(LocalizationConstants.StringResourceBaseName, typeof(LaunchExitMessages).Assembly);

    /// <summary>Describes the early exit without presenting a clean exit as a crash.</summary>
    /// <param name="launch">The launch whose diagnostic history remains in the registry.</param>
    /// <param name="localization">The application localization service, when available.</param>
    /// <returns>A localized message with the observed exit code when known.</returns>
    internal static string Describe(GameLaunchInfo launch, ILocalizationService? localization)
    {
        var key = launch.ExitCode.HasValue
            ? "GameProfiles.Notification.EarlyExit.WithCode"
            : "GameProfiles.Notification.EarlyExit.UnknownCode";
        object?[] arguments = launch.ExitCode.HasValue ? [launch.ExitCode.Value] : [];
        if (localization != null)
        {
            return localization.GetString(key, arguments);
        }

        // Preserve localization for callers constructing the launcher without application DI.
        var culture = CultureInfo.CurrentUICulture;
        return string.Format(culture, Resources.GetString(key, culture) ?? key, arguments);
    }
}
