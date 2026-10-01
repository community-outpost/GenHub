// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Constants;

/// <summary>
/// Catalog constants for the WorldBuilder game-data layer: object-tree node names, Thing,
/// Terrain, Road, Bridge, script-template, and player-template field keys, block tokens
/// queried from the SAGE INI database, and the compiled picker tables ported from the
/// engine (EditorSorting, KindOf, ObjectStatus, terrain classes, script parameter lists).
/// </summary>
public static class WorldBuilderCatalogConstants
{
    /// <summary>
    /// Object-tree node names, mirroring ObjectOptions::addObject tiers.
    /// </summary>
    public static class ObjectTree
    {
        /// <summary>Root node for templates whose EditorSorting is TEST.</summary>
        public const string TestNodeName = "TEST";

        /// <summary>Sorting tier for templates with no recognized EditorSorting value.</summary>
        public const string UnsortedName = "UNSORTED";

        /// <summary>Fallback side tier when a template declares no Side.</summary>
        public const string MissingSideName = "UNSORTED";

        /// <summary>Root node for legacy model entries without a ThingTemplate.</summary>
        public const string LegacyModelsNodeName = "**TEST MODELS";
    }

    /// <summary>
    /// Block tokens queried from the SAGE INI database by the catalog services.
    /// </summary>
    public static class Blocks
    {
        /// <summary>Terrain material block.</summary>
        public const string Terrain = "Terrain";

        /// <summary>Road template block.</summary>
        public const string Road = "Road";

        /// <summary>Bridge template block.</summary>
        public const string Bridge = "Bridge";

        /// <summary>Science prerequisite block.</summary>
        public const string Science = "Science";

        /// <summary>Upgrade block.</summary>
        public const string Upgrade = "Upgrade";

        /// <summary>Special power block.</summary>
        public const string SpecialPower = "SpecialPower";

        /// <summary>Player template (faction) block.</summary>
        public const string PlayerTemplate = "PlayerTemplate";

        /// <summary>Script action display-override block.</summary>
        public const string ScriptAction = "ScriptAction";

        /// <summary>Script condition display-override block.</summary>
        public const string ScriptCondition = "ScriptCondition";

        /// <summary>Sound-effect audio event block.</summary>
        public const string AudioEvent = "AudioEvent";

        /// <summary>Dialog (streaming speech) audio event block.</summary>
        public const string DialogEvent = "DialogEvent";

        /// <summary>Music track block.</summary>
        public const string MusicTrack = "MusicTrack";

        /// <summary>Video (movie) block.</summary>
        public const string Video = "Video";

        /// <summary>Command button block.</summary>
        public const string CommandButton = "CommandButton";

        /// <summary>2D animation (emoticon source) block.</summary>
        public const string Animation = "Animation";
    }

    /// <summary>
    /// Editor-relevant ThingTemplate field keys from s_objectFieldParseTable.
    /// </summary>
    public static class ThingFields
    {
        /// <summary>Localized display label (LABEL: prefix plus CSF key).</summary>
        public const string DisplayName = "DisplayName";

        /// <summary>Default owning side; drives the first tree tier.</summary>
        public const string Side = "Side";

        /// <summary>Editor sorting bucket; drives the second tree tier.</summary>
        public const string EditorSortingKey = "EditorSorting";

        /// <summary>KindOf flag list.</summary>
        public const string KindOfKey = "KindOf";

        /// <summary>Buildable status.</summary>
        public const string Buildable = "Buildable";

        /// <summary>Build cost in credits.</summary>
        public const string BuildCost = "BuildCost";

        /// <summary>Build time in seconds.</summary>
        public const string BuildTime = "BuildTime";

        /// <summary>Tree and preview color (R:r G:g B:b [A:a]).</summary>
        public const string DisplayColor = "DisplayColor";

        /// <summary>MappedImage name for the command-button cameo.</summary>
        public const string ButtonImage = "ButtonImage";

        /// <summary>MappedImage name for the selection portrait.</summary>
        public const string SelectPortrait = "SelectPortrait";

        /// <summary>Command button set name.</summary>
        public const string CommandSet = "CommandSet";

