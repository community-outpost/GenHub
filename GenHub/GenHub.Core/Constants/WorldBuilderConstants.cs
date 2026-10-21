// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the WorldBuilder map format used by Generals and Zero Hour.
/// Ported from the Zero Hour WorldBuilder and engine chunk serializers:
/// DataChunk framing (Common/DataChunk), chunk versions (Common/MapReaderWriterInfo),
/// compression envelope (Libraries/Compression/CompressionManager), RefPack codec
/// (Libraries/Compression/EAC/refdecode), water tracks (.wak), team exchange (.teams),
/// and well-known map dictionary keys (Common/WellKnownKeys).
/// License note: on-disk format constants are functional facts; see docs/tools/worldbuilder.md.
/// </summary>
public static class WorldBuilderConstants
{
    /// <summary>
    /// DataChunk file framing shared by .map, .teams, and script export files.
    /// </summary>
    public static class Framing
    {
        /// <summary>Chunk header size in bytes: u32 id + u16 version + i32 size.</summary>
        public const int ChunkHeaderSize = 10;

        /// <summary>Maximum chunk label length in the table of contents (u8 length prefix).</summary>
        public const int MaxLabelLength = 255;

        /// <summary>Table-of-contents magic at the start of every chunk file: 'C','k','M','p'.</summary>
        public static readonly byte[] TableOfContentsMagic = [0x43, 0x6B, 0x4D, 0x70];
    }

    /// <summary>
    /// Chunk labels written to Zero Hour .map files, in canonical write order.
    /// </summary>
    public static class Chunks
    {
        /// <summary>Terrain heights, borders, and border rectangles.</summary>
        public const string HeightMapData = "HeightMapData";

        /// <summary>Tile indices, blend tiles, texture classes, and cliff info.</summary>
        public const string BlendTileData = "BlendTileData";

        /// <summary>Optional tiling-mode probe chunk (EVAL_TILING_MODES builds only).</summary>
        public const string FunkyTiling = "FUNKY_TILING";

        /// <summary>World dictionary (weather, compression, camera, map name).</summary>
        public const string WorldInfo = "WorldInfo";

        /// <summary>Player sides, build lists, and skirmish teams.</summary>
        public const string SidesList = "SidesList";

        /// <summary>Map object container.</summary>
        public const string ObjectsList = "ObjectsList";

        /// <summary>Single map object entry.</summary>
        public const string Object = "Object";

        /// <summary>Polygon trigger container.</summary>
        public const string PolygonTriggers = "PolygonTriggers";

        /// <summary>Terrain and object lighting for all times of day.</summary>
        public const string GlobalLighting = "GlobalLighting";

        /// <summary>Embedded 128x128 preview pixels (legacy; ZH writes a sidecar .tga instead).</summary>
        public const string MapPreview = "MapPreview";

        /// <summary>Waypoint link pairs.</summary>
        public const string WaypointsList = "WaypointsList";

        /// <summary>Per-player script lists (nested inside SidesList).</summary>
        public const string PlayerScriptsList = "PlayerScriptsList";

        /// <summary>One player's script list.</summary>
        public const string ScriptList = "ScriptList";

        /// <summary>Named script group.</summary>
        public const string ScriptGroup = "ScriptGroup";

        /// <summary>Single script with condition and actions.</summary>
        public const string Script = "Script";

        /// <summary>OR branch of AND conditions.</summary>
        public const string OrCondition = "OrCondition";

        /// <summary>Single AND condition.</summary>
        public const string Condition = "Condition";

        /// <summary>Actions run when the condition is true.</summary>
        public const string ScriptAction = "ScriptAction";

        /// <summary>Actions run when the condition is false.</summary>
        public const string ScriptActionFalse = "ScriptActionFalse";

        /// <summary>Side names plus optional side dictionaries for script export files.</summary>
        public const string ScriptsPlayers = "ScriptsPlayers";

        /// <summary>Team dictionaries (map files and .teams exchange files).</summary>
        public const string ScriptTeams = "ScriptTeams";
    }

    /// <summary>
    /// Chunk versions written by the Zero Hour WorldBuilder.
    /// </summary>
    public static class Versions
    {
        /// <summary>HeightMapData with multiple boundary rectangles.</summary>
        public const ushort HeightMap = 4;

        /// <summary>BlendTileData with cliff state and passable flags (Zero Hour).</summary>
        public const ushort BlendTile = 8;

        /// <summary>World dictionary.</summary>
        public const ushort WorldInfo = 1;

        /// <summary>Sides with build-list scripts, health, and team lists.</summary>
        public const ushort SidesList = 3;

        /// <summary>Objects with property dictionaries.</summary>
        public const ushort ObjectsList = 3;

        /// <summary>Triggers with water, river, and layer names (Zero Hour).</summary>
        public const ushort Triggers = 4;

