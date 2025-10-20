using System.Collections.Generic;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Provides standard resolution presets for game settings.
/// </summary>
public static class ResolutionPresetsProvider
{
    /// <summary>
    /// Gets the standard resolution presets.
    /// </summary>
    public static IReadOnlyList<string> StandardResolutions { get; } = new List<string>
    {
        "800x600",
        "1024x768",
        "1152x864",
        "1280x720",    // 720p
        "1280x768",
        "1280x800",
        "1280x960",
        "1280x1024",
        "1360x768",
        "1366x768",
        "1400x1050",
        "1440x900",
        "1600x900",
        "1600x1024",
        "1600x1200",
        "1680x1050",
        "1920x1080",   // 1080p
        "1920x1200",
        "2048x1152",
        "2560x1080",   // Ultrawide
        "2560x1440",   // 1440p
        "2560x1600",
        "3440x1440",   // Ultrawide 1440p
        "3840x2160",   // 4K
        "5120x2880",   // 5K
        "7680x4320",   // 8K
    };
}
