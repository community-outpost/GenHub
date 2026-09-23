using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace GenHub.Features.Launching;

/// <summary>
/// Shared launch-target preparation for the game launch runners: bundle roots resolve
/// to their executables, and Flatpak bundles fail with install-then-run guidance that
/// names the bundle's real application ID.
/// </summary>
internal static class RunnerTargetResolver
{
    /// <summary>
    /// Resolves a bundle-root target to its executable, passing files through untouched.
    /// </summary>
    /// <param name="executablePath">The configured launch target.</param>
    /// <param name="logger">The calling runner's logger.</param>
    /// <returns>The executable path, or <c>null</c> when a bundle is unresolvable.</returns>
    internal static string? ResolveBundleTarget(string executablePath, ILogger logger)
    {
        if (!executablePath.EndsWith(ContentFormatConstants.MacAppBundleExtension, StringComparison.OrdinalIgnoreCase) || File.Exists(executablePath))
        {
            return executablePath;
        }

        var resolved = GameClientEntryDetector.ResolveBundleExecutableAbsolute(executablePath);
        if (resolved is null)
        {
            logger.LogWarning("Application bundle has no resolvable executable: {ExecutablePath}", executablePath);
        }

        return resolved;
    }

    /// <summary>
    /// Builds the localized Flatpak guidance message for a bundle that GenHub cannot
    /// launch directly.
    /// </summary>
    /// <param name="bundlePath">The Flatpak bundle path.</param>
    /// <param name="logger">The calling runner's logger.</param>
    /// <param name="localizationService">Optional localization; English fallback when absent.</param>
    /// <returns>The guidance message naming the install-then-run steps.</returns>
    internal static string FlatpakGuidance(string bundlePath, ILogger logger, ILocalizationService? localizationService)
    {
        var appId = FlatpakBundleHelper.TryExtractAppId(bundlePath);
        logger.LogWarning("Cannot launch Flatpak bundle directly: {ExecutablePath} (app {AppId})", bundlePath, appId ?? "unknown");
        return appId is null
            ? Localize(localizationService, LaunchMessageConstants.FlatpakRequiresInstallUnknownIdKey, LaunchMessageConstants.FlatpakRequiresInstallUnknownId, bundlePath)
            : Localize(localizationService, LaunchMessageConstants.FlatpakRequiresInstallKey, LaunchMessageConstants.FlatpakRequiresInstall, bundlePath, appId);
    }

    /// <summary>
    /// Localizes a message key with an English fallback.
    /// </summary>
    /// <param name="localizationService">Optional localization; English fallback when absent.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The English fallback template.</param>
    /// <param name="arguments">Format arguments.</param>
    /// <returns>The localized or fallback message.</returns>
    internal static string Localize(ILocalizationService? localizationService, string key, string fallback, params object?[] arguments)
    {
        return LaunchGuardMessages.Localize(localizationService, key, fallback, arguments);
    }
}
