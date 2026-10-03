using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using System;

namespace GenHub.Core.Helpers;

/// <summary>
/// The cross-OS launch guard matrix shared by the launch runners and profile launch
/// validation: macOS targets run only on macOS, Linux targets only on Linux, and
/// Windows targets run natively on Windows or through a compatibility runner elsewhere.
/// </summary>
public static class LaunchGuardMessages
{
    /// <summary>
    /// Returns the localized guard error for a target/host mismatch, or <c>null</c>
    /// when the target may launch on this host.
    /// </summary>
    /// <param name="platform">The detected target platform.</param>
    /// <param name="localizationService">Optional localization; English fallback when absent.</param>
    /// <returns>The guard error, or <c>null</c> when launch may proceed.</returns>
    public static string? GetCrossOsError(ExecutablePlatform platform, ILocalizationService? localizationService)
    {
        if (OperatingSystem.IsWindows())
        {
            if (platform == ExecutablePlatform.MacOS)
            {
                return Text(LaunchMessageConstants.MacOSOnWindowsKey, LaunchMessageConstants.MacOSOnWindows, localizationService);
            }

            if (platform == ExecutablePlatform.Linux)
            {
                return Text(LaunchMessageConstants.LinuxOnWindowsKey, LaunchMessageConstants.LinuxOnWindows, localizationService);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (platform == ExecutablePlatform.MacOS)
            {
                return Text(LaunchMessageConstants.MacOSOnLinuxKey, LaunchMessageConstants.MacOSOnLinux, localizationService);
            }
        }
        else if (OperatingSystem.IsMacOS() && platform == ExecutablePlatform.Linux)
        {
            return Text(LaunchMessageConstants.LinuxOnMacOSKey, LaunchMessageConstants.LinuxOnMacOS, localizationService);
        }

        return null;
    }

    /// <summary>
    /// Localizes a message key with an English fallback.
    /// </summary>
    /// <param name="localizationService">Optional localization; English fallback when absent.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The English fallback template.</param>
    /// <param name="arguments">Format arguments.</param>
    /// <returns>The localized or fallback message.</returns>
    public static string Localize(ILocalizationService? localizationService, string key, string fallback, params object?[] arguments)
    {
        if (localizationService?.TryGetString(key, out var localized, arguments) == true)
        {
            return localized;
        }

        return arguments.Length == 0 ? fallback : string.Format(fallback, arguments);
    }

    private static string Text(string key, string fallback, ILocalizationService? localizationService)
    {
        return Localize(localizationService, key, fallback);
    }
}