        /// <summary>Lighting with extra terrain and object lights.</summary>
        public const ushort Lighting = 3;

        /// <summary>Embedded preview pixels.</summary>
        public const ushort MapPreview = 1;

        /// <summary>Waypoint links.</summary>
        public const ushort Waypoints = 1;

        /// <summary>
        /// PlayerScriptsList. The engine enum auto-assigns this the value 5
        /// (it follows K_SCRIPT_CONDITION_VERSION_4 = 4); files on disk carry 5.
        /// </summary>
        public const ushort PlayerScripts = 5;

        /// <summary>Per-player script list.</summary>
        public const ushort ScriptList = 1;

        /// <summary>Script groups with subroutine flag.</summary>
        public const ushort ScriptGroup = 2;

        /// <summary>Scripts with delay-evaluation field.</summary>
        public const ushort Script = 2;

        /// <summary>OR condition branches.</summary>
        public const ushort OrCondition = 1;

        /// <summary>Conditions with internal-name rematching keys.</summary>
        public const ushort Condition = 4;

        /// <summary>Actions with internal-name rematching keys.</summary>
        public const ushort Action = 2;

        /// <summary>Side names for script export files.</summary>
        public const ushort ScriptsPlayers = 2;

        /// <summary>Team dictionaries.</summary>
        public const ushort ScriptTeams = 1;

        /// <summary>Funky tiling probe.</summary>
        public const ushort FunkyTiling = 1;
    }

    /// <summary>
    /// Compression envelope: 4-byte magic, i32 uncompressed length, payload.
    /// </summary>
    public static class Compression
    {
        /// <summary>Envelope header size: magic plus uncompressed length.</summary>
        public const int EnvelopeHeaderSize = 8;

        /// <summary>ZLib envelope magic prefix "ZL" (third byte '1'..'9', fourth nul).</summary>
        public const byte ZLibMagic0 = 0x5A;

        /// <summary>ZLib envelope magic second byte.</summary>
        public const byte ZLibMagic1 = 0x4C;

        /// <summary>ZLib level used when GenHub writes compressed maps.</summary>
        public const int DefaultZLibLevel = 5;

        /// <summary>Minimum ZLib envelope level.</summary>
        public const int MinZLibLevel = 1;

        /// <summary>Maximum ZLib envelope level.</summary>
        public const int MaxZLibLevel = 9;

        /// <summary>ZLib stream header CMF byte (deflate, 32K window).</summary>
        public const byte ZLibStreamCmf = 0x78;

        /// <summary>No-compression intent id (engine CompressionType).</summary>
        public const int IntentNone = 0;

        /// <summary>RefPack intent id (engine CompressionType).</summary>
        public const int IntentRefPack = 1;

        /// <summary>First ZLib intent id (level = id - 2).</summary>
        public const int IntentZLibBase = 3;

        /// <summary>Last ZLib intent id.</summary>
        public const int IntentZLibMax = 11;

        /// <summary>ZLib stream header FLG bytes by envelope level 1..9 (index 0 unused; mirrors zlib level_flags).</summary>
        public static readonly byte[] ZLibStreamFlgByLevel = [0x00, 0x01, 0x5E, 0x5E, 0x5E, 0x5E, 0x9C, 0xDA, 0xDA, 0xDA];

        /// <summary>RefPack envelope magic "EAR\0" (default for maps).</summary>
        public static readonly byte[] RefPackMagic = [0x45, 0x41, 0x52, 0x00];

        /// <summary>Nox LZH envelope magic "NOX\0".</summary>
        public static readonly byte[] NoxMagic = [0x4E, 0x4F, 0x58, 0x00];

        /// <summary>BTree envelope magic "EAB\0".</summary>
        public static readonly byte[] BTreeMagic = [0x45, 0x41, 0x42, 0x00];

        /// <summary>Huffman envelope magic "EAH\0".</summary>
        public static readonly byte[] HuffMagic = [0x45, 0x41, 0x48, 0x00];
    }

    /// <summary>
    /// RefPack (10FB family) stream markers.
    /// </summary>
    public static class RefPack
    {
        /// <summary>Maximum match offset the three copy forms can address.</summary>
        public const int MaxOffsetShort = 1023;

        /// <summary>End-of-stream literal threshold: runs above this end the stream.</summary>
        public const int LiteralRunLimit = 112;
    }

    /// <summary>
    /// Engine limits mirrored from the game sources.
    /// </summary>
    public static class Limits
    {
        /// <summary>Maximum players per map.</summary>
        public const int MaxPlayerCount = 16;

        /// <summary>Global lights per lighting set.</summary>
        public const int MaxGlobalLights = 3;

        /// <summary>Times of day stored in the lighting chunk.</summary>
        public const int TimeOfDayCount = 4;

