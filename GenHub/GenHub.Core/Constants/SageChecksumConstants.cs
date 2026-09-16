using System.IO;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants and path definitions for SAGE engine game file checksum calculation.
/// </summary>
public static class SageChecksumConstants
{
    /// <summary>
    /// Search pattern for SAGE BIG archive files.
    /// </summary>
    public const string BigFileSearchPattern = "*.big";

    /// <summary>
    /// File extension for SAGE BIG archive files including the leading period.
    /// </summary>
    public const string BigFileExtension = ".big";

    /// <summary>
    /// Search pattern for SAGE INI files.
    /// </summary>
    public const string IniFileSearchPattern = "*.ini";

    /// <summary>
    /// File extension for SAGE INI files including the leading period.
    /// </summary>
    public const string IniFileExtension = ".ini";

    /// <summary>
    /// Relative path substring for Zero Hour INIZH big archive.
    /// </summary>
    public const string IniZhBigRelativePath = @"data\ini\inizh.big";

    /// <summary>
    /// Relative path to the Data/INI directory.
    /// </summary>
    public static readonly string DataIniRelativePath = Path.Combine("Data", "INI");

    /// <summary>
    /// Relative path to skirmish scripts file.
    /// </summary>
    public static readonly string SkirmishScriptsRelativePath = Path.Combine("Data", "Scripts", "SkirmishScripts.scb");

    /// <summary>
    /// Relative path to multiplayer scripts file.
    /// </summary>
    public static readonly string MultiplayerScriptsRelativePath = Path.Combine("Data", "Scripts", "MultiplayerScripts.scb");

    /// <summary>
    /// SAGE Zero Hour (GeneralsMD) INI hierarchy load order (DefaultPath, OverridePath) matching GenCRC / GeneralsGameCode.
    /// </summary>
    public static readonly (string DefaultPath, string OverridePath)[] GeneralsMdOrder =
    [
        (@"Data\INI\Default\GameData", @"Data\INI\GameData"),
        (@"Data\INI\Default\Water", string.Empty),
        (@"Data\INI\Water", string.Empty),
        (@"Data\INI\Default\Weather", string.Empty),
        (@"Data\INI\Weather", string.Empty),
        (@"Data\INI\Default\Science", @"Data\INI\Science"),
        (@"Data\INI\Default\Multiplayer", @"Data\INI\Multiplayer"),
        (@"Data\INI\Default\Terrain", @"Data\INI\Terrain"),
        (@"Data\INI\Default\Voice", string.Empty),
        (@"Data\INI\Voice", string.Empty),
        (@"Data\INI\Default\CommandSet", @"Data\INI\CommandSet"),
        (@"Data\INI\Default\CommandButton", @"Data\INI\CommandButton"),
        (@"Data\INI\Default\Object", @"Data\INI\Object"),
        (@"Data\INI\Default\Armor", @"Data\INI\Armor"),
        (@"Data\INI\Default\Locomotor", @"Data\INI\Locomotor"),
        (@"Data\INI\Default\DamageFX", @"Data\INI\DamageFX"),
        (@"Data\INI\Default\SpecialPower", @"Data\INI\SpecialPower"),
        (@"Data\INI\Default\Weapon", @"Data\INI\Weapon"),
        (@"Data\INI\Default\Upgrade", @"Data\INI\Upgrade"),
        (@"Data\INI\Default\ControlBarResizer", @"Data\INI\ControlBarResizer"),
        (@"Data\INI\Default\ControlBarScheme", @"Data\INI\ControlBarScheme"),
        (@"Data\INI\Default\Video", @"Data\INI\Video"),
        (@"Data\INI\Default\InGameUI", @"Data\INI\InGameUI"),
        (@"Data\INI\Default\DrawGroupInfo", @"Data\INI\DrawGroupInfo"),
        (@"Data\INI\Default\Credits", @"Data\INI\Credits"),
        (@"Data\INI\Default\Roads", @"Data\INI\Roads"),
        (@"Data\INI\Default\Crate", @"Data\INI\Crate"),
        (@"Data\INI\Default\Animation", @"Data\INI\Animation"),
        (@"Data\INI\Default\Audio", @"Data\INI\Audio"),
    ];

    /// <summary>
    /// SAGE Generals INI hierarchy load order (DefaultPath, OverridePath) matching GenCRC / GeneralsGameCode.
    /// </summary>
    public static readonly (string DefaultPath, string OverridePath)[] GeneralsOrder =
    [
        (@"Data\INI\Default\GameData", @"Data\INI\GameData"),
        (@"Data\INI\Default\Water", string.Empty),
        (@"Data\INI\Water", string.Empty),
        (@"Data\INI\Default\Weather", string.Empty),
        (@"Data\INI\Weather", string.Empty),
        (@"Data\INI\Default\Science", @"Data\INI\Science"),
        (@"Data\INI\Default\Multiplayer", @"Data\INI\Multiplayer"),
        (@"Data\INI\Default\Terrain", @"Data\INI\Terrain"),
        (@"Data\INI\Default\Voice", string.Empty),
        (@"Data\INI\Voice", string.Empty),
        (@"Data\INI\Default\CommandSet", @"Data\INI\CommandSet"),
        (@"Data\INI\Default\CommandButton", @"Data\INI\CommandButton"),
        (@"Data\INI\Default\Object", @"Data\INI\Object"),
        (@"Data\INI\Default\Armor", @"Data\INI\Armor"),
        (@"Data\INI\Default\Locomotor", @"Data\INI\Locomotor"),
        (@"Data\INI\Default\DamageFX", @"Data\INI\DamageFX"),
        (@"Data\INI\Default\SpecialPower", @"Data\INI\SpecialPower"),
        (@"Data\INI\Default\Weapon", @"Data\INI\Weapon"),
        (@"Data\INI\Default\Upgrade", @"Data\INI\Upgrade"),
        (@"Data\INI\Default\ControlBarResizer", @"Data\INI\ControlBarResizer"),
        (@"Data\INI\Default\ControlBarScheme", @"Data\INI\ControlBarScheme"),
        (@"Data\INI\Default\Video", @"Data\INI\Video"),
        (@"Data\INI\Default\InGameUI", @"Data\INI\InGameUI"),
        (@"Data\INI\Default\DrawGroupInfo", @"Data\INI\DrawGroupInfo"),
        (@"Data\INI\Default\Credits", @"Data\INI\Credits"),
        (@"Data\INI\Default\Roads", @"Data\INI\Roads"),
        (@"Data\INI\Default\Crate", @"Data\INI\Crate"),
        (@"Data\INI\Default\Animation", @"Data\INI\Animation"),
        (@"Data\INI\Default\Audio", @"Data\INI\Audio"),
    ];
}
