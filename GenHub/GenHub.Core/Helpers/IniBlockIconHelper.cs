namespace GenHub.Core.Helpers;

/// <summary>
/// Helper mapping INI block types and module keywords to Material icon kinds.
/// </summary>
public static class IniBlockIconHelper
{
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

        return blockType.ToUpperInvariant() switch
        {
            "OBJECT" or "OBJECTRESKIN" => "CubeOutline",
            "WEAPON" => "Crosshairs",
            "ARMOR" or "ARMORSET" => "ShieldOutline",
            "WEAPONSET" => "CrosshairsGps",
            "COMMANDBUTTON" => "GestureTapButton",
            "COMMANDSET" => "FormatListBulleted",
            "UPGRADE" => "ArrowUpBoldHexagonOutline",
            "SCIENCE" => "FlaskOutline",
            "SPECIALPOWER" => "LightningBoltOutline",
            "LOCOMOTOR" => "CompassOutline",
            "OBJECTCREATIONLIST" => "Creation",
            "DAMAGEFX" => "Fire",
            "PLAYERTEMPLATE" => "AccountGroupOutline",
            "EXPERIENCELEVELS" => "StarOutline",
            "VETERANCY" => "ChevronTripleUp",
            "MAPPEDIMAGE" => "ImageOutline",
            "DRAW" => "PaletteOutline",
            "BEHAVIOR" => "Cogs",
            "BODY" => "HeartPulse",
            "CLIENTUPDATE" => "Update",
            "CONDITIONSTATE" or "MODELCONDITIONSTATE" or "DEFAULTCONDITIONSTATE" or "TRANSITIONSTATE" or "ANIMATIONSTATE" or "IDLEANIMATIONSTATE" => "LayersOutline",
            "TURRET" or "ALTTURRET" => "ShieldSword",
            "REPLACEMODULE" or "ADDMODULE" or "REMOVEMODULE" => "PuzzleOutline",
            "PREREQUISITES" => "LockCheckOutline",
            "CREATEOBJECT" or "CREATEDEBRIS" => "CubeSend",
            "FIREWEAPON" => "Pistol",
            "WEATHER" => "WeatherPartlyCloudy",
            "WATERTRANSPARENCY" => "WaterOutline",
            "AIDATA" => "RobotOutline",
            "AUDIOEVENT" or "DIALOGEVENT" or "MUSICTRACK" or "UNITSPECIFICSOUNDS" => "VolumeHigh",
            "FXLIST" or "FXLISTATBONEPOS" or "PARTICLESYSTEM" => "Flare",
            _ => "CodeTags",
        };
    }
}