        /// <summary>Alternate template names built for this template.</summary>
        public const string BuildVariations = "BuildVariations";

        /// <summary>Asset scale applied to the preview model.</summary>
        public const string Scale = "Scale";
    }

    /// <summary>
    /// Draw-module type and model-name field keys for preview model resolution.
    /// </summary>
    public static class DrawModules
    {
        /// <summary>W3D model draw module; model comes from the first condition state Model field.</summary>
        public const string W3DModelDraw = "W3DModelDraw";

        /// <summary>W3D tree draw module; model comes from the ModelName field.</summary>
        public const string W3DTreeDraw = "W3DTreeDraw";

        /// <summary>Model field inside a ModelDraw condition state.</summary>
        public const string Model = "Model";

        /// <summary>Model name field of a W3DTreeDraw module.</summary>
        public const string ModelName = "ModelName";
    }

    /// <summary>
    /// EditorSorting bucket names in engine order (ThingSort.h EditorSortingNames).
    /// </summary>
    public static class EditorSorting
    {
        /// <summary>Sorting value routed under the TEST root node.</summary>
        public const string Test = "TEST";

        /// <summary>All sorting names, index-aligned with the engine enum.</summary>
        public static readonly string[] Names =
        [
            "NONE",
            "STRUCTURE",
            "INFANTRY",
            "VEHICLE",
            "SHRUBBERY",
            "MISC_MAN_MADE",
            "MISC_NATURAL",
            "DEBRIS",
            "SYSTEM",
            "AUDIO",
            "TEST",
            "FOR_REVIEW",
            "ROAD",
            "WAYPOINT",
        ];
    }

    /// <summary>
    /// Terrain block field keys from m_terrainTypeFieldParseTable.
    /// </summary>
    public static class TerrainFields
    {
        /// <summary>Texture file under Art\Textures.</summary>
        public const string Texture = "Texture";

        /// <summary>True when the entry is a blend edge (excluded from the palette).</summary>
        public const string BlendEdges = "BlendEdges";

        /// <summary>Terrain class name (palette sort key).</summary>
        public const string Class = "Class";

        /// <summary>True when construction is restricted on this terrain.</summary>
        public const string RestrictConstruction = "RestrictConstruction";

        /// <summary>Water glint strength.</summary>
        public const string GlintStrength = "GlintStrength";

        /// <summary>Water glint gloss.</summary>
        public const string GlintGloss = "GlintGloss";
    }

    /// <summary>
    /// Terrain class names in engine order (terrainTypeNames).
    /// </summary>
    public static class TerrainClasses
    {
        /// <summary>All class names, index-aligned with the engine enum.</summary>
        public static readonly string[] Names =
        [
            "NONE",
            "DESERT_1",
            "DESERT_2",
            "DESERT_3",
            "EASTERN_EUROPE_1",
            "EASTERN_EUROPE_2",
            "EASTERN_EUROPE_3",
            "SWISS_1",
            "SWISS_2",
            "SWISS_3",
            "SNOW_1",
            "SNOW_2",
            "SNOW_3",
            "DIRT",
            "GRASS",
            "TRANSITION",
            "ROCK",
            "SAND",
            "CLIFF",
            "WOOD",
            "BLEND_EDGE",
            "DESERT_LIVE",
            "DESERT_DRY",
            "SAND_ACCENT",
            "BEACH_TROPICAL",
            "BEACH_PARK",
            "MOUNTAIN_RUGGED",
            "GRASS_COBBLESTONE",
            "GRASS_ACCENT",
            "RESIDENTIAL",
            "SNOW_RUGGED",
            "SNOW_FLAT",
            "FIELD",
            "ASPHALT",
            "CONCRETE",
            "CHINA",
            "ROCK_ACCENT",
            "URBAN",
        ];
    }

    /// <summary>
    /// Road block field keys from m_terrainRoadFieldParseTable.
    /// </summary>
    public static class RoadFields
    {
        /// <summary>Road texture file.</summary>
        public const string Texture = "Texture";

        /// <summary>Road width in world units.</summary>
        public const string RoadWidth = "RoadWidth";

