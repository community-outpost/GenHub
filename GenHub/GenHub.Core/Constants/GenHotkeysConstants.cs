using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the GenHotkeys tool.
/// </summary>
public static class GenHotkeysConstants
{
    /// <summary>Mutual exclusion and special action CSF labels.</summary>
    public static class CsfLabels
    {
        /// <summary>CSF label for Daisy Cutter special power.</summary>
        public const string DaisyCutter = "CONTROLBAR:DaisyCutter";

        /// <summary>CSF label for MOAB special power.</summary>
        public const string Moab = "CONTROLBAR:MOAB";

        /// <summary>CSF label for China Land Mines upgrade.</summary>
        public const string UpgradeChinaMines = "CONTROLBAR:UpgradeChinaMines";

        /// <summary>CSF label for China EMP / Neutron Mines upgrade.</summary>
        public const string UpgradeEmpMines = "CONTROLBAR:UpgradeEMPMines";

        /// <summary>CSF label for China Satellite Hack 1 upgrade.</summary>
        public const string UpgradeChinaSatelliteHackOne = "CONTROLBAR:UpgradeChinaSatelliteHackOne";

        /// <summary>CSF label for China Satellite Hack 2 upgrade.</summary>
        public const string UpgradeChinaSatelliteHackTwo = "CONTROLBAR:UpgradeChinaSatelliteHackTwo";

        /// <summary>CSF label for Structure Sell command.</summary>
        public const string Sell = "CONTROLBAR:Sell";
    }

    /// <summary>Mutual exclusion and special action icon names.</summary>
    public static class IconNames
    {
        /// <summary>Icon identifier for USA Daisy Cutter.</summary>
        public const string UsaDaisyCutter = "USADaisyCutter";

        /// <summary>Icon identifier for USA MOAB.</summary>
        public const string UsaMoab = "USAMOAB";

        /// <summary>Icon identifier for PRC Land Mines.</summary>
        public const string PrcLandMine = "PRCLandMine";

        /// <summary>Icon identifier for PRC Neutron Mines.</summary>
        public const string PrcNeutronMines = "PRCNeutronMines";

        /// <summary>Icon identifier for PRC Satellite Hack 1.</summary>
        public const string PrcSatelliteHack1 = "PRCSatelliteHack1";

        /// <summary>Icon identifier for PRC Satellite Hack 2.</summary>
        public const string PrcSatelliteHack2 = "PRCSatelliteHack2";

        /// <summary>Icon identifier for Sell action.</summary>
        public const string Sell = "Sell";
    }

    /// <summary>Standard faction short-codes and keywords.</summary>
    public static class FactionCodes
    {
        /// <summary>PRC / China faction code.</summary>
        public const string Prc = "PRC";

        /// <summary>Infantry general code.</summary>
        public const string Infantry = "INF";

        /// <summary>Nuke general code.</summary>
        public const string Nuke = "NUK";

        /// <summary>Tank general code.</summary>
        public const string Tank = "TNK";

        /// <summary>GLA faction code.</summary>
        public const string Gla = "GLA";

        /// <summary>Toxin general code.</summary>
        public const string Toxic = "TOX";

        /// <summary>Stealth general code.</summary>
        public const string Stealth = "STL";

        /// <summary>Demolition general code.</summary>
        public const string Demo = "DML";

        /// <summary>USA faction code.</summary>
        public const string Usa = "USA";

        /// <summary>Air Force general code.</summary>
        public const string AirForce = "AIR";

        /// <summary>Laser general code.</summary>
        public const string Laser = "LSR";

        /// <summary>Superweapon general code.</summary>
        public const string SuperWeapon = "SWG";

        /// <summary>Keyword identifying Infantry general.</summary>
        public const string KeywordInfantry = "Infantry";

        /// <summary>Keyword identifying Nuke general.</summary>
        public const string KeywordNuke = "Nuke";

