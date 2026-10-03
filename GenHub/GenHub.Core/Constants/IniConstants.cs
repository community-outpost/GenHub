namespace GenHub.Core.Constants;

using System.Collections.Generic;

/// <summary>
/// Constants for Generals and Zero Hour INI data files
/// (game objects, weapons, upgrades, damage, armor, command sets).
/// Syntax follows the engine INI parser: semicolon comments, blocks opened by a
/// block type line and closed by <c>End</c>. The engine tolerates tab characters,
/// and this tool normalizes them to preserve structure.
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

        /// <summary>Marks a trailing production-time percentage.</summary>
        public const char PercentSuffix = '%';

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
        /// All top-level block types with schema assistance.
        /// </summary>
        public static readonly string[] All =
        [
            Object,
            Weapon,
            Armor,
            ArmorSet,
            WeaponSet,
            CommandButton,
            CommandSet,
            Upgrade,
            Science,
            SpecialPower,
            Locomotor,
            ObjectCreationList,
            DamageFX,
            PlayerTemplate,
            ExperienceLevels,
            Veterancy,
            MappedImage,
            SubBlockTypes.AudioEvent,
            SubBlockTypes.DialogEvent,
            SubBlockTypes.ParticleSystem,
        ];
    }

    /// <summary>
    /// Reference values the engine accepts without a matching definition.
    /// </summary>
    public static class ReferenceSentinels
    {
        /// <summary>Null audio marker accepted anywhere an audio event is referenced.</summary>
        public const string NoSound = "NoSound";
    }

    /// <summary>
    /// Special body values the engine accepts on object health.
    /// </summary>
    public static class BodyMarkers
    {
        /// <summary>Indestructible body marker.</summary>
        public const string Immortal = "Immortal";
    }

    /// <summary>
    /// Additional sub-block and map.ini directive block types.
    /// </summary>
    public static class SubBlockTypes
    {
        /// <summary>Default condition state sub-block.</summary>
        public const string DefaultConditionState = "DefaultConditionState";

        /// <summary>Condition state sub-block.</summary>
        public const string ConditionState = "ConditionState";

        /// <summary>Transition state sub-block.</summary>
        public const string TransitionState = "TransitionState";

        /// <summary>Animation state sub-block.</summary>
        public const string AnimationState = "AnimationState";

        /// <summary>Replace module in map.ini.</summary>
        public const string ReplaceModule = "ReplaceModule";

        /// <summary>Add module in map.ini.</summary>
        public const string AddModule = "AddModule";

        /// <summary>Prerequisites sub-block.</summary>
        public const string Prerequisites = "Prerequisites";

        /// <summary>Turret sub-block.</summary>
        public const string Turret = "Turret";

        /// <summary>Alt turret sub-block.</summary>
        public const string AltTurret = "AltTurret";

        /// <summary>Death definition sub-block inside slow death behaviors.</summary>
        public const string Die = "Die";

        /// <summary>Create object sub-block.</summary>
        public const string CreateObject = "CreateObject";

        /// <summary>Create debris sub-block.</summary>
        public const string CreateDebris = "CreateDebris";

        /// <summary>Deliver payload sub-block.</summary>
        public const string DeliverPayload = "DeliverPayload";

        /// <summary>Apply random force sub-block.</summary>
        public const string ApplyRandomForce = "ApplyRandomForce";

        /// <summary>Fire weapon sub-block.</summary>
        public const string FireWeapon = "FireWeapon";

        /// <summary>Damage nugget sub-block inside weapons.</summary>
        public const string DamageNugget = "DamageNugget";

        /// <summary>Damage over time nugget sub-block inside weapons.</summary>
        public const string DOTNugget = "DOTNugget";

        /// <summary>Paralyze nugget sub-block inside weapons.</summary>
        public const string ParalyzeNugget = "ParalyzeNugget";

        /// <summary>Steal money nugget sub-block inside weapons.</summary>
        public const string StealMoneyNugget = "StealMoneyNugget";

        /// <summary>Meta impact nugget sub-block inside weapons.</summary>
        public const string MetaImpactNugget = "MetaImpactNugget";

        /// <summary>FXList at bone pos sub-block.</summary>
        public const string FXListAtBonePos = "FXListAtBonePos";

        /// <summary>Damage event sub-block inside damage effects.</summary>
        public const string DamageFXEvent = "DamageFXEvent";

        /// <summary>Unit specific effects sub-block inside objects.</summary>
        public const string UnitSpecificFX = "UnitSpecificFX";

        /// <summary>Attack area decal sub-block inside objects.</summary>
        public const string AttackAreaDecal = "AttackAreaDecal";

        /// <summary>Targeting reticle decal sub-block inside objects.</summary>
        public const string TargetingReticleDecal = "TargetingReticleDecal";

        /// <summary>Grid decal template sub-block inside objects.</summary>
        public const string GridDecalTemplate = "GridDecalTemplate";

        /// <summary>Delivery decal sub-block inside objects and creation lists.</summary>
        public const string DeliveryDecal = "DeliveryDecal";

        /// <summary>Sound sub-block inside effect lists.</summary>
        public const string Sound = "Sound";

        /// <summary>Light pulse sub-block inside effect lists.</summary>
        public const string LightPulse = "LightPulse";

        /// <summary>View shake sub-block inside effect lists.</summary>
        public const string ViewShake = "ViewShake";

        /// <summary>Terrain scorch sub-block inside effect lists.</summary>
        public const string TerrainScorch = "TerrainScorch";

        /// <summary>Tracer sub-block inside effect lists.</summary>
        public const string Tracer = "Tracer";

        /// <summary>Attack sub-block inside object creation lists.</summary>
        public const string Attack = "Attack";

        /// <summary>Mission sub-block inside campaigns.</summary>
        public const string Mission = "Mission";

        /// <summary>Image part sub-block inside command bar schemes.</summary>
        public const string ImagePart = "ImagePart";

        /// <summary>Side info sub-block inside AI data.</summary>
        public const string SideInfo = "SideInfo";

        /// <summary>First AI skill set sub-block inside side info.</summary>
        public const string SkillSet1 = "SkillSet1";

        /// <summary>Second AI skill set sub-block inside side info.</summary>
        public const string SkillSet2 = "SkillSet2";

        /// <summary>Third AI skill set sub-block inside side info.</summary>
        public const string SkillSet3 = "SkillSet3";

        /// <summary>Fourth AI skill set sub-block inside side info.</summary>
        public const string SkillSet4 = "SkillSet4";

        /// <summary>Fifth AI skill set sub-block inside side info.</summary>
        public const string SkillSet5 = "SkillSet5";

        /// <summary>Skirmish build list sub-block inside AI data.</summary>
        public const string SkirmishBuildList = "SkirmishBuildList";

        /// <summary>Structure sub-block inside skirmish build lists.</summary>
        public const string Structure = "Structure";

        /// <summary>Inheritable module sub-block inside object defaults.</summary>
        public const string InheritableModule = "InheritableModule";

        /// <summary>Overrideable by like kind sub-block inside object defaults.</summary>
        public const string OverrideableByLikeKind = "OverrideableByLikeKind";

        /// <summary>Side sounds sub-block inside EVA events.</summary>
        public const string SideSounds = "SideSounds";

        /// <summary>Window sub-block inside window transitions.</summary>
        public const string Window = "Window";

        /// <summary>Unit specific sounds sub-block.</summary>
        public const string UnitSpecificSounds = "UnitSpecificSounds";

        /// <summary>Water transparency block.</summary>
        public const string WaterTransparency = "WaterTransparency";

        /// <summary>Weather block.</summary>
        public const string Weather = "Weather";

        /// <summary>AI Data block.</summary>
        public const string AIData = "AIData";

        /// <summary>Audio event block.</summary>
        public const string AudioEvent = "AudioEvent";

        /// <summary>Dialog event block.</summary>
        public const string DialogEvent = "DialogEvent";

        /// <summary>Music track block.</summary>
        public const string MusicTrack = "MusicTrack";

        /// <summary>FXList block.</summary>
        public const string FXList = "FXList";

        /// <summary>Particle system block.</summary>
        public const string ParticleSystem = "ParticleSystem";

        /// <summary>Object reskin block.</summary>
        public const string ObjectReskin = "ObjectReskin";

        /// <summary>
        /// All sub-block and directive types.
        /// </summary>
        public static readonly string[] All =
        [
            DefaultConditionState,
            ConditionState,
            TransitionState,
            AnimationState,
            ReplaceModule,
            AddModule,
            Prerequisites,
            Turret,
            AltTurret,
            Die,
            CreateObject,
            CreateDebris,
            DeliverPayload,
            ApplyRandomForce,
            FireWeapon,
            DamageNugget,
            DOTNugget,
            ParalyzeNugget,
            StealMoneyNugget,
            MetaImpactNugget,
            FXListAtBonePos,
            DamageFXEvent,
            UnitSpecificFX,
            AttackAreaDecal,
            TargetingReticleDecal,
            GridDecalTemplate,
            DeliveryDecal,
            Sound,
            LightPulse,
            ViewShake,
            TerrainScorch,
            Tracer,
            Attack,
            Mission,
            ImagePart,
            SideInfo,
            SkillSet1,
            SkillSet2,
            SkillSet3,
            SkillSet4,
            SkillSet5,
            SkirmishBuildList,
            Structure,
            InheritableModule,
            OverrideableByLikeKind,
            SideSounds,
            Window,
            UnitSpecificSounds,
            WaterTransparency,
            Weather,
            AIData,
            AudioEvent,
            DialogEvent,
            MusicTrack,
            FXList,
            ParticleSystem,
            ObjectReskin,
        ];
    }

    /// <summary>
    /// Name patterns for parameterized engine sub-blocks that cannot be enumerated.
    /// </summary>
    public static class SubBlockTypePatterns
    {
        /// <summary>Prefix for numbered challenge mode general persona blocks.</summary>
        public const string GeneralPersonaPrefix = "GeneralPersona";

        /// <summary>Suffix for interface radius cursor blocks.</summary>
        public const string RadiusCursorSuffix = "RadiusCursor";
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

        /// <summary>Default condition state.</summary>
        public const string DefaultConditionState = "DefaultConditionState";

        /// <summary>Replace module in map.ini.</summary>
        public const string ReplaceModule = "ReplaceModule";

        /// <summary>Add module in map.ini.</summary>
        public const string AddModule = "AddModule";

        /// <summary>
        /// Animation key. Deliberately excluded from <see cref="All"/>: it opens a
        /// nested block only inside AnimationState and TransitionState parents when
        /// followed by animation block fields, and stays a plain field elsewhere
        /// (notably inside ConditionState modules, which list several Animation fields).
        /// </summary>
        public const string Animation = "Animation";

        /// <summary>
        /// All keys that always open a module sub-block.
        /// </summary>
        public static readonly string[] All =
        [
            Body, Behavior, Draw, ClientUpdate, ConditionState,
            ModelConditionState, TransitionState, AnimationState, IdleAnimationState,
            DefaultConditionState, ReplaceModule, AddModule,
        ];
    }

    /// <summary>
    /// Field keys found inside nested <c>Animation = Name ... End</c> sub-blocks.
    /// The parser uses them to tell animation sub-blocks apart from lone
    /// field style <c>Animation = Name</c> lines.
    /// </summary>
    public static class AnimationBlockFields
    {
        /// <summary>Animation asset name key.</summary>
        public const string AnimationName = "AnimationName";

        /// <summary>Animation playback mode key.</summary>
        public const string AnimationMode = "AnimationMode";

        /// <summary>Animation blend time key.</summary>
        public const string AnimationBlendTime = "AnimationBlendTime";

        /// <summary>Animation speed factor range key.</summary>
        public const string AnimationSpeedFactorRange = "AnimationSpeedFactorRange";

        /// <summary>Animation priority key.</summary>
        public const string AnimationPriority = "AnimationPriority";

        /// <summary>Animation must complete blend key.</summary>
        public const string AnimationMustCompleteBlend = "AnimationMustCompleteBlend";

        /// <summary>Fade begin frame key.</summary>
        public const string FadeBeginFrame = "FadeBeginFrame";

        /// <summary>Fade end frame key.</summary>
        public const string FadeEndFrame = "FadeEndFrame";

        /// <summary>Use weapon timing key.</summary>
        public const string UseWeaponTiming = "UseWeaponTiming";

        /// <summary>
        /// All field keys identifying a nested Animation sub-block.
        /// </summary>
        public static readonly string[] All =
        [
            AnimationName,
            AnimationMode,
            AnimationBlendTime,
            AnimationSpeedFactorRange,
            AnimationPriority,
            AnimationMustCompleteBlend,
            FadeBeginFrame,
            FadeEndFrame,
            UseWeaponTiming,
        ];
    }

    /// <summary>
    /// Map override whitespace directives. Lines such as <c>RemoveModule ModuleTag</c>
    /// in map INI overrides always use whitespace syntax; serializing them with
    /// <c>Key = Value</c> would change the engine command's meaning.
    /// </summary>
    public static class MapDirectives
    {
        /// <summary>Remove module directive in map INI overrides.</summary>
        public const string RemoveModule = "RemoveModule";
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
    /// Boolean option values for toggle fields such as <c>IsTrainable</c>.
    /// </summary>
    public static class BooleanOptions
    {
        /// <summary>Enabled value.</summary>
        public const string Yes = "YES";

        /// <summary>Disabled value.</summary>
        public const string No = "NO";

        /// <summary>All boolean option values.</summary>
        public static readonly string[] All = [Yes, No];
    }

    /// <summary>
    /// Well known command button command verbs from the engine command list.
    /// Offered as suggestions alongside document values so custom commands still appear.
    /// </summary>
    public static class CommandButtonCommands
    {
        /// <summary>
        /// Known command verbs in alphabetical order.
        /// </summary>
        public static readonly string[] All =
        [
            "BOMBARD",
            "CONSTRUCT",
            "DOZER_CONSTRUCT",
            "FIRE_WEAPON",
            "GUARD",
            "GUARD_WITHOUT_PURSUIT",
            "HACK",
            "REPAIR",
            "SPECIAL_POWER",
            "STOP",
            "UNIT_BUILD",
        ];
    }

    /// <summary>
    /// Well known command button border types.
    /// </summary>
    public static class CommandButtonBorderTypes
    {
        /// <summary>
        /// Known border types in alphabetical order.
        /// </summary>
        public static readonly string[] All =
        [
            "ACTION",
            "BUILD",
            "SYSTEM",
        ];
    }

    /// <summary>
    /// Well known command button option flags.
    /// </summary>
    public static class CommandButtonOptions
    {
        /// <summary>
        /// Known option flags in alphabetical order.
        /// </summary>
        public static readonly string[] All =
        [
            "NEED_SPECIAL_POWER",
            "NEED_TRIGGERED_SPECIAL_POWER",
            "OK_FOR_MULTI_SELECT",
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

        /// <summary>Select portrait key.</summary>
        public const string SelectPortrait = "SelectPortrait";

        /// <summary>Kind of flags key.</summary>
        public const string KindOf = "KindOf";

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

        /// <summary>Max health key.</summary>
        public const string MaxHealth = "MaxHealth";

        /// <summary>Initial health key.</summary>
        public const string InitialHealth = "InitialHealth";

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

        /// <summary>Armor key.</summary>
        public const string Armor = "Armor";

        /// <summary>Weapon key.</summary>
        public const string Weapon = "Weapon";

        /// <summary>Vision range key.</summary>
        public const string VisionRange = "VisionRange";

        /// <summary>Shroud clearing range key.</summary>
        public const string ShroudClearingRange = "ShroudClearingRange";

        /// <summary>Delay between shots key.</summary>
        public const string DelayBetweenShots = "DelayBetweenShots";

        /// <summary>Button border type key.</summary>
        public const string ButtonBorderType = "ButtonBorderType";

        /// <summary>Target object key.</summary>
        public const string TargetObject = "TargetObject";

        /// <summary>Body key.</summary>
        public const string Body = "Body";

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

        /// <summary>Experience required key.</summary>
        public const string ExperienceRequired = "ExperienceRequired";

        /// <summary>Is trainable key.</summary>
        public const string IsTrainable = "IsTrainable";

        /// <summary>Crushable level key.</summary>
        public const string CrushableLevel = "CrushableLevel";

        /// <summary>Max simultaneous of type key.</summary>
        public const string MaxSimultaneousOfType = "MaxSimultaneousOfType";

        /// <summary>Voice select key.</summary>
        public const string VoiceSelect = "VoiceSelect";

        /// <summary>Voice move key.</summary>
        public const string VoiceMove = "VoiceMove";

        /// <summary>Voice attack key.</summary>
        public const string VoiceAttack = "VoiceAttack";

        /// <summary>Voice fear key.</summary>
        public const string VoiceFear = "VoiceFear";

        /// <summary>Voice guard key.</summary>
        public const string VoiceGuard = "VoiceGuard";

        /// <summary>Sound stealth on key.</summary>
        public const string SoundStealthOn = "SoundStealthOn";

        /// <summary>Production time change key used by upgrade-style effect blocks.</summary>
        public const string ProductionTimeChange = "ProductionTimeChange";

        /// <summary>Academy classify key used by upgrade blocks.</summary>
        public const string AcademyClassify = "AcademyClassify";

        /// <summary>Unit specific sound key used by upgrade blocks.</summary>
        public const string UnitSpecificSound = "UnitSpecificSound";

        /// <summary>Radar priority key used by object blocks.</summary>
        public const string RadarPriority = "RadarPriority";

        /// <summary>Sub-objects shown by a draw state.</summary>
        public const string ShowSubObjects = "ShowSubObjects";

        /// <summary>Sub-objects hidden by a draw state.</summary>
        public const string HideSubObjects = "HideSubObjects";

        /// <summary>Model asset key used by draw states.</summary>
        public const string Model = "Model";

        /// <summary>Starting building key used by player templates.</summary>
        public const string StartingBuilding = "StartingBuilding";

        /// <summary>Commonly referenced field keys.</summary>
        public static readonly IReadOnlyList<string> All =
        [
            DisplayName,
            ButtonImage,
            SelectPortrait,
            KindOf,
            BuildCost,
            BuildTime,
            DamageType,
            PrimaryDamage,
            PrimaryDamageRadius,
            DeathType,
            Upgrade,
            TriggeredBy,
            Health,
            Side,
            MaxHealth,
            Model,
            Speed,
            Armor,
            Command,
            Object,
            AttackRange,
            Weapon,
            VisionRange,
            ShroudClearingRange,
            ExperienceRequired,
            IsTrainable,
            CrushableLevel,
            MaxSimultaneousOfType,
            VoiceSelect,
            VoiceMove,
            VoiceAttack,
            VoiceFear,
            VoiceGuard,
            SoundStealthOn,
            ProductionTimeChange,
            AcademyClassify,
            UnitSpecificSound,
            RadarPriority,
            ShowSubObjects,
            HideSubObjects
        ];
    }

    /// <summary>
    /// Common KindOf flags used across Generals and Zero Hour.
    /// </summary>
    public static class KindOfFlags
    {
        /// <summary>
        /// Representative KindOf flags for auto-complete and multi-selection.
        /// </summary>
        public static readonly string[] All =
        [
            "STRUCTURE", "SELECTABLE", "IMMOBILE", "CAN_ATTACK", "CAN_CAST_REFLECTIONS",
            "VEHICLE", "INFANTRY", "AIRCRAFT", "DRONE", "SCORE", "PRELOAD",
            "FS_POWER", "FS_FACTORY", "FS_BASE_DEFENSE", "FS_TECHNOLOGY",
            "AUTO_RALLYPOINT", "CAPTURABLE", "TRANSPORT", "CLEARED_BY_BUILD",
            "BALLISTIC_MISSILE", "DEFENSIVE_WALL", "REBUILD_HOLE", "HEAL_PAD",
            "STEALTH_GARRISON", "SUPPLY_SOURCE", "CASH_GENERATOR", "DOZER", "HARVESTER",
            "PRODUCED_AT_HELIPAD", "ATTACK_NEEDS_LINE_OF_SIGHT", "NO_COLLIDE",
            "GARRISONABLE_UNTIL_DESTROYED", "SALVAGER", "POWERED", "PARACHUTABLE",
        ];
    }

    /// <summary>
    /// Known side and faction identifiers for Command &amp; Conquer Generals and Zero Hour.
    /// </summary>
    public static class Sides
    {
        /// <summary>America faction.</summary>
        public const string America = "America";

        /// <summary>USA alias.</summary>
        public const string USA = "USA";

        /// <summary>China faction.</summary>
        public const string China = "China";

        /// <summary>GLA faction.</summary>
        public const string GLA = "GLA";

        /// <summary>Civilian faction.</summary>
        public const string Civilian = "Civilian";

        /// <summary>Boss faction.</summary>
        public const string Boss = "Boss";

        /// <summary>
        /// Representative list of sides and Zero Hour general factions.
        /// </summary>
        public static readonly IReadOnlyList<string> All =
        [
            America,
            USA,
            China,
            GLA,
            Civilian,
            Boss,
            "AmericaAirForceGeneral",
            "AmericaLaserGeneral",
            "AmericaSuperWeaponGeneral",
            "ChinaTankGeneral",
            "ChinaInfantryGeneral",
            "ChinaNukeGeneral",
            "GLAToxinGeneral",
            "GLADemolitionGeneral",
            "GLAStealthGeneral",
        ];
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

        /// <summary>Debounce delay before resolving the 3D model preview, in milliseconds.</summary>
        public const int ModelPreviewDebounceMs = 350;

        /// <summary>Default 3D preview animation frame rate, in frames per second.</summary>
        public const int DefaultPreviewFrameRate = 30;

        /// <summary>Maximum 3D preview animation frame rate, in frames per second.</summary>
        public const int MaxPreviewFrameRate = 120;

        /// <summary>Maximum reference results shown in the reference browser.</summary>
        public const int MaxReferenceResults = 500;

        /// <summary>Maximum live validation rows shown in the issues panel.</summary>
        public const int MaxValidationRows = 50;

        /// <summary>Maximum picker textures preloaded with thumbnails.</summary>
        public const int MaxPickerThumbnails = 64;

        /// <summary>Maximum mapped image files parsed for texture pickers.</summary>
        public const int MaxMappedImageFiles = 50;

        /// <summary>Maximum mapped image definitions offered by texture pickers.</summary>
        public const int MaxPickerDefinitions = 2000;

        /// <summary>Maximum characters rendered in the raw text preview.</summary>
        public const int MaxRawPreviewChars = 100000;

        /// <summary>Maximum overview cards preloaded with portrait thumbnails.</summary>
        public const int MaxCardThumbnails = 60;

        /// <summary>Block count above which the explorer groups blocks by type.</summary>
        public const int BlockGroupThreshold = 12;

        /// <summary>Maximum related objects shown on the preview canvas for reference blocks.</summary>
        public const int MaxRelatedObjects = 12;

        /// <summary>Maximum models composed into one multi-model 3D preview scene.</summary>
        public const int MaxCompositeModels = 8;

        /// <summary>Maximum owner candidates cloned while composing a multi-model 3D preview.</summary>
        public const int MaxCompositeOwnerAttempts = 3;

        /// <summary>Maximum reverse-reference results returned per lookup.</summary>
        public const int MaxReferencers = 12;

        /// <summary>Maximum files demand-parsed per reverse-reference lookup.</summary>
        public const int MaxReverseParseFiles = 25;

        /// <summary>Maximum hop depth for cross-file model resolution walks.</summary>
        public const int MaxResolutionDepth = 4;

        /// <summary>Maximum blocks visited per cross-file model resolution walk.</summary>
        public const int MaxResolutionNodes = 48;

        /// <summary>Debounce delay before resolving cross-file preview references, in milliseconds.</summary>
        public const int CrossFileResolutionDebounceMs = 150;

        /// <summary>Maximum layered file systems cached by the W3D model resolver.</summary>
        public const int MaxCachedFileSystems = 4;
    }
}