        /// <summary>Road width inside the texture.</summary>
        public const string RoadWidthInTexture = "RoadWidthInTexture";
    }

    /// <summary>
    /// Bridge block field keys from m_terrainBridgeFieldParseTable.
    /// </summary>
    public static class BridgeFields
    {
        /// <summary>Bridge model scale.</summary>
        public const string BridgeScale = "BridgeScale";

        /// <summary>Scaffold object template name.</summary>
        public const string ScaffoldObjectName = "ScaffoldObjectName";

        /// <summary>Scaffold support object template name.</summary>
        public const string ScaffoldSupportObjectName = "ScaffoldSupportObjectName";

        /// <summary>Radar-map color.</summary>
        public const string RadarColor = "RadarColor";

        /// <summary>Damage-transition effect height.</summary>
        public const string TransitionEffectsHeight = "TransitionEffectsHeight";

        /// <summary>Effect count per damage transition.</summary>
        public const string NumFXPerType = "NumFXPerType";

        /// <summary>Pristine bridge deck model.</summary>
        public const string BridgeModelName = "BridgeModelName";

        /// <summary>Pristine bridge texture.</summary>
        public const string Texture = "Texture";

        /// <summary>Damaged bridge deck model.</summary>
        public const string BridgeModelNameDamaged = "BridgeModelNameDamaged";

        /// <summary>Damaged bridge texture.</summary>
        public const string TextureDamaged = "TextureDamaged";

        /// <summary>Heavily damaged bridge deck model.</summary>
        public const string BridgeModelNameReallyDamaged = "BridgeModelNameReallyDamaged";

        /// <summary>Heavily damaged bridge texture.</summary>
        public const string TextureReallyDamaged = "TextureReallyDamaged";

        /// <summary>Broken bridge deck model.</summary>
        public const string BridgeModelNameBroken = "BridgeModelNameBroken";

        /// <summary>Broken bridge texture.</summary>
        public const string TextureBroken = "TextureBroken";

        /// <summary>Tower object on the from-left corner.</summary>
        public const string TowerObjectNameFromLeft = "TowerObjectNameFromLeft";

        /// <summary>Tower object on the from-right corner.</summary>
        public const string TowerObjectNameFromRight = "TowerObjectNameFromRight";

        /// <summary>Tower object on the to-left corner.</summary>
        public const string TowerObjectNameToLeft = "TowerObjectNameToLeft";

        /// <summary>Tower object on the to-right corner.</summary>
        public const string TowerObjectNameToRight = "TowerObjectNameToRight";

        /// <summary>Sound played on a damage transition.</summary>
        public const string DamagedToSound = "DamagedToSound";

        /// <summary>Sound played on a repair transition.</summary>
        public const string RepairedToSound = "RepairedToSound";

        /// <summary>Object creation list fired on a damage or repair transition.</summary>
        public const string TransitionToOCL = "TransitionToOCL";

        /// <summary>Effect list fired on a damage or repair transition.</summary>
        public const string TransitionToFX = "TransitionToFX";

        /// <summary>Fraction of the deck removed as a hole when broken.</summary>
        public const string BridgeHoleAreaPercentage = "BridgeHoleAreaPercentage";
    }

    /// <summary>
    /// ScriptAction and ScriptCondition block field keys from TheTemplateFieldParseTable.
    /// </summary>
    public static class ScriptTemplateFields
    {
        /// <summary>Compiled internal name; the template key.</summary>
        public const string InternalName = "InternalName";

        /// <summary>Primary display name override.</summary>
        public const string UIName = "UIName";

        /// <summary>Secondary display name override.</summary>
        public const string UIName2 = "UIName2";

        /// <summary>Editor help text override.</summary>
        public const string HelpText = "HelpText";
    }

    /// <summary>
    /// PlayerTemplate field keys used by the faction and side pickers.
    /// </summary>
    public static class PlayerTemplateFields
    {
        /// <summary>Faction side string (getAllSideStrings source).</summary>
        public const string Side = "Side";
    }

    /// <summary>
    /// String-table label conventions.
    /// </summary>
    public static class StringLabels
    {
        /// <summary>Prefix stripped from DisplayName values before CSF lookup.</summary>
        public const string LabelPrefix = "LABEL:";
    }

