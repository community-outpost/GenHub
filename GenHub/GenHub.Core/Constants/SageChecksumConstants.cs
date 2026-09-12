namespace GenHub.Core.Constants;

/// <summary>
/// Constants used in SAGE engine checksum and CRC calculation.
/// </summary>
public static class SageChecksumConstants
{
    /// <summary>
    /// Search pattern for SAGE big archive files.
    /// </summary>
    public const string BigFileSearchPattern = "*.big";

    /// <summary>
    /// Relative path substring for Zero Hour INIZH big archive.
    /// </summary>
    public const string IniZhBigRelativePath = @"data\ini\inizh.big";

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
        (@"Data\INI\Default\Roads", @"Data\INI\Roads"),
        (string.Empty, @"Data\INI\Rank"),
        (@"Data\INI\Default\PlayerTemplate", @"Data\INI\PlayerTemplate"),
        (@"Data\INI\Default\FXList", @"Data\INI\FXList"),
        (string.Empty, @"Data\INI\Weapon"),
        (@"Data\INI\Default\ObjectCreationList", @"Data\INI\ObjectCreationList"),
        (string.Empty, @"Data\INI\Locomotor"),
        (@"Data\INI\Default\SpecialPower", @"Data\INI\SpecialPower"),
        (string.Empty, @"Data\INI\DamageFX"),
        (string.Empty, @"Data\INI\Armor"),
        (@"Data\INI\Default\Object", @"Data\INI\Object"),
        (@"Data\INI\Default\Upgrade", @"Data\INI\Upgrade"),
        (@"Data\INI\Default\AIData", @"Data\INI\AIData"),
        (@"Data\INI\Default\Crate", @"Data\INI\Crate"),
    ];
}