        /// <summary>Keyword identifying Tank general.</summary>
        public const string KeywordTank = "Tank";

        /// <summary>Keyword identifying Toxin general.</summary>
        public const string KeywordTox = "Tox";

        /// <summary>Keyword identifying Stealth general.</summary>
        public const string KeywordStealth = "Stealth";

        /// <summary>Keyword identifying Demolition general.</summary>
        public const string KeywordDemo = "Demo";
    }

    /// <summary>Tool unique identifier.</summary>
    public const string ToolId = "genhotkeys";

    /// <summary>Tool display name.</summary>
    public const string ToolName = "Hotkeys Editor";

    /// <summary>Tool description.</summary>
    public const string ToolDescription = "Visual hotkey editor for C&C Generals & Zero Hour with icon overlays and direct .big addon integration.";

    /// <summary>Tool plugin version.</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>Vanilla preset name.</summary>
    public const string PresetVanilla = "Vanilla";

    /// <summary>Legionnaire preset name.</summary>
    public const string PresetLegionnaire = "Legionnaire";

    /// <summary>Leikeze preset name.</summary>
    public const string PresetLeikeze = "Leikeze";

    /// <summary>Game tag for Generals.</summary>
    public const string GameTagGenerals = "Gen";

    /// <summary>Game tag for Zero Hour.</summary>
    public const string GameTagZeroHour = "ZH";

    /// <summary>Display name for Generals.</summary>
    public const string GameDisplayNameGenerals = "Generals";

    /// <summary>Display name for Zero Hour.</summary>
    public const string GameDisplayNameZeroHour = "Zero Hour";

    /// <summary>Profile directory name for Generals.</summary>
    public const string ProfileDirectoryGenerals = "Generals";

    /// <summary>Profile directory name for Zero Hour.</summary>
    public const string ProfileDirectoryGeneralsZh = "GeneralsZH";

    /// <summary>Candidate icon path template.</summary>
    public const string ProfileIconsPathPattern = "Profiles/{0}/Icons/{1}{2}{3}";

    /// <summary>URI pattern for Avalonia faction icons.</summary>
    [SuppressMessage("Minor Code Smell", "S1075:URIs should not be hardcoded", Justification = "Avalonia resource URI pattern")]
    public const string FactionIconUriPattern = "avares://GenHub/Assets/Icons/Factions/{0}";

    /// <summary>Minimum overlap ratio threshold between layout actions to consider an upgrade variant.</summary>
    public const double UpgradeVariantOverlapRatioThreshold = 0.40;

    /// <summary>Storage directory name for user hotkey profiles.</summary>
    public const string HotkeysStorageDirectory = "Hotkeys";

    /// <summary>Default CommandMap filename.</summary>
    public const string CommandMapFileName = "CommandMap.ini";

    /// <summary>Standard CSF filename.</summary>
    public const string GeneralsCsfFileName = "generals.csf";

    /// <summary>Preset path for English CSF.</summary>
    public const string PresetsLeikezeEn = "Presets/LeikezeEN.csf";

    /// <summary>Preset path for Russian CSF.</summary>
    public const string PresetsLegionnaireRu = "Presets/LegionnaireRU.csf";

    /// <summary>Preset path for CommandMap.ini.</summary>
    public const string PresetsCommandMap = "Presets/CommandMap.ini";

    /// <summary>Tech tree relative path for Generals.</summary>
    public const string TechTreeGenerals = "Profiles/Generals/TechTree.json";

    /// <summary>Tech tree relative path for Zero Hour.</summary>
    public const string TechTreeGeneralsZh = "Profiles/GeneralsZH/TechTree.json";

    /// <summary>
    /// Naming format for generated hotkey .big files: !Hotkeys_{0}_{1}.big.
    /// Prefixed with '!' so SAGE engine loads it alphabetically before retail archives (e.g. EnglishZH.big),
    /// because SAGE's ArchiveFileSystem uses first-loaded wins (overwrite = FALSE).
    /// </summary>
    public const string BigFileNamePattern = "!Hotkeys_{0}_{1}.big";

