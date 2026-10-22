namespace GenHub.Core.Constants;

/// <summary>
/// Constants for Generals and Zero Hour INI data files
/// (game objects, weapons, upgrades, damage, armor, command sets).
/// Syntax follows the engine INI parser: semicolon comments, blocks opened by a
/// block type line and closed by <c>End</c>. The engine tolerates tab characters,
/// but this tool rejects them so files stay in canonical form.
/// </summary>
public static class IniConstants
{
    /// <summary>
    /// Block tag literals.
    /// </summary>
    public static class BlockTags
    {
        /// <summary>Closes an INI block.</summary>
        public const string End = "End";
    }

    /// <summary>
    /// Statement syntax constants.
    /// </summary>
    public static class Syntax
    {
        /// <summary>Starts a comment running to the end of the line.</summary>
        public const char Comment = ';';

        /// <summary>Separates a key from its value.</summary>
        public const char KeyValueSeparator = '=';

        /// <summary>Canonical newline used when writing files.</summary>
        public const string NewLine = "\r\n";
    }

    /// <summary>
    /// Well known INI block types parsed with schema assistance.
    /// Unknown block types are preserved and edited generically.
    /// </summary>
    public static class BlockTypes
    {
        /// <summary>Defines a game object.</summary>
        public const string Object = "Object";

        /// <summary>Defines a weapon.</summary>
        public const string Weapon = "Weapon";

        /// <summary>Defines armor damage multipliers.</summary>
        public const string Armor = "Armor";

        /// <summary>Defines an armor set condition.</summary>
        public const string ArmorSet = "ArmorSet";

        /// <summary>Defines an object weapon set condition.</summary>
        public const string WeaponSet = "WeaponSet";

        /// <summary>Defines a command button.</summary>
        public const string CommandButton = "CommandButton";

        /// <summary>Defines a command set.</summary>
        public const string CommandSet = "CommandSet";

        /// <summary>Defines an upgrade.</summary>
        public const string Upgrade = "Upgrade";

        /// <summary>Defines a science.</summary>
        public const string Science = "Science";

        /// <summary>Defines a special power.</summary>
        public const string SpecialPower = "SpecialPower";

        /// <summary>Defines a locomotor.</summary>
        public const string Locomotor = "Locomotor";

        /// <summary>Defines an object creation list.</summary>
        public const string ObjectCreationList = "ObjectCreationList";

        /// <summary>Defines damage effects.</summary>
        public const string DamageFX = "DamageFX";

        /// <summary>Defines a player template.</summary>
        public const string PlayerTemplate = "PlayerTemplate";

        /// <summary>Defines experience levels.</summary>
        public const string ExperienceLevels = "ExperienceLevels";

        /// <summary>Defines veterancy multipliers.</summary>
        public const string Veterancy = "Veterancy";

        /// <summary>Defines a mapped image sprite.</summary>
        public const string MappedImage = "MappedImage";

        /// <summary>
        /// All block types with schema assistance.
        /// </summary>
        public static readonly string[] All =
        [
            Object, Weapon, Armor, ArmorSet, WeaponSet, CommandButton, CommandSet,
            Upgrade, Science, SpecialPower, Locomotor, ObjectCreationList,
            DamageFX, PlayerTemplate, ExperienceLevels, Veterancy,
            MappedImage,
        ];
    }

    /// <summary>
    /// Object module slot keys. Lines such as <c>Draw = W3DModelDraw Tag</c> open a
    /// nested module sub-block closed by <c>End</c> instead of acting as plain fields.
    /// Matches the engine object module parsing in the public game code.
    /// </summary>
    public static class ModuleKeys
    {
        /// <summary>Body module slot.</summary>
        public const string Body = "Body";

        /// <summary>Behavior module slot.</summary>
        public const string Behavior = "Behavior";

        /// <summary>Draw module slot.</summary>
        public const string Draw = "Draw";

        /// <summary>Client update module slot.</summary>
        public const string ClientUpdate = "ClientUpdate";