    /// <summary>
    /// Text .str file conventions (Generals.str source format).
    /// </summary>
    public static class StrFile
    {
        /// <summary>Terminates one label entry (matched case-insensitively).</summary>
        public const string EndToken = "End";

        /// <summary>Introduces a quoted value line.</summary>
        public const char Quote = '"';

        /// <summary>Default language directory used for the CSF path.</summary>
        public const string DefaultLanguage = "English";
    }

    /// <summary>
    /// KindOf flag names in engine order (KindOfMaskType::s_bitNameList).
    /// </summary>
    public static class KindOf
    {
        /// <summary>All flag names for the KIND_OF picker.</summary>
        public static readonly string[] Names =
        [
            "OBSTACLE",
            "SELECTABLE",
            "IMMOBILE",
            "CAN_ATTACK",
            "STICK_TO_TERRAIN_SLOPE",
            "CAN_CAST_REFLECTIONS",
            "SHRUBBERY",
            "STRUCTURE",
            "INFANTRY",
            "VEHICLE",
            "AIRCRAFT",
            "HUGE_VEHICLE",
            "DOZER",
            "HARVESTER",
            "COMMANDCENTER",
            "PRISON",
            "COLLECTS_PRISON_BOUNTY",
            "POW_TRUCK",
            "LINEBUILD",
            "SALVAGER",
            "WEAPON_SALVAGER",
            "TRANSPORT",
            "BRIDGE",
            "LANDMARK_BRIDGE",
            "BRIDGE_TOWER",
            "PROJECTILE",
            "PRELOAD",
            "NO_GARRISON",
            "WAVEGUIDE",
            "WAVE_EFFECT",
            "NO_COLLIDE",
            "REPAIR_PAD",
            "HEAL_PAD",
            "STEALTH_GARRISON",
            "CASH_GENERATOR",
            "AIRFIELD",
            "DRAWABLE_ONLY",
            "MP_COUNT_FOR_VICTORY",
            "REBUILD_HOLE",
            "SCORE",
            "SCORE_CREATE",
            "SCORE_DESTROY",
            "NO_HEAL_ICON",
            "CAN_RAPPEL",
            "PARACHUTABLE",
            "CAN_SURRENDER",
            "CAN_BE_REPULSED",
            "MOB_NEXUS",
            "IGNORED_IN_GUI",
            "CRATE",
            "CAPTURABLE",
            "CLEARED_BY_BUILD",
            "SMALL_MISSILE",
            "ALWAYS_VISIBLE",
            "UNATTACKABLE",
            "MINE",
            "CLEANUP_HAZARD",
            "PORTABLE_STRUCTURE",
            "ALWAYS_SELECTABLE",
            "ATTACK_NEEDS_LINE_OF_SIGHT",
            "WALK_ON_TOP_OF_WALL",
            "DEFENSIVE_WALL",
            "FS_POWER",
            "FS_FACTORY",
            "FS_BASE_DEFENSE",
            "FS_TECHNOLOGY",
            "AIRCRAFT_PATH_AROUND",
            "LOW_OVERLAPPABLE",
            "FORCEATTACKABLE",
            "AUTO_RALLYPOINT",
            "TECH_BUILDING",
            "POWERED",
            "PRODUCED_AT_HELIPAD",
            "DRONE",
            "CAN_SEE_THROUGH_STRUCTURE",
            "BALLISTIC_MISSILE",
            "CLICK_THROUGH",
            "SUPPLY_SOURCE_ON_PREVIEW",
            "PARACHUTE",
            "GARRISONABLE_UNTIL_DESTROYED",
            "BOAT",
            "IMMUNE_TO_CAPTURE",
            "HULK",
            "SHOW_PORTRAIT_WHEN_CONTROLLED",
            "SPAWNS_ARE_THE_WEAPONS",
            "CANNOT_BUILD_NEAR_SUPPLIES",
            "SUPPLY_SOURCE",
            "REVEAL_TO_ALL",
            "DISGUISER",
            "INERT",
            "HERO",
            "IGNORES_SELECT_ALL",
            "DONT_AUTO_CRUSH_INFANTRY",
            "CLIFF_JUMPER",
            "FS_SUPPLY_DROPZONE",
            "FS_SUPERWEAPON",
            "FS_BLACK_MARKET",
            "FS_SUPPLY_CENTER",
            "FS_STRATEGY_CENTER",
            "MONEY_HACKER",
            "ARMOR_SALVAGER",
            "REVEALS_ENEMY_PATHS",
            "BOOBY_TRAP",
            "FS_FAKE",
            "FS_INTERNET_CENTER",
            "BLAST_CRATER",
            "PROP",
            "OPTIMIZED_TREE",
            "FS_ADVANCED_TECH",
            "FS_BARRACKS",
            "FS_WARFACTORY",
            "FS_AIRFIELD",
            "AIRCRAFT_CARRIER",
            "NO_SELECT",
            "REJECT_UNMANNED",
            "CANNOT_RETALIATE",
            "TECH_BASE_DEFENSE",
            "EMP_HARDENED",
            "DEMOTRAP",
            "CONSERVATIVE_BUILDING",
            "IGNORE_DOCKING_BONES",
            "CAN_RETALIATE",
            "NO_BATTLE_PLAN",
            "ENABLE_INFANTRY_LIGHTING",
            "DISABLE_INFANTRY_LIGHTING",
            "SHOW_PROGRESS_BAR",
            "VTOL",
            "LARGE_AIRCRAFT",
            "MEDIUM_AIRCRAFT",
            "SMALL_AIRCRAFT",
            "ARTILLERY",
            "HEAVY_ARTILLERY",
            "ANTI_AIR",
            "SCOUT",
            "COMMANDO",
            "HEAVY_INFANTRY",
            "SUPERHEAVY_VEHICLE",
            "TELEPORTER",
            "SHIPYARD",
            "NO_MOVE_EFFECTS_ON_WATER",
            "SHIP",
            "SUBMARINE",
            "EXTRA1",
            "EXTRA2",
            "EXTRA3",
            "EXTRA4",
            "EXTRA5",
            "EXTRA6",
            "EXTRA7",
            "EXTRA8",
            "EXTRA9",
            "EXTRA10",
            "EXTRA11",
            "EXTRA12",
            "EXTRA13",
            "EXTRA14",
            "EXTRA15",
            "EXTRA16",
            "TARGET_DESIGNATOR",
            "NO_ATTACK_WARNING",
        ];
    }