    /// <summary>Target directory in .big for localized CSF files.</summary>
    public const string DataEnglishDirectory = "Data/English";

    /// <summary>Target directory in .big for overlay TGA textures.</summary>
    public const string ArtTexturesDirectory = "Art/Textures";

    /// <summary>Relative directory in .big for hand-created mapped images INIs.</summary>
    public const string MappedImagesHandCreatedDirectory = "Data/INI/MappedImages/HandCreated";

    /// <summary>Relative directory in .big for 512-texture size mapped images INIs.</summary>
    public const string MappedImagesTextureSize512Directory = "Data/INI/MappedImages/TextureSize_512";

    /// <summary>MappedImages INI filename for hand-created overrides.</summary>
    public const string HandCreatedHotkeysIniFileName = "Hotkeys.ini";

    /// <summary>MappedImages INI filename for TextureSize_512 overrides.</summary>
    public const string TextureSize512HotkeysIniFileName = "zzHotkeys.ini";

    /// <summary>ControlBar CSF key prefix.</summary>
    public const string CsfControlBarPrefix = "CONTROLBAR:";

    /// <summary>Command CSF key prefix.</summary>
    public const string CsfCommandPrefix = "COMMAND:";

    /// <summary>
    /// Mapping of primary hotkey labels (typically CONTROLBAR:...) to their corresponding
    /// sidebar shortcut button labels (OBJECT:..., GUI:Superweapon..., ...Shortcut) in generals.csf.
    /// In the SAGE engine, original Generals uses SPECIAL_POWER_FROM_COMMAND_CENTER and Zero Hour uses
    /// SPECIAL_POWER_FROM_SHORTCUT with identical labels, which reference distinct CSF labels rather than
    /// the primary Command Center button labels.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ShortcutLabelAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // USA Generals Powers & Superweapons
            ["CONTROLBAR:SpyDrone"] = ["OBJECT:SpyDrone"],
            ["CONTROLBAR:A10ThunderboltMissileStrike"] = ["GUI:SuperweaponA10ThunderboltMissileStrike"],
            ["CONTROLBAR:DaisyCutter"] = ["OBJECT:DaisyCutterBomb"],
            ["CONTROLBAR:Paradrop"] = ["GUI:SuperweaponParadropAmerica"],
            ["CONTROLBAR:TankParadrop"] = ["GUI:SuperweaponTankParadrop"],
            ["CONTROLBAR:ClusterMines"] = ["OBJECT:ClusterMinesBomb"],
            ["CONTROLBAR:EMPPulse"] = ["OBJECT:EMPPulseBomb"],
            ["CONTROLBAR:SpectreGunship"] = ["CONTROLBAR:SpectreGunshipFromShortcut"],
            ["CONTROLBAR:LeafletDrop"] = ["CONTROLBAR:LeafletDropShort"],
            ["CONTROLBAR:FireParticleUplinkCannon"] = ["CONTROLBAR:FireParticleUplinkCannonShortcut"],
            ["CONTROLBAR:EmergencyRepair"] = ["GUI:SuperweaponEmergencyRepair"],
            ["CONTROLBAR:SpySatellite"] = ["CONTROLBAR:NoHotKeySpySatellite"],
            ["CONTROLBAR:CIAIntelligence"] = ["CONTROLBAR:CIAIntelligenceShortcut"],

