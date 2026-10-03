// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System.Collections.Frozen;

namespace GenHub.Core.Constants;

/// <summary>
/// Grammar constants for the typed SAGE INI parser, ported from the engine INI loader
/// (Core/GameEngine/Source/Common/INI/INI.cpp): line grammar, block tables, Object-family
/// inheritance tokens, module openers, and the reskin-legal field set.
/// </summary>
public static class SageIniConstants
{
    /// <summary>
    /// Line grammar constants matching INI::readLine and the tokenizer separators.
    /// </summary>
    public static class Grammar
    {
        /// <summary>Starts a comment; the remainder of the line is ignored.</summary>
        public const char Comment = ';';

        /// <summary>Closes a block or sub-block; matched case-insensitively like the engine.</summary>
        public const string EndToken = "End";

        /// <summary>Maximum characters per line (INI_MAX_CHARS_PER_LINE).</summary>
        public const int MaxCharsPerLine = 1028;

        /// <summary>Maximum nested #include depth before the chain is rejected.</summary>
        public const int MaxIncludeDepth = 16;

        /// <summary>Maximum diagnostics retained per parsed file; further diagnostics are counted and dropped.</summary>
        public const int MaxDiagnosticsPerFile = 1000;

        /// <summary>Token separators from INI::getSeps (" \n\r\t="). Tab is included because the engine tokenizes on it even though readLine asserts against it.</summary>
        public static readonly char[] TokenSeparators = [' ', '\t', '\r', '\n', '='];
    }

    /// <summary>
    /// Preprocessor directive keywords. The engine has no preprocessor; these are a GenHub
    /// extension so shared fragments and symbolic values can be reused across INI sources.
    /// </summary>
    public static class Directives
    {
        /// <summary>Defines a whole-token macro: #define NAME value....</summary>
        public const string Define = "#define";

        /// <summary>Includes another file inline: #include "path" or #include path.</summary>
        public const string Include = "#include";

        /// <summary>Directive introducer; any line whose first token starts with this is a directive line.</summary>
        public const char Introducer = '#';
    }

    /// <summary>
    /// Object-family block tokens with inheritance semantics.
    /// </summary>
    public static class Inheritance
    {
        /// <summary>Base template block: Object &lt;Name&gt;.</summary>
        public const string Object = "Object";

        /// <summary>Visual-only clone: ObjectReskin &lt;Name&gt; &lt;Parent&gt;.</summary>
        public const string ObjectReskin = "ObjectReskin";

        /// <summary>Full-table patch of an existing template: ObjectExtend &lt;Name&gt; &lt;Parent&gt;.</summary>
        public const string ObjectExtend = "ObjectExtend";

        /// <summary>Editor-level alias with extend semantics: ChildObject &lt;Name&gt; &lt;Parent&gt;. The engine has no such block; the Qt map.ini editor treats it as Object kind.</summary>
        public const string ChildObject = "ChildObject";
    }

    /// <summary>
    /// Fields of Object-family blocks that open an End-terminated nested module scope
    /// (ThingTemplate::parseModuleName entries: Behavior, Body, Draw, ClientUpdate).
    /// </summary>
    public static class ModuleOpeners
    {
        /// <summary>Behavior module opener.</summary>
        public const string Behavior = "Behavior";

        /// <summary>Body module opener (parsed as behavior type 999, body interface required).</summary>
        public const string Body = "Body";

        /// <summary>Draw module opener; the only module allowed in the reskin table.</summary>
        public const string Draw = "Draw";

        /// <summary>Client update module opener.</summary>
        public const string ClientUpdate = "ClientUpdate";