        /// <summary>Blend-tile sentinel every blend record must carry.</summary>
        public const int BlendSentinel = 0x7ADA0000;

        /// <summary>Blend inverted bit inside the inverted blend byte.</summary>
        public const int InvertedMask = 0x1;

        /// <summary>Blend flipped bit inside the inverted blend byte.</summary>
        public const int FlippedMask = 0x2;

        /// <summary>Maximum texture classes the reader accepts.</summary>
        public const int MaxTextureClasses = 200;

        /// <summary>Maximum blend tiles the reader accepts.</summary>
        public const int MaxBlendTiles = 16192;

        /// <summary>Maximum bitmap tiles the reader accepts.</summary>
        public const int MaxBitmapTiles = 2048;

        /// <summary>Maximum undo depth kept by the editor session.</summary>
        public const int MaxUndoDepth = 64;

        /// <summary>Maximum dimension for map previews.</summary>
        public const int MaxPreviewDimension = 4096;

        /// <summary>Maximum decompressed map payload size in bytes (64 MB).</summary>
        public const int MaxDecompressedSize = 64 * 1024 * 1024;
    }

    /// <summary>
    /// Terrain geometry facts.
    /// </summary>
    public static class Terrain
    {
        /// <summary>World units per heightmap cell.</summary>
        public const float CellSize = 10.0f;

        /// <summary>World Z per height byte.</summary>
        public const float HeightScale = 10.0f / 16.0f;

        /// <summary>Maximum height byte value.</summary>
        public const int MaxHeight = 255;

        /// <summary>Cliff threshold in world Z units (engine PATHFIND_CLIFF_SLOPE_LIMIT_F).</summary>
        public const float CliffSlopeLimitWorldZ = 9.8f;

        /// <summary>Atlas base red: muted ground tone sampled by classes without resolved textures.</summary>
        public const byte FallbackAtlasRed = 107;

        /// <summary>Atlas base green: muted ground tone sampled by classes without resolved textures.</summary>
        public const byte FallbackAtlasGreen = 102;

        /// <summary>Atlas base blue: muted ground tone sampled by classes without resolved textures.</summary>
        public const byte FallbackAtlasBlue = 71;

        /// <summary>Lenient cliff threshold in world Z used when recomputing cliffs after interactive edits.</summary>
        public const float CliffToolSlopeLimitWorldZ = 20f;
    }

    /// <summary>
    /// Map object flag bits (Common/MapObject).
    /// </summary>
    public static class ObjectFlags
    {
        /// <summary>First point of a road segment.</summary>
        public const int RoadPoint1 = 0x00000002;

        /// <summary>Second point of a road segment.</summary>
        public const int RoadPoint2 = 0x00000004;

        /// <summary>Either road-point bit set.</summary>
        public const int RoadFlags = RoadPoint1 | RoadPoint2;

        /// <summary>Angled rather than curved road corner.</summary>
        public const int RoadCornerAngled = 0x00000008;

        /// <summary>First point of a bridge.</summary>
        public const int BridgePoint1 = 0x00000010;

        /// <summary>Second point of a bridge.</summary>
        public const int BridgePoint2 = 0x00000020;

        /// <summary>Either bridge-point bit set.</summary>
        public const int BridgeFlags = BridgePoint1 | BridgePoint2;

        /// <summary>Tight road corner.</summary>
        public const int RoadCornerTight = 0x00000040;

        /// <summary>Generic alpha join at this road end.</summary>
        public const int RoadJoin = 0x00000080;

        /// <summary>WorldBuilder-only hidden object.</summary>
        public const int DontRender = 0x00000100;
    }

    /// <summary>
    /// Well-known object property values (defaults, stance, veterancy).
    /// </summary>
    public static class Objects
    {
        /// <summary>Default template for newly placed objects.</summary>
        public const string DefaultTemplate = "CivilianBuilding01";

        /// <summary>Fallback team for objects without an explicit team.</summary>
        public const string NeutralTeam = "[neutral]";

        /// <summary>Generic engine default token.</summary>
        public const string Normal = "Normal";

        /// <summary>Passive aggressiveness stance.</summary>
        public const string Passive = "Passive";

        /// <summary>Aggressive stance.</summary>
        public const string Aggressive = "Aggressive";

        /// <summary>Regular veterancy.</summary>
        public const string Regular = "Regular";

        /// <summary>Veteran veterancy.</summary>
        public const string Veteran = "Veteran";

        /// <summary>Elite veterancy.</summary>
        public const string Elite = "Elite";

        /// <summary>Heroic veterancy.</summary>
        public const string Heroic = "Heroic";
    }

    /// <summary>
    /// Dictionary value types (Common/Dict).
    /// </summary>
    public enum DictValueType
    {
        /// <summary>Boolean stored as one byte.</summary>
        Bool = 0,