            // China Generals Powers & Superweapons
            ["CONTROLBAR:CarpetBomb"] = ["OBJECT:CarpetBomb"],
            ["CONTROLBAR:Nuke_CarpetBomb"] = ["OBJECT:Nuke_CarpetBomb"],
            ["CONTROLBAR:ArtilleryBarrage"] = ["CONTROLBAR:NoHotKeyArtilleryBarrage"],
            ["CONTROLBAR:Frenzy"] = ["CONTROLBAR:NoHotKeyFrenzy"],
            ["CONTROLBAR:CashHack"] = ["GUI:SuperweaponCashHack"],
            ["CONTROLBAR:NeutronMissile"] = ["CONTROLBAR:NeutronMissileShortcut"],
            ["CONTROLBAR:NukeDrop"] = ["OBJECT:NukeDrop"],
            ["CONTROLBAR:CommunicationsDownload"] = ["CONTROLBAR:CommunicationsDownloadShortcut"],
            ["CONTROLBAR:NapalmStrike"] = ["GUI:SuperweaponNapalmStrike"],
            ["CONTROLBAR:CrateDrop"] = ["GUI:SuperweaponCrateDrop"],

            // GLA Generals Powers & Superweapons
            ["CONTROLBAR:ScudStorm"] = ["CONTROLBAR:ScudStormShortcut"],
            ["CONTROLBAR:AnthraxBomb"] = ["OBJECT:AnthraxBomb"],
            ["CONTROLBAR:Ambush"] = ["GUI:SuperweaponRebelAmbush"],
            ["CONTROLBAR:SneakAttack"] = ["CONTROLBAR:SneakAttackShort"],
            ["CONTROLBAR:GPSScrambler"] = ["GUI:SuperweaponGPSScrambler"],
            ["CONTROLBAR:RadarVanScan"] = ["CONTROLBAR:RadarVanScanShortcut"],
        };

    /// <summary>
    /// Gets the abbreviated game tag ("Gen" or "ZH") for the specified game type.
    /// </summary>
    /// <param name="gameType">Target game type.</param>
    /// <returns>"Gen" for Generals, "ZH" for Zero Hour.</returns>
    public static string GetGameTag(GameType gameType) =>
        gameType == GameType.Generals ? GameTagGenerals : GameTagZeroHour;

    /// <summary>
    /// Gets the friendly display name ("Generals" or "Zero Hour") for the specified game type.
    /// </summary>
    /// <param name="gameType">Target game type.</param>
    /// <returns>"Generals" or "Zero Hour".</returns>
    public static string GetGameDisplayName(GameType gameType) =>
        gameType == GameType.Generals ? GameDisplayNameGenerals : GameDisplayNameZeroHour;

    /// <summary>
    /// Gets the profile asset directory ("Generals" or "GeneralsZH") for the specified game type.
    /// </summary>
    /// <param name="gameType">Target game type.</param>
    /// <returns>Asset directory name.</returns>
    public static string GetProfileDirectory(GameType gameType) =>
        gameType == GameType.Generals ? ProfileDirectoryGenerals : ProfileDirectoryGeneralsZh;

    /// <summary>
    /// Computes the standard .big filename for a hotkey profile.
    /// </summary>
    /// <param name="profileName">Profile name.</param>
    /// <param name="targetGame">Target game.</param>
    /// <returns>The .big filename, e.g. !Hotkeys_MyProfile_ZH.big.</returns>
    public static string GetBigFileName(string? profileName, GameType targetGame)
    {
        var sanitizedName = Regex.Replace(profileName ?? "Hotkeys", @"[^a-zA-Z0-9_\-]", "_", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = "Hotkeys";
        }

        var gameTag = GetGameTag(targetGame);
        return string.Format(CultureInfo.InvariantCulture, BigFileNamePattern, sanitizedName, gameTag);
    }

    /// <summary>
    /// Computes the manifest display name for a hotkey profile addon.
    /// </summary>
    /// <param name="profileName">Profile name.</param>
    /// <param name="targetGame">Target game.</param>
    /// <returns>The manifest name, e.g. Hotkeys - MyProfile (ZH).</returns>
    public static string GetManifestDisplayName(string? profileName, GameType targetGame)
    {
        var gameTag = GetGameTag(targetGame);
        return $"Hotkeys - {profileName ?? "Default"} ({gameTag})";
    }
}
