using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;
using System.Linq;

namespace GenHub.Core.Helpers;

/// <summary>
/// Matches a declared platform against a host runtime identifier. A platform is either an
/// OS family (<c>windows</c>, <c>macos</c>, <c>linux</c>) or runtime OS name (<c>win</c>, <c>osx</c>),
/// which matches every architecture, or either one with an architecture (<c>win-x64</c>,
/// <c>osx-arm64</c>, <c>macos-arm64</c>), which also requires the architecture to match.
/// A version after the OS name must be numeric (<c>osx.13-arm64</c>).
/// </summary>
public static class RuntimePlatformMatcher
{
    /// <summary>
    /// Determines whether <paramref name="platform"/> applies to the host.
    /// </summary>
    /// <param name="platform">The declared platform, or <c>null</c> for any host.</param>
    /// <param name="hostRuntimeIdentifier">The host runtime identifier, for example <c>osx-arm64</c>.</param>
    /// <returns>
    /// <c>true</c> when no platform is declared, or when the declared OS family and any
    /// declared architecture match the host. An unrecognised platform never matches.
    /// </returns>
    public static bool Matches(string? platform, string? hostRuntimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return true;
        }

        if (!TryParse(platform, out var family, out var architecture)
            || !TryParse(hostRuntimeIdentifier, out var hostFamily, out var hostArchitecture))
        {
            return false;
        }

        return family == hostFamily
            && (architecture is null || string.Equals(architecture, hostArchitecture, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether <paramref name="platform"/> is empty or names a recognised OS family or runtime identifier.
    /// </summary>
    /// <param name="platform">The declared platform.</param>
    /// <returns><c>true</c> when the platform is absent or can be matched against a host.</returns>
    public static bool IsRecognised(string? platform) =>
        string.IsNullOrWhiteSpace(platform) || TryParse(platform, out _, out _);

    private static bool TryParse(string? value, out ExecutablePlatform family, out string? architecture)
    {
        family = ExecutablePlatform.Unknown;
        architecture = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split(CatalogConstants.AssetPlatforms.ArchitectureSeparator);
        if (!TryResolveOs(parts[0], out family))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            return true;
        }

        var isLinux = family == ExecutablePlatform.Linux;
        var qualifiersValid = parts[1..^1].All(qualifier =>
            isLinux && CatalogConstants.AssetPlatforms.LinuxQualifiers.Contains(qualifier, StringComparer.OrdinalIgnoreCase));
        if (!qualifiersValid || !CatalogConstants.AssetPlatforms.Architectures.Contains(parts[^1], StringComparer.OrdinalIgnoreCase))
        {
            family = ExecutablePlatform.Unknown;
            return false;
        }

        architecture = parts[^1];
        return true;
    }

    private static bool TryResolveOs(string osPart, out ExecutablePlatform family)
    {
        var versionSeparator = osPart.IndexOf(CatalogConstants.AssetPlatforms.OsVersionSeparator);
        var osName = versionSeparator < 0 ? osPart : osPart[..versionSeparator];
        var version = versionSeparator < 0 ? null : osPart[(versionSeparator + 1)..];
        var versionValid = version == null
            || version.Split(CatalogConstants.AssetPlatforms.OsVersionSeparator).All(segment => segment.Length > 0 && segment.All(char.IsAsciiDigit));
        family = versionValid ? ResolveFamily(osName) : ExecutablePlatform.Unknown;
        return family != ExecutablePlatform.Unknown;
    }

    private static ExecutablePlatform ResolveFamily(string osName)
    {
        if (IsName(osName, CatalogConstants.AssetPlatforms.WindowsFamily) || IsName(osName, CatalogConstants.AssetPlatforms.WindowsRuntimeOs))
        {
            return ExecutablePlatform.Windows;
        }

        if (IsName(osName, CatalogConstants.AssetPlatforms.MacOSFamily) || IsName(osName, CatalogConstants.AssetPlatforms.MacOSRuntimeOs))
        {
            return ExecutablePlatform.MacOS;
        }

        return IsName(osName, CatalogConstants.AssetPlatforms.Linux) ? ExecutablePlatform.Linux : ExecutablePlatform.Unknown;
    }

    private static bool IsName(string value, string name) => string.Equals(value, name, StringComparison.OrdinalIgnoreCase);
}