        /// <summary>32-bit integer.</summary>
        Int = 1,

        /// <summary>32-bit float.</summary>
        Real = 2,

        /// <summary>Length-prefixed ASCII string.</summary>
        AsciiString = 3,

        /// <summary>Length-prefixed UTF-16 string.</summary>
        UnicodeString = 4,
    }

    /// <summary>
    /// Well-known dictionary keys used inside map files (Common/WellKnownKeys).
    /// </summary>
    public static class DictKeys
    {
        /// <summary>Team display name.</summary>
        public const string TeamName = "teamName";

        /// <summary>Owning player or team name.</summary>
        public const string TeamOwner = "teamOwner";

        /// <summary>Waypoint object name.</summary>
        public const string WaypointName = "waypointName";

        /// <summary>Waypoint numeric id.</summary>
        public const string WaypointId = "waypointID";

        /// <summary>Object template name.</summary>
        public const string ObjectName = "objectName";

        /// <summary>Include this object in script exports.</summary>
        public const string ExportWithScript = "exportWithScript";

        /// <summary>Player display name.</summary>
        public const string PlayerName = "playerName";

        /// <summary>Whether the player is human.</summary>
        public const string PlayerIsHuman = "playerIsHuman";

        /// <summary>Player display name text.</summary>
        public const string PlayerDisplayName = "playerDisplayName";

        /// <summary>Player enemies list.</summary>
        public const string PlayerEnemies = "playerEnemies";

        /// <summary>Player allies list.</summary>
        public const string PlayerAllies = "playerAllies";

        /// <summary>Original owner team.</summary>
        public const string OriginalOwner = "originalOwner";

        /// <summary>Player faction template.</summary>
        public const string PlayerFaction = "playerFaction";

        /// <summary>Weather key in the world dictionary.</summary>
        public const string Weather = "weather";

        /// <summary>Compression override in the world dictionary.</summary>
        public const string CompressionType = "compression";

        /// <summary>Map display name in the world dictionary.</summary>
        public const string MapName = "mapName";

        /// <summary>Initial camera position in the world dictionary.</summary>
        public const string InitialCameraPosition = "InitialCameraPosition";

        /// <summary>Object script name (MFC-OBJ-03).</summary>
        public const string ObjectScript = "script";

        /// <summary>Object initial health percent (MFC-OBJ-04).</summary>
        public const string ObjectHealth = "health";

        /// <summary>Object maximum hit points (MFC-OBJ-05).</summary>
        public const string ObjectHitPoints = "hitPoints";

        /// <summary>Object aggressiveness preset (MFC-OBJ-06).</summary>
        public const string ObjectAggressiveness = "aggressiveness";

        /// <summary>Object veterancy preset (MFC-OBJ-07).</summary>
        public const string ObjectVeterancy = "veterancy";

        /// <summary>Object enabled flag (MFC-OBJ-08).</summary>
        public const string ObjectEnabled = "enabled";

        /// <summary>Object unsellable flag (MFC-OBJ-09).</summary>
        public const string ObjectUnsellable = "unsellable";

        /// <summary>Object targetable flag (MFC-OBJ-10).</summary>
        public const string ObjectTargetable = "targetable";

        /// <summary>Object indestructible flag (MFC-OBJ-11).</summary>
        public const string ObjectIndestructible = "indestructible";

        /// <summary>Object AI recruitable flag (MFC-OBJ-12).</summary>
        public const string ObjectRecruitableAI = "recruitableAI";

        /// <summary>Object powered flag (MFC-OBJ-13).</summary>
        public const string ObjectPowered = "powered";

        /// <summary>Object selectable flag (MFC-OBJ-14).</summary>
        public const string ObjectSelectable = "selectable";

        /// <summary>Object stopping distance (MFC-OBJ-15).</summary>
        public const string ObjectStoppingDistance = "stoppingDistance";

        /// <summary>Object vision/targeting distance (MFC-OBJ-16).</summary>
        public const string ObjectVisionDistance = "visionDistance";

        /// <summary>Object shroud clearing distance (MFC-OBJ-17).</summary>
        public const string ObjectShroudClearingDistance = "shroudClearingDistance";

        /// <summary>Object time override (MFC-OBJ-22).</summary>
        public const string ObjectTime = "time";

        /// <summary>Object visual scale factor (MFC-OBJ-24).</summary>
        public const string ObjectScale = "scale";

        /// <summary>Object scale override enabled (MFC-OBJ-24).</summary>
        public const string ObjectScaleEnabled = "scaleEnabled";

        /// <summary>Object attached sound (MFC-OBJ-25).</summary>
        public const string ObjectSound = "sound";

        /// <summary>Object sound customization flag (MFC-OBJ-26).</summary>
        public const string ObjectSoundCustomize = "soundCustomize";

