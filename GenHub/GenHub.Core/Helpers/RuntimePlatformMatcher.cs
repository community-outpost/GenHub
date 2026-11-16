using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;

namespace GenHub.Core.Helpers;

/// <summary>
/// Matches a declared platform against a host runtime identifier. A platform is either an
/// OS family (<c>windows</c>, <c>macos</c>, <c>linux</c>), which matches every architecture,
/// or a runtime identifier (<c>win-x64</c>, <c>osx-arm64</c>, <c>linux-x64</c>), which also
/// requires the architecture to match.
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

    private static bool TryParse(string? value, out ExecutablePlatform family, out string? architecture)
    {
        family = ExecutablePlatform.Unknown;
        architecture = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var separator = trimmed.LastIndexOf(CatalogConstants.AssetPlatforms.ArchitectureSeparator);
        var osPart = separator < 0 ? trimmed : trimmed[..separator];
        if (separator >= 0)
        {
            architecture = trimmed[(separator + 1)..];
            if (architecture.Length == 0)
            {
                return false;
            }
        }

        family = ResolveFamily(osPart);
        return family != ExecutablePlatform.Unknown;
    }

    private static ExecutablePlatform ResolveFamily(string osPart)
    {
        if (osPart.StartsWith(CatalogConstants.AssetPlatforms.WindowsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ExecutablePlatform.Windows;
        }

        if (osPart.StartsWith(CatalogConstants.AssetPlatforms.MacOSRuntimePrefix, StringComparison.OrdinalIgnoreCase)
            || osPart.StartsWith(CatalogConstants.AssetPlatforms.MacOSFamilyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ExecutablePlatform.MacOS;
        }

        return osPart.StartsWith(CatalogConstants.AssetPlatforms.LinuxPrefix, StringComparison.OrdinalIgnoreCase)
            ? ExecutablePlatform.Linux
            : ExecutablePlatform.Unknown;
    }
}