        /// <summary>Draw condition state.</summary>
        public const string ConditionState = "ConditionState";

        /// <summary>Draw model condition state.</summary>
        public const string ModelConditionState = "ModelConditionState";

        /// <summary>Draw transition state.</summary>
        public const string TransitionState = "TransitionState";

        /// <summary>Draw animation state.</summary>
        public const string AnimationState = "AnimationState";

        /// <summary>Draw idle animation state.</summary>
        public const string IdleAnimationState = "IdleAnimationState";

        /// <summary>
        /// All keys that always open a module sub-block.
        /// </summary>
        public static readonly string[] All =
        [
            Body, Behavior, Draw, ClientUpdate, ConditionState,
            ModelConditionState, TransitionState, AnimationState, IdleAnimationState,
        ];
    }

    /// <summary>
    /// Bare valueless entry keys. Lines holding only one of these keys inside a block
    /// are single-line entries rather than nested blocks. The engine credits files
    /// use <c>Blank</c> for empty lines within a credits block.
    /// </summary>
    public static class ValuelessKeys
    {
        /// <summary>Empty line entry in credits blocks.</summary>
        public const string Blank = "Blank";

        /// <summary>
        /// All keys parsed as valueless entries instead of nested blocks.
        /// </summary>
        public static readonly string[] All = [Blank];
    }

    /// <summary>
    /// Death type names from the engine, matching <c>TheDeathNames</c> in the public game code.
    /// </summary>
    public static class DeathTypes
    {
        /// <summary>
        /// All death types in engine canonical order.
        /// </summary>
        public static readonly string[] All =
        [
            "NORMAL", "NONE", "CRUSHED", "BURNED", "EXPLODED", "POISONED",
            "TOPPLED", "FLOODED", "SUICIDED", "LASERED", "DETONATED",
            "SPLATTED", "POISONED_BETA", "EXTRA_2", "EXTRA_3", "EXTRA_4",
            "EXTRA_5", "EXTRA_6", "EXTRA_7", "EXTRA_8", "POISONED_GAMMA",
        ];
    }

    /// <summary>
    /// Armor and weapon set condition flags from the engine, matching
    /// <c>ArmorSetFlags</c> in the public game code.
    /// </summary>
    public static class ArmorSetConditions
    {
        /// <summary>
        /// All set condition flags in engine canonical order.
        /// </summary>
        public static readonly string[] All =
        [
            "VETERAN", "ELITE", "HERO", "PLAYER_UPGRADE",
            "WEAK_VERSUS_BASEDEFENSES", "SECOND_LIFE",
            "CRATE_UPGRADE_ONE", "CRATE_UPGRADE_TWO",
        ];
    }

    /// <summary>
    /// Damage type names from the engine damage system.
    /// </summary>
    public static class DamageTypes
    {
        /// <summary>
        /// All Zero Hour damage types in engine order, matching
        /// <c>DamageTypeFlags::s_bitNameList</c> in the public game code.
        /// Original Generals additionally defines <c>FLESHY_SNIPER</c>.
        /// </summary>
        public static readonly string[] All =
        [
            "EXPLOSION", "CRUSH", "ARMOR_PIERCING", "SMALL_ARMS", "GATTLING",
            "RADIATION", "FLAME", "LASER", "SNIPER", "POISON", "HEALING",
            "UNRESISTABLE", "WATER", "DEPLOY", "SURRENDER", "HACK", "KILL_PILOT",
            "PENALTY", "FALLING", "MELEE", "DISARM", "HAZARD_CLEANUP",
            "PARTICLE_BEAM", "TOPPLING", "INFANTRY_MISSILE", "AURORA_BOMB",
            "LAND_MINE", "JET_MISSILES", "STEALTHJET_MISSILES", "MOLOTOV_COCKTAIL",
            "COMANCHE_VULCAN", "SUBDUAL_MISSILE", "SUBDUAL_VEHICLE",
            "SUBDUAL_BUILDING", "SUBDUAL_UNRESISTABLE", "MICROWAVE",
            "KILL_GARRISONED", "STATUS",
        ];
    }

