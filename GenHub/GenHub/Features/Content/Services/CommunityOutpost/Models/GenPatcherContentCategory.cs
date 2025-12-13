using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Features.Content.Services.CommunityOutpost.Models;

/// <summary>
/// Categories for GenPatcher content to enable grouping in UI.
/// </summary>
public enum GenPatcherContentCategory
{
    /// <summary>
    /// Community Patch (TheSuperHackers Build from legi.cc/patch).
    /// </summary>
    CommunityPatch,

    /// <summary>
    /// Official game patches (1.08, 1.04 etc.).
    /// </summary>
    OfficialPatch,

    /// <summary>
    /// Base game files (vanilla game).
    /// </summary>
    BaseGame,

    /// <summary>
    /// Control bar addons.
    /// </summary>
    ControlBar,

    /// <summary>
    /// Hotkey addons.
    /// </summary>
    Hotkeys,

    /// <summary>
    /// Camera modifications.
    /// </summary>
    Camera,

    /// <summary>
    /// Tools and utilities.
    /// </summary>
    Tools,

    /// <summary>
    /// Maps and missions.
    /// </summary>
    Maps,

    /// <summary>
    /// Visual enhancements (textures, icons).
    /// </summary>
    Visuals,

    /// <summary>
    /// System prerequisites (VC++ redistributables).
    /// </summary>
    Prerequisites,

    /// <summary>
    /// Other uncategorized content.
    /// </summary>
    Other,
}