        /// <summary>Object sound enabled flag (MFC-OBJ-27).</summary>
        public const string ObjectSoundEnabled = "soundEnabled";

        /// <summary>Object sound looping flag (MFC-OBJ-28).</summary>
        public const string ObjectSoundLooping = "soundLooping";

        /// <summary>Object sound loop count (MFC-OBJ-29).</summary>
        public const string ObjectSoundLoopCount = "soundLoopCount";

        /// <summary>Object sound priority (MFC-OBJ-30).</summary>
        public const string ObjectSoundPriority = "soundPriority";

        /// <summary>Object sound volume (MFC-OBJ-31).</summary>
        public const string ObjectSoundVolume = "soundVolume";

        /// <summary>Object sound minimum volume (MFC-OBJ-32).</summary>
        public const string ObjectSoundMinVolume = "soundMinVolume";

        /// <summary>Object sound minimum range (MFC-OBJ-33).</summary>
        public const string ObjectSoundMinRange = "soundMinRange";

        /// <summary>Object sound maximum range (MFC-OBJ-34).</summary>
        public const string ObjectSoundMaxRange = "soundMaxRange";

        /// <summary>Object pre-built upgrades, semicolon separated (MFC-OBJ-35).</summary>
        public const string ObjectUpgrades = "upgrades";

        /// <summary>Object reflects-in-mirror flag (MFC-OBJ-36).</summary>
        public const string ObjectReflectsInMirror = "reflectsInMirror";
    }

    /// <summary>
    /// Weather preset names used by the WorldBuilder.
    /// </summary>
    public static class WeatherPresets
    {
        /// <summary>Normal weather preset.</summary>
        public const string Normal = "Normal";

        /// <summary>Snow weather preset.</summary>
        public const string Snow = "Snow";
    }

    /// <summary>
    /// Well-known team identifiers used by the game engine.
    /// </summary>
    public static class Teams
    {
        /// <summary>Default root team prefix / name.</summary>
        public const string DefaultRootTeam = "team";

        /// <summary>Default engine team name.</summary>
        public const string DefaultTeam = "DefaultTeam";
    }

    /// <summary>
    /// Well-known player side identifiers used by the game engine.
    /// </summary>
    public static class Sides
    {
        /// <summary>Civilian side player name (neutral, never owns skirmish teams).</summary>
        public const string CivilianPlayerName = "PlyrCivilian";

        /// <summary>Civilian side faction.</summary>
        public const string CivilianFaction = "FactionCivilian";
    }

    /// <summary>
    /// Well-known file names handled by the WorldBuilder tool.
    /// </summary>
    public static class FileNames
    {
        /// <summary>Project manifest file name.</summary>
        public const string Manifest = "worldbuilder.json";

        /// <summary>Native WorldBuilder executable names probed in the game folder.</summary>
        public static readonly string[] SidecarExecutables = ["WorldBuilder.exe", "worldbuilder.exe"];
    }

    /// <summary>
    /// File extensions handled by the WorldBuilder tool.
    /// </summary>
    public static class FileExtensions
    {
        /// <summary>Compiled map file.</summary>
        public const string Map = ".map";

        /// <summary>Map override INI beside the .map.</summary>
        public const string MapIni = ".ini";

        /// <summary>Wave-track companion file.</summary>
        public const string WaveTracks = ".wak";

        /// <summary>Team exchange file.</summary>
        public const string TeamsFile = ".teams";

        /// <summary>Script export file.</summary>
        public const string Scripts = ".scb";

        /// <summary>Preview image beside the .map.</summary>
        public const string PreviewImage = ".tga";
    }

    /// <summary>
    /// Map generation defaults mirrored from the engine map generator dialog.
    /// </summary>
    public static class MapGen
    {
        /// <summary>Default random seed (0 keeps generation deterministic).</summary>
        public const int DefaultSeed = 0;

        /// <summary>Default playable width in cells.</summary>
        public const int DefaultWidth = 120;

        /// <summary>Default playable height in cells.</summary>
        public const int DefaultHeight = 120;

        /// <summary>Default player count.</summary>
        public const int DefaultPlayers = 2;

        /// <summary>Map name written by the procedural generator.</summary>
        public const string GeneratedMapName = "Generated Map";

        /// <summary>Default road template placed by the generator.</summary>
        public const string DefaultRoadTemplate = "DirtRoad";

        /// <summary>Default open-ground texture class painted by the generator.</summary>
        public const string DefaultGroundTexture = "Grass";

        /// <summary>Default cliff texture class painted by the generator.</summary>
        public const string DefaultCliffTexture = "Cliff";

        /// <summary>RNG salt for start placement (SALT_STARTS).</summary>
        public const int SaltStarts = 0;

