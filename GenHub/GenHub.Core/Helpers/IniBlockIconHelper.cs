using System.Collections.Frozen;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper mapping INI block types and module keywords to Material icon kinds.
/// </summary>
public static class IniBlockIconHelper
{
    private const string LayersIconKind = "LayersOutline";

    private const string VolumeIconKind = "VolumeHigh";

    private static readonly FrozenDictionary<string, string> IconMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["OBJECT"] = "CubeOutline",
        ["OBJECTRESKIN"] = "CubeOutline",
        ["WEAPON"] = "Crosshairs",
        ["ARMOR"] = "ShieldOutline",
        ["ARMORSET"] = "ShieldOutline",
        ["WEAPONSET"] = "CrosshairsGps",
        ["COMMANDBUTTON"] = "GestureTapButton",
        ["COMMANDSET"] = "FormatListBulleted",
        ["UPGRADE"] = "ArrowUpBoldHexagonOutline",
        ["SCIENCE"] = "FlaskOutline",
        ["SPECIALPOWER"] = "LightningBoltOutline",
        ["LOCOMOTOR"] = "CompassOutline",
        ["OBJECTCREATIONLIST"] = "Creation",
        ["DAMAGEFX"] = "Fire",
        ["PLAYERTEMPLATE"] = "AccountGroupOutline",
        ["EXPERIENCELEVELS"] = "StarOutline",
        ["EXPERIENCELEVEL"] = "StarOutline",
        ["VETERANCY"] = "ChevronTripleUp",
        ["MAPPEDIMAGE"] = "ImageOutline",
        ["DRAW"] = "PaletteOutline",
        ["BEHAVIOR"] = "Cogs",
        ["BODY"] = "HeartPulse",
        ["CLIENTUPDATE"] = "Update",
        ["CONDITIONSTATE"] = LayersIconKind,
        ["MODELCONDITIONSTATE"] = LayersIconKind,
        ["DEFAULTCONDITIONSTATE"] = LayersIconKind,
        ["TRANSITIONSTATE"] = LayersIconKind,
        ["ANIMATIONSTATE"] = LayersIconKind,
        ["IDLEANIMATIONSTATE"] = LayersIconKind,
        ["TURRET"] = "ShieldSword",
        ["ALTTURRET"] = "ShieldSword",
        ["REPLACEMODULE"] = "PuzzleOutline",
        ["ADDMODULE"] = "PuzzleOutline",
        ["REMOVEMODULE"] = "PuzzleOutline",
        ["PREREQUISITES"] = "LockCheckOutline",
        ["CREATEOBJECT"] = "CubeSend",
        ["CREATEDEBRIS"] = "CubeSend",
        ["FIREWEAPON"] = "Pistol",
        ["WEATHER"] = "WeatherPartlyCloudy",
        ["WATERTRANSPARENCY"] = "WaterOutline",
        ["AIDATA"] = "RobotOutline",
        ["AUDIOEVENT"] = VolumeIconKind,
        ["DIALOGEVENT"] = VolumeIconKind,
        ["MUSICTRACK"] = VolumeIconKind,
        ["UNITSPECIFICSOUNDS"] = VolumeIconKind,
        ["UNITSPECIFICSOUND"] = VolumeIconKind,
        ["FXLIST"] = "Flare",
        ["FXLISTATBONEPOS"] = "Flare",
        ["PARTICLESYSTEM"] = "Flare",
        ["TERRAIN"] = "Terrain",
        ["ROAD"] = "RoadVariant",
        ["BRIDGE"] = "Bridge",
        ["EVAEVENT"] = "BullhornOutline",
        ["EVA"] = "BullhornOutline",
        ["CAMPAIGN"] = "MapLegend",
        ["MOUSECURSOR"] = "CursorDefault",
        ["MOUSE"] = "CursorDefault",
        ["CRATEDATA"] = "PackageVariant",
        ["CRATE"] = "PackageVariant",
        ["RANK"] = "MedalOutline",
        ["VIDEO"] = "VideoOutline",
        ["ANIMATION"] = "AnimationPlayOutline",
        ["ANIMATION2D"] = "AnimationPlayOutline",
        ["WATERSET"] = "Waves",
        ["MULTIPLAYERCOLOR"] = "PaletteSwatchOutline",
        ["MULTIPLAYERSETTINGS"] = "CogOutline",
        ["CHALLENGEMODE"] = "TrophyOutline",
        ["CHALLENGEGENERALS"] = "TrophyOutline",
        ["GAMEDATA"] = "DatabaseOutline",
        ["GAMELOD"] = "Speedometer",
        ["STATICGAMELOD"] = "Speedometer",
        ["DYNAMICGAMELOD"] = "Speedometer",
        ["CREDITS"] = "TextBoxOutline",
        ["WEBPAGEURL"] = "Web",
        ["WEBPAGES"] = "Web",
        ["WINDOWTRANSITION"] = "Transition",
        ["CONTROLBARSCHEME"] = "DockLeft",
        ["CONTROLS"] = "DockLeft",
        ["COMMANDMAP"] = "KeyboardOutline",
        ["STINGER"] = "MusicNoteOutline",
        ["SOUND"] = "MusicNoteOutline",
        ["SOUNDEFFECTS"] = "MusicNoteOutline",
        ["SPEECH"] = "AccountVoice",
        ["MISCAUDIO"] = "Tune",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the Material icon kind string for the specified block type or module key.
    /// </summary>
    /// <param name="blockType">The INI block type or module name.</param>
    /// <returns>A valid Material icon kind name.</returns>
    public static string GetIconKind(string? blockType)
    {
        if (string.IsNullOrEmpty(blockType))
        {
            return "FileDocumentOutline";
        }

        return IconMap.TryGetValue(blockType, out var icon) ? icon : "CodeTags";
    }
}
