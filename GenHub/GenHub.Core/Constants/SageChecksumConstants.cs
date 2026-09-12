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
    /// SAGE Zero Hour (GeneralsMD) INI hierarchy load order (DefaultPath, OverridePath).
    /// </summary>
    public static readonly (string DefaultPath, string OverridePath)[] GeneralsMdOrder =
    [
        (@"Data\INI\Default\GameData", @"Data\INI\GameData"),
        (string.Empty, @"Data\INI\GameData.ini"),
        (string.Empty, @"Data\INI\Default\GameData.ini"),
        (string.Empty, @"Data\INI\INIZH.ini"),
        (string.Empty, @"Data\INI\Default\INIZH.ini"),
        (@"Data\INI\Default\Water", @"Data\INI\Water"),
        (string.Empty, @"Data\INI\Default\Weather.ini"),
        (string.Empty, @"Data\INI\Weather.ini"),
        (string.Empty, @"Data\INI\Default\Terrain.ini"),
        (string.Empty, @"Data\INI\Terrain.ini"),
        (string.Empty, @"Data\INI\Default\Road.ini"),
        (string.Empty, @"Data\INI\Road.ini"),
        (string.Empty, @"Data\INI\Default\Handicap.ini"),
        (string.Empty, @"Data\INI\Handicap.ini"),
        (string.Empty, @"Data\INI\Default\CommandSet.ini"),
        (string.Empty, @"Data\INI\CommandSet.ini"),
        (string.Empty, @"Data\INI\Default\CommandButton.ini"),
        (string.Empty, @"Data\INI\CommandButton.ini"),
        (string.Empty, @"Data\INI\Default\Science.ini"),
        (string.Empty, @"Data\INI\Science.ini"),
        (string.Empty, @"Data\INI\Default\ModifierList.ini"),
        (string.Empty, @"Data\INI\ModifierList.ini"),
        (string.Empty, @"Data\INI\Default\ControlBarScheme.ini"),
        (string.Empty, @"Data\INI\ControlBarScheme.ini"),
        (string.Empty, @"Data\INI\Default\Video.ini"),
        (string.Empty, @"Data\INI\Video.ini"),
        (string.Empty, @"Data\INI\Default\AudioFX.ini"),
        (string.Empty, @"Data\INI\AudioFX.ini"),
        (string.Empty, @"Data\INI\Default\Animation.ini"),
        (string.Empty, @"Data\INI\Animation.ini"),
        (string.Empty, @"Data\INI\Default\Rank.ini"),
        (string.Empty, @"Data\INI\Rank.ini"),
        (string.Empty, @"Data\INI\Default\WebBanners.ini"),
        (string.Empty, @"Data\INI\WebBanners.ini"),
        (string.Empty, @"Data\INI\Default\MiscFX.ini"),
        (string.Empty, @"Data\INI\MiscFX.ini"),
        (string.Empty, @"Data\INI\Default\ParticleSystem.ini"),
        (string.Empty, @"Data\INI\ParticleSystem.ini"),
        (string.Empty, @"Data\INI\Default\FXList.ini"),
        (string.Empty, @"Data\INI\FXList.ini"),
        (string.Empty, @"Data\INI\Default\DamageFX.ini"),
        (string.Empty, @"Data\INI\DamageFX.ini"),
        (string.Empty, @"Data\INI\Default\Armor.ini"),
        (string.Empty, @"Data\INI\Armor.ini"),
        (string.Empty, @"Data\INI\Default\Locomotor.ini"),
        (string.Empty, @"Data\INI\Locomotor.ini"),
        (string.Empty, @"Data\INI\Default\SpecialPower.ini"),
        (string.Empty, @"Data\INI\SpecialPower.ini"),
        (string.Empty, @"Data\INI\Default\Weapon.ini"),
        (string.Empty, @"Data\INI\Weapon.ini"),
        (string.Empty, @"Data\INI\DamageFX"),
        (string.Empty, @"Data\INI\Armor"),
        (@"Data\INI\Default\Object", @"Data\INI\Object"),
        (@"Data\INI\Default\Upgrade", @"Data\INI\Upgrade"),
        (@"Data\INI\Default\AIData", @"Data\INI\AIData"),
        (@"Data\INI\Default\Crate", @"Data\INI\Crate"),
    ];
}