    /// <summary>
    /// ObjectStatus flag names in engine order (ObjectStatusMaskType::s_bitNameList).
    /// </summary>
    public static class ObjectStatus
    {
        /// <summary>All flag names for the OBJECT_STATUS picker.</summary>
        public static readonly string[] Names =
        [
            "NONE",
            "DESTROYED",
            "CAN_ATTACK",
            "UNDER_CONSTRUCTION",
            "UNSELECTABLE",
            "NO_COLLISIONS",
            "NO_ATTACK",
            "AIRBORNE_TARGET",
            "PARACHUTING",
            "REPULSOR",
            "HIJACKED",
            "AFLAME",
            "BURNED",
            "WET",
            "IS_FIRING_WEAPON",
            "IS_BRAKING",
            "STEALTHED",
            "DETECTED",
            "CAN_STEALTH",
            "SOLD",
            "UNDERGOING_REPAIR",
            "RECONSTRUCTING",
            "MASKED",
            "IS_ATTACKING",
            "USING_ABILITY",
            "IS_AIMING_WEAPON",
            "NO_ATTACK_FROM_AI",
            "IGNORING_STEALTH",
            "IS_CARBOMB",
            "DECK_HEIGHT_OFFSET",
            "STATUS_RIDER1",
            "STATUS_RIDER2",
            "STATUS_RIDER3",
            "STATUS_RIDER4",
            "STATUS_RIDER5",
            "STATUS_RIDER6",
            "STATUS_RIDER7",
            "STATUS_RIDER8",
            "FAERIE_FIRE",
            "KILLING_SELF",
            "REASSIGN_PARKING",
            "BOOBY_TRAPPED",
            "IMMOBILE",
            "DISGUISED",
            "DEPLOYED",
            "STATUS_RIDER9",
            "STATUS_RIDER10",
            "STATUS_RIDER11",
            "STATUS_RIDER12",
            "STATUS_RIDER13",
            "STATUS_RIDER14",
            "STATUS_RIDER15",
            "STATUS_RIDER16",
            "SCUTTLING",
        ];
    }