        /// <summary>RNG salt for the terrain noise passes (SALT_TERRAIN).</summary>
        public const int SaltTerrain = 1013;

        /// <summary>RNG salt for slope limiting (SALT_SLOPES).</summary>
        public const int SaltSlopes = 2027;

        /// <summary>RNG salt for cliff texturing (SALT_CLIFFS).</summary>
        public const int SaltCliffs = 3041;

        /// <summary>RNG salt for ground texturing (SALT_TEXTURES).</summary>
        public const int SaltTextures = 4057;

        /// <summary>RNG salt for tree scatter (SALT_TREES).</summary>
        public const int SaltTrees = 5077;

        /// <summary>RNG salt for rock scatter (SALT_ROCKS).</summary>
        public const int SaltRocks = 6091;

        /// <summary>RNG salt for supply dock placement.</summary>
        public const int SaltSupplies = 7103;

        /// <summary>Neighbor height step that reads as exposed rock when texturing.</summary>
        public const int TexturingSteepStep = 8;

        /// <summary>Chance a steep non-cliff cell paints as cliff rock.</summary>
        public const double TexturingSteepChance = 0.75;

        /// <summary>Chance a ground cell touching a cliff paints as cliff rock.</summary>
        public const double TexturingEdgeChance = 0.35;

        /// <summary>Skirmish faction pool cycled by the map generator.</summary>
        public static readonly string[] SkirmishFactions =
        [
            "FactionAmerica", "FactionChina", "FactionGLA",
            "FactionAmericaAirForceGeneral", "FactionAmericaLaserGeneral", "FactionAmericaSuperWeaponGeneral",
            "FactionChinaTankGeneral", "FactionChinaNukeGeneral", "FactionChinaInfantryGeneral",
            "FactionGLADemolitionGeneral", "FactionGLAToxinGeneral", "FactionGLAStealthGeneral",
        ];

        /// <summary>Skirmish player names paired with the faction pool.</summary>
        public static readonly string[] SkirmishPlayerNames =
        [
            "SkirmishAmerica", "SkirmishChina", "SkirmishGLA",
            "SkirmishAmericaAirForceGeneral", "SkirmishAmericaLaserGeneral", "SkirmishAmericaSuperWeaponGeneral",
            "SkirmishChinaTankGeneral", "SkirmishChinaNukeGeneral", "SkirmishChinaInfantryGeneral",
            "SkirmishGLADemolitionGeneral", "SkirmishGLAToxinGeneral", "SkirmishGLAStealthGeneral",
        ];
    }

    /// <summary>
    /// Map preview dimensions (MapPreview.h).
    /// </summary>
    public static class Preview
    {
        /// <summary>Preview width in pixels.</summary>
        public const int Width = 128;

        /// <summary>Preview height in pixels.</summary>
        public const int Height = 128;
    }

    /// <summary>
    /// Map canvas rendering defaults.
    /// </summary>
    public static class Canvas
    {
        /// <summary>Default water table height (WaterOptions.cpp).</summary>
        public const int DefaultWaterLevel = 7;

        /// <summary>Default grid spacing in cells.</summary>
        public const int DefaultGridStep = 10;

        /// <summary>Default brush radius in cells.</summary>
        public const int DefaultBrushRadius = 3;

        /// <summary>Maximum brush radius in cells.</summary>
        public const int MaxBrushRadius = 12;
    }

    /// <summary>
    /// Script parameter types (GameLogic/Scripts).
    /// </summary>
    public enum ScriptParameterType
    {
        /// <summary>Integer.</summary>
        Int = 0,

        /// <summary>Float.</summary>
        Real = 1,

        /// <summary>Script name.</summary>
        Script = 2,

        /// <summary>Team name.</summary>
        Team = 3,

        /// <summary>Counter name.</summary>
        Counter = 4,

        /// <summary>Flag name.</summary>
        Flag = 5,

        /// <summary>Comparison operator.</summary>
        Comparison = 6,

        /// <summary>Waypoint name.</summary>
        Waypoint = 7,

        /// <summary>Boolean integer.</summary>
        Boolean = 8,

        /// <summary>Trigger area name.</summary>
        TriggerArea = 9,

        /// <summary>Text string.</summary>
        TextString = 10,

        /// <summary>Side name.</summary>
        Side = 11,

        /// <summary>Sound name.</summary>
        Sound = 12,

        /// <summary>Subroutine script name.</summary>
        ScriptSubroutine = 13,

        /// <summary>Unit template name.</summary>
        Unit = 14,

        /// <summary>Object type name.</summary>
        ObjectType = 15,

        /// <summary>3D coordinate.</summary>
        Coord3D = 16,

        /// <summary>Angle in radians.</summary>
        Angle = 17,

        /// <summary>Team state name.</summary>
        TeamState = 18,

