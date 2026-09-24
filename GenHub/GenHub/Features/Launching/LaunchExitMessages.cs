using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.GameProfile;
using System;
using System.Globalization;
using System.Resources;

namespace GenHub.Features.Launching;

/// <summary>Shared localized messages for launch completion.</summary>
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
            ? ProfileValidationConstants.EarlyExitWithCodeKey
            : ProfileValidationConstants.EarlyExitUnknownCodeKey;
        object?[] arguments = launch.ExitCode.HasValue ? [launch.ExitCode.Value] : [];
        return GetString(key, localization, arguments);
    }

    /// <summary>Resolves a launch message for both DI and standalone callers.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="localization">The application localization service, when available.</param>
    /// <param name="arguments">The format arguments.</param>
    /// <returns>The localized message, or its unformatted template if invalid.</returns>
    internal static string GetString(string key, ILocalizationService? localization, params object?[] arguments)
    {
        if (localization != null)
        {
            return localization.GetString(key, arguments);
        }

        // Preserve localization for callers constructing the launcher without application DI.
        var culture = CultureInfo.CurrentUICulture;
        var template = Resources.GetString(key, culture) ?? key;
        try
        {
            return string.Format(culture, template, arguments);
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