        /// <summary>All module opener field names.</summary>
        public static readonly FrozenSet<string> All = FrozenSet.ToFrozenSet<string>(
            [
                Behavior,
                Body,
                Draw,
                ClientUpdate,
            ],
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Fields legal inside ObjectReskin blocks (ThingTemplate::s_objectReskinFieldParseTable):
    /// draw modules, geometry, fence offsets, and simultaneous-build caps only.
    /// </summary>
    public static class ReskinFields
    {
        /// <summary>Geometry type field.</summary>
        public const string Geometry = "Geometry";

        /// <summary>Geometry major radius field.</summary>
        public const string GeometryMajorRadius = "GeometryMajorRadius";

        /// <summary>Geometry minor radius field.</summary>
        public const string GeometryMinorRadius = "GeometryMinorRadius";

        /// <summary>Geometry height field.</summary>
        public const string GeometryHeight = "GeometryHeight";

        /// <summary>Small-geometry flag field.</summary>
        public const string GeometryIsSmall = "GeometryIsSmall";

        /// <summary>Fence width field.</summary>
        public const string FenceWidth = "FenceWidth";

        /// <summary>Fence horizontal offset field.</summary>
        public const string FenceXOffset = "FenceXOffset";

        /// <summary>Simultaneous-build cap field.</summary>
        public const string MaxSimultaneousOfType = "MaxSimultaneousOfType";

        /// <summary>Simultaneous-build link key field.</summary>
        public const string MaxSimultaneousLinkKey = "MaxSimultaneousLinkKey";

        /// <summary>All reskin-legal field names, including the Draw module opener.</summary>
        public static readonly FrozenSet<string> All = FrozenSet.ToFrozenSet<string>(
            [
                ModuleOpeners.Draw,
                Geometry,
                GeometryMajorRadius,
                GeometryMinorRadius,
                GeometryHeight,
                GeometryIsSmall,
                FenceWidth,
                FenceXOffset,
                MaxSimultaneousOfType,
                MaxSimultaneousLinkKey,
            ],
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Bare End-terminated scopes nested directly inside Object-family blocks. Unlike
    /// modules these openers carry no type or tag (ArmorSet, WeaponSet, and Prerequisites
    /// consume lines until End via their own parse functions in ThingTemplate.cpp).
    /// </summary>
    public static class ObjectNestedScopes
    {
        /// <summary>Armor set scope opener.</summary>
        public const string ArmorSet = "ArmorSet";

        /// <summary>Weapon set scope opener.</summary>
        public const string WeaponSet = "WeaponSet";

        /// <summary>Prerequisites scope opener.</summary>
        public const string Prerequisites = "Prerequisites";

        /// <summary>All bare scope opener field names.</summary>
        public static readonly FrozenSet<string> All = FrozenSet.ToFrozenSet<string>(
            [
                ArmorSet,
                WeaponSet,
                Prerequisites,
            ],
            StringComparer.Ordinal);
    }

    /// <summary>
    /// End-terminated scopes nested inside Draw modules. The Generals/Zero Hour W3DModelDraw
    /// parse table carries DefaultConditionState, ConditionState, and TransitionState as
    /// End-terminated scopes; AliasConditionState is a one-line directive with no End and
    /// must never be depth-counted. ModelConditionState, AnimationState, and
    /// IdleAnimationState are later-SAGE (BFME-era) tokens with no Generals/Zero Hour
    /// parse-table entry, kept as deliberate tolerance so mod files using them keep
    /// parsing; Animation and IdleAnimation are plain fields inside a state, not scopes.
    /// </summary>
    public static class ModuleNestedScopes
    {
        /// <summary>Default condition state scope opener.</summary>
        public const string DefaultConditionState = "DefaultConditionState";

        /// <summary>Condition state scope opener.</summary>
        public const string ConditionState = "ConditionState";

        /// <summary>Alias condition state one-line directive (ZH token, never End-terminated).</summary>
        public const string AliasConditionState = "AliasConditionState";

        /// <summary>Transition state scope opener.</summary>
        public const string TransitionState = "TransitionState";

        /// <summary>Later-SAGE model condition state scope opener, tolerated for mod files.</summary>
        public const string ModelConditionState = "ModelConditionState";

        /// <summary>Later-SAGE animation state scope opener, tolerated for mod files.</summary>
        public const string AnimationState = "AnimationState";

        /// <summary>Later-SAGE idle animation state scope opener, tolerated for mod files.</summary>
        public const string IdleAnimationState = "IdleAnimationState";

        /// <summary>All module-nested scope opener field names.</summary>
        public static readonly FrozenSet<string> All = FrozenSet.ToFrozenSet<string>(
            [
                DefaultConditionState,
                ConditionState,
                TransitionState,
                ModelConditionState,
                AnimationState,
                IdleAnimationState,
            ],
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Bare End-terminated nuggets nested directly inside FXList blocks, matching the eight
    /// Generals/Zero Hour FXList parse-table entries.
    /// </summary>
    public static class FxListScopes
    {
        /// <summary>Sound nugget scope opener.</summary>
        public const string Sound = "Sound";

        /// <summary>Ray-effect nugget scope opener.</summary>
        public const string RayEffect = "RayEffect";

        /// <summary>Tracer nugget scope opener.</summary>
        public const string Tracer = "Tracer";

        /// <summary>Light-pulse nugget scope opener.</summary>
        public const string LightPulse = "LightPulse";

        /// <summary>View-shake nugget scope opener.</summary>
        public const string ViewShake = "ViewShake";

        /// <summary>Terrain-scorch nugget scope opener.</summary>
        public const string TerrainScorch = "TerrainScorch";

        /// <summary>Particle system nugget scope opener.</summary>
        public const string ParticleSystem = "ParticleSystem";

        /// <summary>FX-list-at-bone nugget scope opener.</summary>
        public const string FXListAtBonePos = "FXListAtBonePos";

        /// <summary>All FXList nugget scope opener field names.</summary>
        public static readonly FrozenSet<string> All = FrozenSet.ToFrozenSet<string>(
            [
                Sound,
                RayEffect,
                Tracer,
                LightPulse,
                ViewShake,
                TerrainScorch,
                ParticleSystem,
                FXListAtBonePos,
            ],
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Bare End-terminated nuggets nested directly inside ObjectCreationList blocks, matching
    /// the six Generals/Zero Hour ObjectCreationList parse-table entries.
    /// </summary>
    public static class ObjectCreationListScopes
    {
        /// <summary>Create-object nugget scope opener.</summary>
        public const string CreateObject = "CreateObject";

        /// <summary>Create-debris nugget scope opener.</summary>
        public const string CreateDebris = "CreateDebris";

        /// <summary>Random-force nugget scope opener.</summary>
        public const string ApplyRandomForce = "ApplyRandomForce";

        /// <summary>Deliver-payload nugget scope opener.</summary>
        public const string DeliverPayload = "DeliverPayload";

        /// <summary>Fire-weapon nugget scope opener.</summary>
        public const string FireWeapon = "FireWeapon";

        /// <summary>Attack nugget scope opener.</summary>
        public const string Attack = "Attack";

        /// <summary>All ObjectCreationList nugget scope opener field names.</summary>
        public static readonly FrozenSet<string> All = FrozenSet.ToFrozenSet<string>(
            [
                CreateObject,
                CreateDebris,
                ApplyRandomForce,
                DeliverPayload,
                FireWeapon,
                Attack,
            ],
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Block tokens with dedicated nested-scope tables in the SAGE INI parser.
    /// </summary>
    public static class BlockTokens
    {
        /// <summary>Effect list block with the eight engine nugget scopes.</summary>
        public const string FXList = "FXList";

        /// <summary>Spawn list block with the six engine nugget scopes.</summary>
        public const string ObjectCreationList = "ObjectCreationList";
    }

    /// <summary>
    /// Block dispatch tables. Block tokens are matched case-sensitively like the engine
    /// (findBlockParse uses strcmp), while End is matched case-insensitively (stricmp).
    /// </summary>
    public static class BlockTables
    {
        /// <summary>Full engine block table (theTypeTable) plus the ChildObject editor alias, used for subsystem loads.</summary>
        public static readonly FrozenSet<string> Full = FrozenSet.ToFrozenSet<string>(
            [
                "AIData",
                "Animation",
                "Armor",
                "ArmorExtend",
                "AudioEvent",
                "AudioSettings",
                "BenchProfile",
                "Bridge",
                "BuffTemplate",
                "Campaign",
                "ChallengeGenerals",
                "ChatCommand",
                Inheritance.ChildObject,
                "CommandButton",
                "CommandMap",
                "CommandSet",
                "ControlBarResizer",
                "ControlBarScheme",
                "CrateData",
                "Credits",
                "DamageFX",
                "DialogEvent",
                "DrawGroupInfo",
                "DynamicGameLOD",
                "EvaEvent",
                BlockTokens.FXList,
                "GameData",
                "HeaderTemplate",
                "InGameUI",
                "LODPreset",
                "Language",
                "Locomotor",
                "LocomotorExtend",
                "MapCache",
                "MapData",
                "MappedImage",
                "MiscAudio",
                "Mouse",
                "MouseCursor",
                "MultiplayerColor",
                "MultiplayerSettings",
                "MultiplayerStartingMoneyChoice",
                "MusicTrack",
                Inheritance.Object,
                BlockTokens.ObjectCreationList,
                Inheritance.ObjectExtend,
                Inheritance.ObjectReskin,
                "OnlineChatColors",
                "ParticleSystem",
                "PlayerTemplate",
                "Rank",
                "ReallyLowMHz",
                "Road",
                "Science",
                "ScriptAction",
                "ScriptCondition",
                "ShellMenuScheme",
                "SpecialPower",
                "StaticGameLOD",
                "Terrain",
                "Upgrade",
                "Video",
                "WaterSet",
                "WaterTransparency",
                "Weapon",
                "WeaponExtend",
                "Weather",
                "WebpageURL",
                "WindowTransition",
            ],
            StringComparer.Ordinal);

        /// <summary>Reduced WorldBuilder map.ini block table (theWbTypeTable), used for loadWB. WaterSet is deliberately absent, matching the engine.</summary>
        public static readonly FrozenSet<string> WorldBuilder = FrozenSet.ToFrozenSet<string>(
            [
                Inheritance.Object,
                Inheritance.ObjectReskin,
                "ObjectCreationList",
                "SpecialPower",
                "ParticleSystem",
                "Science",
                "Armor",
                "Weapon",
                "FXList",
                "DamageFX",
                "AudioEvent",
                "WaterTransparency",
            ],
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Subsystem short names used to derive Default\ and override INI directories for the boot table.
    /// </summary>
    public static class Subsystems
    {
        /// <summary>Global game data subsystem.</summary>
        public const string GameData = "GameData";

        /// <summary>Water subsystem.</summary>
        public const string Water = "Water";

        /// <summary>Science subsystem.</summary>
        public const string Science = "Science";

        /// <summary>Multiplayer settings subsystem.</summary>
        public const string Multiplayer = "Multiplayer";

        /// <summary>Terrain material subsystem.</summary>
        public const string Terrain = "Terrain";

        /// <summary>Road and bridge subsystem.</summary>
        public const string Roads = "Roads";

        /// <summary>Script template directory (single directory, no Default pair).</summary>
        public const string Scripts = "Scripts";

        /// <summary>Audio event subsystem.</summary>
        public const string AudioEvents = "AudioEvents";

        /// <summary>Veterancy rank subsystem (override directory only).</summary>
        public const string Rank = "Rank";

        /// <summary>Player template subsystem.</summary>
        public const string PlayerTemplate = "PlayerTemplate";

        /// <summary>Special power subsystem.</summary>
        public const string SpecialPower = "SpecialPower";

        /// <summary>Effect list subsystem.</summary>
        public const string FXList = "FXList";

        /// <summary>Weapon subsystem (override directory only).</summary>
        public const string Weapon = "Weapon";

        /// <summary>Object creation list subsystem.</summary>
        public const string ObjectCreationList = "ObjectCreationList";

        /// <summary>Locomotor subsystem (override directory only).</summary>
        public const string Locomotor = "Locomotor";

        /// <summary>Damage effect subsystem (override directory only).</summary>
        public const string DamageFX = "DamageFX";

        /// <summary>Armor subsystem (override directory only).</summary>
        public const string Armor = "Armor";

        /// <summary>Object template subsystem.</summary>
        public const string Object = "Object";

        /// <summary>Supply crate subsystem.</summary>
        public const string Crate = "Crate";

        /// <summary>Upgrade subsystem.</summary>
        public const string Upgrade = "Upgrade";
    }
}
