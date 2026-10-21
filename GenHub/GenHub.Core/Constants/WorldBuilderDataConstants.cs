// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Constants;

/// <summary>
/// Data-layer paths for the WorldBuilder game asset file system: SAGE INI subsystem
/// directories (boot order in WorldBuilder.cpp InitInstance), art roots, string tables,
/// map sidecars, and .BIG load-precedence tokens.
/// All virtual paths use the engine backslash separator, matching .BIG entry names.
/// </summary>
public static class WorldBuilderDataConstants
{
    /// <summary>
    /// Virtual path separators used by the SAGE engine and .BIG archives.
    /// </summary>
    public static class Separators
    {
        /// <summary>Canonical engine separator used inside .BIG entries and INI paths.</summary>
        public const char Virtual = '\\';

        /// <summary>Alternate separator accepted on input and normalized to <see cref="Virtual"/>.</summary>
        public const char Alternate = '/';
    }

    /// <summary>
    /// SAGE INI subsystem directories under the game root.
    /// </summary>
    public static class Ini
    {
        /// <summary>Root INI directory.</summary>
        public const string Root = @"Data\INI";

        /// <summary>Pristine baseline INI directory; loaded before <see cref="Root"/> overrides.</summary>
        public const string DefaultRoot = @"Data\INI\Default";

        /// <summary>Default directory prefix applied to subsystem names.</summary>
        public const string DefaultPrefix = @"Data\INI\Default\";

        /// <summary>Global game data (MAP_HEIGHT_SCALE, time of day, weather).</summary>
        public const string GameData = @"Data\INI\GameData";

        /// <summary>Debug-only game data (RTS_DEBUG builds).</summary>
        public const string GameDataDebug = @"Data\INI\GameDataDebug";

        /// <summary>Water sets and transparency.</summary>
        public const string Water = @"Data\INI\Water";

        /// <summary>Weather presets.</summary>
        public const string Weather = @"Data\INI\Weather";

        /// <summary>Science prerequisites.</summary>
        public const string Science = @"Data\INI\Science";

        /// <summary>Multiplayer settings, colors, and start spots.</summary>
        public const string Multiplayer = @"Data\INI\Multiplayer";

        /// <summary>Terrain material palette.</summary>
        public const string Terrain = @"Data\INI\Terrain";

        /// <summary>Road and bridge templates.</summary>
        public const string Roads = @"Data\INI\Roads";

        /// <summary>Veterancy ranks.</summary>
        public const string Rank = @"Data\INI\Rank";

        /// <summary>Player template sides.</summary>
        public const string PlayerTemplate = @"Data\INI\PlayerTemplate";

        /// <summary>Special powers.</summary>
        public const string SpecialPower = @"Data\INI\SpecialPower";

        /// <summary>Particle and effect lists.</summary>
        public const string FXList = @"Data\INI\FXList";

        /// <summary>Weapon definitions.</summary>
        public const string Weapon = @"Data\INI\Weapon";

        /// <summary>Object creation lists.</summary>
        public const string ObjectCreationList = @"Data\INI\ObjectCreationList";

        /// <summary>Locomotor definitions.</summary>
        public const string Locomotor = @"Data\INI\Locomotor";

        /// <summary>Damage effects.</summary>
        public const string DamageFX = @"Data\INI\DamageFX";

        /// <summary>Armor sets.</summary>
        public const string Armor = @"Data\INI\Armor";

        /// <summary>Object templates (the object catalog).</summary>
        public const string Object = @"Data\INI\Object";

        /// <summary>Supply crates.</summary>
        public const string Crate = @"Data\INI\Crate";

        /// <summary>Upgrades.</summary>
        public const string Upgrade = @"Data\INI\Upgrade";

        /// <summary>AI data.</summary>
        public const string AIData = @"Data\INI\AIData";

        /// <summary>Script templates loaded explicitly by the WorldBuilder.</summary>
        public const string Scripts = @"Data\Scripts\Scripts";

        /// <summary>Audio events.</summary>
        public const string AudioEvents = @"Data\INI\AudioEvents";
    }

    /// <summary>
    /// Art roots under the game root.
    /// </summary>
    public static class Art
    {
        /// <summary>Compiled W3D models.</summary>
        public const string W3D = @"Art\W3D";

        /// <summary>Terrain and model textures (.tga and .dds).</summary>
        public const string Textures = @"Art\Textures";
    }

    /// <summary>
    /// String table paths backing DisplayName resolution.
    /// </summary>
    public static class StringTables
    {
        /// <summary>Text string source.</summary>
        public const string StrPath = @"Data\Generals.str";

        /// <summary>Compiled string table file name.</summary>
        public const string CsfFileName = "Generals.csf";

        /// <summary>Language directory segment of the CSF path.</summary>
        public const string DataRoot = "Data";

        /// <summary>
        /// Formats the language-specific CSF path (data\%s\Generals.csf in WorldBuilder.cpp).
        /// </summary>
        /// <param name="language">The language directory name (for example, English).</param>
        /// <returns>The virtual CSF path.</returns>
        public static string FormatCsfPath(string language)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(language);
            return string.Concat(DataRoot, @"\", language, @"\", CsfFileName);
        }
    }

    /// <summary>
    /// Map sidecar and cache file names.
    /// </summary>
    public static class Files
    {
        /// <summary>Per-map INI override beside the .map.</summary>
        public const string MapIniFileName = "map.ini";

        /// <summary>Map metadata cache under the user Maps directory.</summary>
        public const string MapCacheFileName = "MapCache.ini";
    }

    /// <summary>
    /// .BIG archive load-precedence tokens. Lower values load first and lose
    /// same-path ties, so Zero Hour archives override base Generals archives.
    /// </summary>
    public static class Big
    {
        /// <summary>Base Generals INI archive.</summary>
        public const string IniBigFileName = "INI.big";

        /// <summary>Zero Hour INI archive; overrides <see cref="IniBigFileName"/>.</summary>
        public const string IniZhBigFileName = "INIZH.big";

        /// <summary>Load priority of the base Generals INI archive.</summary>
        public const int IniBigLoadPriority = 0;

        /// <summary>Load priority of the Zero Hour INI archive.</summary>
        public const int IniZhBigLoadPriority = 1;

        /// <summary>Load priority of every other archive.</summary>
        public const int DefaultLoadPriority = 2;

        /// <summary>Archive file extension including the leading period.</summary>
        public const string Extension = ".big";

        /// <summary>Search pattern for archive discovery.</summary>
        public const string SearchPattern = "*.big";
    }
}