        /// <summary>Relation integer.</summary>
        Relation = 19,

        /// <summary>AI mood integer.</summary>
        AiMood = 20,

        /// <summary>Dialog string.</summary>
        Dialog = 21,

        /// <summary>Music track.</summary>
        Music = 22,

        /// <summary>Movie name.</summary>
        Movie = 23,

        /// <summary>Waypoint path label.</summary>
        WaypointPath = 24,

        /// <summary>Localized text key.</summary>
        LocalizedText = 25,

        /// <summary>Bridge name.</summary>
        Bridge = 26,

        /// <summary>Kind-of integer.</summary>
        KindOf = 27,

        /// <summary>Attack priority set name.</summary>
        AttackPrioritySet = 28,

        /// <summary>Radar event integer.</summary>
        RadarEventType = 29,

        /// <summary>Special power name.</summary>
        SpecialPower = 30,

        /// <summary>Science name.</summary>
        Science = 31,

        /// <summary>Upgrade name.</summary>
        Upgrade = 32,

        /// <summary>Command-button ability index.</summary>
        CommandButtonAbility = 33,

        /// <summary>Boundary index.</summary>
        Boundary = 34,

        /// <summary>Buildability integer.</summary>
        Buildable = 35,

        /// <summary>Surfaces integer.</summary>
        SurfacesAllowed = 36,

        /// <summary>Shake intensity integer.</summary>
        ShakeIntensity = 37,

        /// <summary>Command button name.</summary>
        CommandButton = 38,

        /// <summary>Font name.</summary>
        FontName = 39,

        /// <summary>Object status name.</summary>
        ObjectStatus = 40,

        /// <summary>All command-button abilities.</summary>
        CommandButtonAllAbilities = 41,

        /// <summary>Skirmish waypoint path.</summary>
        SkirmishWaypointPath = 42,

        /// <summary>Color as ARGB integer.</summary>
        Color = 43,

        /// <summary>Emoticon.</summary>
        Emoticon = 44,

        /// <summary>Object panel flag.</summary>
        ObjectPanelFlag = 45,

        /// <summary>Faction name.</summary>
        FactionName = 46,

        /// <summary>Object type list.</summary>
        ObjectTypeList = 47,

        /// <summary>Reveal name.</summary>
        RevealName = 48,

        /// <summary>Science availability.</summary>
        ScienceAvailability = 49,

        /// <summary>Left or right.</summary>
        LeftOrRight = 50,

        /// <summary>Percentage float.</summary>
        Percent = 51,
    }

    /// <summary>
    /// WorldBuilder cross-tool seams.
    /// </summary>
    public static class Tool
    {
        /// <summary>
        /// Target tool id for opening map.ini files. Provided by the INI editor
        /// once it is merged; requests no-op gracefully while it is absent.
        /// </summary>
        public const string IniEditorToolId = "genhub.tools.inieditor";
    }

    /// <summary>
    /// W3D model chunk IDs and shader values from WW3D2/w3d_file.h.
    /// </summary>
    public static class W3D
    {
        /// <summary>Chunk header size in bytes (type plus size).</summary>
        public const int ChunkHeaderSize = 8;

        /// <summary>Mask removing the historical has-sub-chunks flag from chunk sizes.</summary>
        public const uint ChunkSizeMask = 0x7FFFFFFF;

        /// <summary>Mesh container chunk.</summary>
        public const uint ChunkMesh = 0x0000;

        /// <summary>Vertex positions (W3dVectorStruct array).</summary>
        public const uint MeshVertices = 0x0002;

        /// <summary>Vertex normals (W3dVectorStruct array).</summary>
        public const uint MeshVertexNormals = 0x0003;

        /// <summary>Bone influence per vertex (skinning, ignored by the viewer).</summary>
        public const uint MeshVertexInfluences = 0x000E;

        /// <summary>Mesh header with counts, bounds, and name.</summary>
        public const uint MeshHeader3 = 0x001F;

        /// <summary>Triangle indices (W3dTriangleStruct array).</summary>
        public const uint MeshTriangles = 0x0020;

        /// <summary>Prelit shade index per vertex.</summary>
        public const uint MeshVertexShadeIndices = 0x0022;

        /// <summary>Pass, vertex-material, shader, and texture counts.</summary>
        public const uint MeshMaterialInfo = 0x0028;

        /// <summary>GPU state per shader (W3dShaderStruct array).</summary>
        public const uint MeshShaders = 0x0029;

        /// <summary>Vertex material container.</summary>
        public const uint MeshVertexMaterials = 0x002A;

        /// <summary>Single vertex material.</summary>
        public const uint MeshVertexMaterial = 0x002B;

        /// <summary>Vertex material name.</summary>
        public const uint MeshVertexMaterialName = 0x002C;