    /// <summary>
    /// Compiled script-parameter picker lists from EditParameter.cpp.
    /// </summary>
    public static class PickLists
    {
        /// <summary>COMPARISON picker entries.</summary>
        public static readonly string[] Comparison =
        [
            "LT Less Than",
            "LE Less Than or Equal",
            "EQ Equal To",
            "GE Greater Than or Equal",
            "GT Greater Than",
            "NE Not Equal To",
        ];

        /// <summary>AI_MOOD picker entries.</summary>
        public static readonly string[] AiMood =
        [
            "Sleep",
            "Passive",
            "Normal",
            "Alert",
            "Aggressive",
        ];

        /// <summary>SKIRMISH_WAYPOINT_PATH picker entries.</summary>
        public static readonly string[] SkirmishWaypointPaths =
        [
            "Center",
            "Backdoor",
            "Flank",
            "Special",
            "Naval",
            "NavalFlank",
        ];

        /// <summary>RADAR_EVENT_TYPE picker entries.</summary>
        public static readonly string[] RadarEventTypes =
        [
            "Construction",
            "Upgrade",
            "Under Attack",
            "Information",
        ];

        /// <summary>LEFT_OR_RIGHT picker entries.</summary>
        public static readonly string[] LeftOrRight =
        [
            "Left",
            "Right",
            "Center (Default)",
        ];

        /// <summary>RELATION picker entries.</summary>
        public static readonly string[] Relation =
        [
            "Enemy",
            "Neutral",
            "Friend",
        ];

        /// <summary>BUILDABLE picker entries (BuildableStatusNames).</summary>
        public static readonly string[] Buildable =
        [
            "Yes",
            "Ignore_Prerequisites",
            "No",
            "Only_By_AI",
        ];

        /// <summary>SURFACES_ALLOWED picker entries.</summary>
        public static readonly string[] Surfaces =
        [
            "Ground",
            "Air",
            "Ground or Air",
        ];

        /// <summary>SHAKE_INTENSITY picker entries.</summary>
        public static readonly string[] ShakeIntensities =
        [
            "Subtle",
            "Normal",
            "Strong",
            "Severe",
            "Cine_Extreme",
            "Cine_Insane",
        ];

        /// <summary>OBJECT_PANEL_FLAG picker entries (TheObjectFlagsNames).</summary>
        public static readonly string[] ObjectPanelFlags =
        [
            "Enabled",
            "Powered",
            "Indestructible",
            "Unsellable",
            "Selectable",
            "AI Recruitable",
            "Player Targetable",
        ];

        /// <summary>SCIENCE_AVAILABILITY picker entries (ScienceAvailabilityNames).</summary>
        public static readonly string[] ScienceAvailability =
        [
            "Available",
            "Disabled",
            "Hidden",
        ];

        /// <summary>Boundary color names cycled by the BOUNDARY picker (BorderColors.h).</summary>
        public static readonly string[] BorderColors =
        [
            "Orange",
            "Green",
            "Blue",
            "Cyan",
            "Magenta",
            "Yellow",
            "Purple",
            "Pink",
        ];
    }

    /// <summary>
    /// Symbolic script names offered alongside dynamic picker content (Scripts.h).
    /// </summary>
    public static class SymbolicNames
    {
        /// <summary>Symbolic team offered by the TEAM picker.</summary>
        public const string ThisTeam = "<This Team>";

        /// <summary>Symbolic unit offered by the UNIT picker.</summary>
        public const string ThisObject = "<This Object>";

        /// <summary>Symbolic players offered first by the SIDE picker.</summary>
        public static readonly string[] SidePlayers =
        [
            "<Local Player>",
            "<This Player>",
            "<This Player's Enemy>",
        ];
    }
}