    /// <summary>
    /// Well known INI field keys shared by the schema and the editor.
    /// </summary>
    public static class FieldKeys
    {
        /// <summary>Display name key.</summary>
        public const string DisplayName = "DisplayName";

        /// <summary>Button image key.</summary>
        public const string ButtonImage = "ButtonImage";

        /// <summary>Build cost key.</summary>
        public const string BuildCost = "BuildCost";

        /// <summary>Build time key.</summary>
        public const string BuildTime = "BuildTime";

        /// <summary>Damage type key.</summary>
        public const string DamageType = "DamageType";

        /// <summary>Primary damage key.</summary>
        public const string PrimaryDamage = "PrimaryDamage";

        /// <summary>Primary damage radius key.</summary>
        public const string PrimaryDamageRadius = "PrimaryDamageRadius";

        /// <summary>Death type key.</summary>
        public const string DeathType = "DeathType";

        /// <summary>Upgrade key.</summary>
        public const string Upgrade = "Upgrade";

        /// <summary>Triggered by key.</summary>
        public const string TriggeredBy = "TriggeredBy";

        /// <summary>Health key.</summary>
        public const string Health = "Health";

        /// <summary>Side key.</summary>
        public const string Side = "Side";

        /// <summary>Icon key.</summary>
        public const string Icon = "Icon";

        /// <summary>Attack range key.</summary>
        public const string AttackRange = "AttackRange";

        /// <summary>Weapon speed key.</summary>
        public const string WeaponSpeed = "WeaponSpeed";

        /// <summary>Command key.</summary>
        public const string Command = "Command";

        /// <summary>Object key.</summary>
        public const string Object = "Object";

        /// <summary>Text label key.</summary>
        public const string TextLabel = "TextLabel";

        /// <summary>Type key.</summary>
        public const string Type = "Type";

        /// <summary>Speed key.</summary>
        public const string Speed = "Speed";

        /// <summary>Turn rate key.</summary>
        public const string TurnRate = "TurnRate";

        /// <summary>Lift key.</summary>
        public const string Lift = "Lift";

        /// <summary>Appearance key.</summary>
        public const string Appearance = "Appearance";

        /// <summary>Upgrades key.</summary>
        public const string Upgrades = "Upgrades";
    }

    /// <summary>
    /// Reference index cache constants.
    /// </summary>
    public static class Cache
    {
        /// <summary>Temporary directory name for extracted vanilla INI archives.</summary>
        public const string VanillaDirectoryName = "GenHub_IniVanilla";

        /// <summary>Marker file proving a vanilla extraction completed.</summary>
        public const string ExtractedMarkerFileName = ".extracted";
    }

    /// <summary>
    /// Editor limits.
    /// </summary>
    public static class Editor
    {
        /// <summary>Maximum undo history entries.</summary>
        public const int MaxUndoHistory = 200;

        /// <summary>Debounce delay before refreshing previews after an edit, in milliseconds.</summary>
        public const int PreviewRefreshDebounceMs = 250;

        /// <summary>Debounce delay before applying the block filter, in milliseconds.</summary>
        public const int FilterDebounceMs = 200;

        /// <summary>Debounce delay before refreshing texture thumbnails, in milliseconds.</summary>
        public const int ThumbnailDebounceMs = 150;

        /// <summary>Maximum reference results shown in the reference browser.</summary>
        public const int MaxReferenceResults = 500;

        /// <summary>Maximum picker textures preloaded with thumbnails.</summary>
        public const int MaxPickerThumbnails = 64;

        /// <summary>Maximum mapped image files parsed for texture pickers.</summary>
        public const int MaxMappedImageFiles = 50;

        /// <summary>Maximum mapped image definitions offered by texture pickers.</summary>
        public const int MaxPickerDefinitions = 2000;

        /// <summary>Maximum characters rendered in the raw text preview.</summary>
        public const int MaxRawPreviewChars = 100000;
    }
}