        /// <summary>Vertex material colors and coefficients.</summary>
        public const uint MeshVertexMaterialInfo = 0x002D;

        /// <summary>Texture list container.</summary>
        public const uint MeshTextures = 0x0030;

        /// <summary>Single texture reference.</summary>
        public const uint MeshTexture = 0x0031;

        /// <summary>Texture file name.</summary>
        public const uint MeshTextureName = 0x0032;

        /// <summary>Optional texture animation info.</summary>
        public const uint MeshTextureInfo = 0x0033;

        /// <summary>Single render pass.</summary>
        public const uint MeshMaterialPass = 0x0038;

        /// <summary>Vertex material ids (single or per-vertex).</summary>
        public const uint PassVertexMaterialIds = 0x0039;

        /// <summary>Shader ids (single or per-triangle).</summary>
        public const uint PassShaderIds = 0x003A;

        /// <summary>Per-vertex diffuse colors.</summary>
        public const uint PassDiffuseColor = 0x003B;

        /// <summary>Per-vertex diffuse illumination.</summary>
        public const uint PassDiffuseIllumination = 0x003C;

        /// <summary>Per-vertex specular colors.</summary>
        public const uint PassSpecularColor = 0x003E;

        /// <summary>Texture stage container.</summary>
        public const uint PassTextureStage = 0x0048;

        /// <summary>Texture ids (single or per-triangle).</summary>
        public const uint StageTextureIds = 0x0049;

        /// <summary>Per-vertex UVs for a stage.</summary>
        public const uint StageTexCoords = 0x004A;

        /// <summary>Bone hierarchy container.</summary>
        public const uint ChunkHierarchy = 0x0100;

        /// <summary>Hierarchy header.</summary>
        public const uint HierarchyHeader = 0x0101;

        /// <summary>Pivot definitions (W3dPivotStruct array).</summary>
        public const uint HierarchyPivots = 0x0102;

        /// <summary>Pivot fixup table.</summary>
        public const uint HierarchyPivotFixups = 0x0103;

        /// <summary>Hierarchical LOD container.</summary>
        public const uint ChunkHlod = 0x0700;

        /// <summary>HLOD header.</summary>
        public const uint HlodHeader = 0x0701;

        /// <summary>LOD level array.</summary>
        public const uint HlodLodArray = 0x0702;

        /// <summary>Lod sub-object array header.</summary>
        public const uint HlodSubObjectArrayHeader = 0x0703;

        /// <summary>Single sub-object reference.</summary>
        public const uint HlodSubObject = 0x0704;

        /// <summary>Aggregate model array.</summary>
        public const uint HlodAggregateArray = 0x0705;

        /// <summary>Proxy model array.</summary>
        public const uint HlodProxyArray = 0x0706;

        /// <summary>Collision box render object.</summary>
        public const uint ChunkBox = 0x0740;

        /// <summary>Contained-model references.</summary>
        public const uint ChunkAggregate = 0x0600;

        /// <summary>W3D file extension.</summary>
        public const string FileExtension = ".w3d";

        /// <summary>Prefix marking a scaled cached object name (no missing-file warning).</summary>
        public const char CachedObjectPrefix = '#';

        /// <summary>Depth comparison: less or equal (engine default).</summary>
        public const byte DepthCompareLequal = 3;

        /// <summary>Depth write enabled (engine default).</summary>
        public const byte DepthMaskWriteEnable = 1;

        /// <summary>Alpha test enabled.</summary>
        public const byte AlphaTestEnable = 1;

        /// <summary>Blend factor zero.</summary>
        public const byte BlendZero = 0;

        /// <summary>Blend factor one (source default).</summary>
        public const byte BlendOne = 1;

        /// <summary>Blend factor source alpha.</summary>
        public const byte BlendSrcAlpha = 4;

        /// <summary>Blend factor one minus source alpha.</summary>
        public const byte BlendOneMinusSrcAlpha = 5;

        /// <summary>Blend factor source color.</summary>
        public const byte BlendSrcColor = 2;

        /// <summary>Blend factor one minus source color.</summary>
        public const byte BlendOneMinusSrcColor = 3;

        /// <summary>Primary gradient: modulate (engine default).</summary>
        public const byte PriGradientModulate = 1;

        /// <summary>Primary gradient: add.</summary>
        public const byte PriGradientAdd = 2;

        /// <summary>Primary gradient: modulate double.</summary>
        public const byte PriGradientModulate2X = 5;

        /// <summary>Texturing enabled.</summary>
        public const byte TexturingEnable = 1;

        /// <summary>Geometry flag marking a mesh the viewer must skip.</summary>
        public const uint GeometryHidden = 0x1000;

        /// <summary>Geometry flag disabling backface culling for a mesh.</summary>
        public const uint GeometryTwoSided = 0x2000;
    }
}
