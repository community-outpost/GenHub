using System.Collections.Generic;
using GenHub.Core.Constants;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// Provides standard resolution presets for game settings.
/// </summary>
public static class ResolutionPresetsProvider
{
    /// <summary>
    /// Gets the standard resolution presets.
    /// </summary>
    public static IReadOnlyList<string> StandardResolutions { get; } = GameSettingsConstants.ResolutionPresets.StandardResolutions;
}