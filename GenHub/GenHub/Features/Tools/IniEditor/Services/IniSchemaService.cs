using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Models.Tools.IniEditor;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace GenHub.Features.Tools.IniEditor.Services;

/// <summary>
/// Schema metadata for known Generals and Zero Hour INI block types,
/// grounded in the public generalsgamecode INI parser and block tables.
/// Unknown blocks and fields remain editable generically.
/// </summary>
public sealed class IniSchemaService : IIniSchemaService
{
    private sealed record SchemaFieldEntry(
        string Key,
        string DescriptionKey,
        bool IsNumeric,
        IReadOnlyList<string>? Options = null,
        string? ReferenceBlockType = null,
        bool IsTexture = false);

    private sealed record SchemaBlockEntry(
        string BlockType,
        string DescriptionKey,
        SchemaFieldEntry[] Fields);

    private sealed record SchemaSnapshot(
        IReadOnlyList<IniBlockSchema> Blocks,
        Dictionary<string, IniBlockSchema> Lookup);

    private static readonly SchemaBlockEntry[] SchemaTables =
    [
            new(
                IniConstants.BlockTypes.Object,
                "Tools.IniEditor.Schema.Blocks.Object",
                [
                    new(IniConstants.FieldKeys.DisplayName, "Tools.IniEditor.Schema.Fields.Object.DisplayName", false),
                    new("Side", "Tools.IniEditor.Schema.Fields.Object.Side", false),
                    new("EditorSorting", "Tools.IniEditor.Schema.Fields.Object.EditorSorting", false),
                    new(IniConstants.FieldKeys.BuildCost, "Tools.IniEditor.Schema.Fields.Object.BuildCost", true),
                    new(IniConstants.FieldKeys.BuildTime, "Tools.IniEditor.Schema.Fields.Object.BuildTime", true),
                    new("Health", "Tools.IniEditor.Schema.Fields.Object.Health", true),
                    new("MaxHealth", "Tools.IniEditor.Schema.Fields.Object.MaxHealth", true),
                    new("ArmorSet", "Tools.IniEditor.Schema.Fields.Object.ArmorSet", false, ReferenceBlockType: IniConstants.BlockTypes.ArmorSet),
                    new("WeaponSet", "Tools.IniEditor.Schema.Fields.Object.WeaponSet", false, ReferenceBlockType: IniConstants.BlockTypes.WeaponSet),
                    new("CommandSet", "Tools.IniEditor.Schema.Fields.Object.CommandSet", false, ReferenceBlockType: IniConstants.BlockTypes.CommandSet),
                    new("Upgrades", "Tools.IniEditor.Schema.Fields.Object.Upgrades", false),
                    new("Prerequisites", "Tools.IniEditor.Schema.Fields.Object.Prerequisites", false),
                    new("Science", "Tools.IniEditor.Schema.Fields.Object.Science", false, ReferenceBlockType: IniConstants.BlockTypes.Science),
                    new("TransportSlotCount", "Tools.IniEditor.Schema.Fields.Object.TransportSlotCount", true),
                    new("VisionRange", "Tools.IniEditor.Schema.Fields.Object.VisionRange", true),
                    new("ShroudClearingRange", "Tools.IniEditor.Schema.Fields.Object.ShroudClearingRange", true),
                    new(IniConstants.FieldKeys.MaxSimultaneousOfType, "Tools.IniEditor.Schema.Fields.Object.MaxSimultaneousOfType", true),
                    new(IniConstants.FieldKeys.ExperienceRequired, "Tools.IniEditor.Schema.Fields.Object.ExperienceRequired", false),
                    new(IniConstants.FieldKeys.IsTrainable, "Tools.IniEditor.Schema.Fields.Object.IsTrainable", false, Options: IniConstants.BooleanOptions.All),
                    new("Scale", "Tools.IniEditor.Schema.Fields.Object.Scale", true),
                    new("Speed", "Tools.IniEditor.Schema.Fields.Object.Speed", true),
                    new("TurnRate", "Tools.IniEditor.Schema.Fields.Object.TurnRate", true),
                    new("CrusherLevel", "Tools.IniEditor.Schema.Fields.Object.CrusherLevel", true),
                    new(IniConstants.FieldKeys.CrushableLevel, "Tools.IniEditor.Schema.Fields.Object.CrushableLevel", true),
                    new("KindOf", "Tools.IniEditor.Schema.Fields.Object.KindOf", false),
                    new("Body", "Tools.IniEditor.Schema.Fields.Object.Body", false),
                    new("Behavior", "Tools.IniEditor.Schema.Fields.Object.Behavior", false),
                    new("Draw", "Tools.IniEditor.Schema.Fields.Object.Draw", false),
                    new("ClientUpdate", "Tools.IniEditor.Schema.Fields.Object.ClientUpdate", false),
                    new("Geometry", "Tools.IniEditor.Schema.Fields.Object.Geometry", false),
                    new("GeometryMajorRadius", "Tools.IniEditor.Schema.Fields.Object.GeometryMajorRadius", true),
                    new("GeometryMinorRadius", "Tools.IniEditor.Schema.Fields.Object.GeometryMinorRadius", true),
                    new("GeometryHeight", "Tools.IniEditor.Schema.Fields.Object.GeometryHeight", true),
                    new("GeometryIsSmall", "Tools.IniEditor.Schema.Fields.Object.GeometryIsSmall", false, Options: IniConstants.BooleanOptions.All),
                    new(IniConstants.FieldKeys.RadarPriority, "Tools.IniEditor.Schema.Fields.Object.RadarPriority", false),
                    new(IniConstants.FieldKeys.SelectPortrait, "Tools.IniEditor.Schema.Fields.Object.SelectPortrait", false, IsTexture: true),
                    new(IniConstants.FieldKeys.VoiceSelect, "Tools.IniEditor.Schema.Fields.Object.VoiceSelect", false, ReferenceBlockType: IniConstants.SubBlockTypes.AudioEvent),
                    new(IniConstants.FieldKeys.VoiceMove, "Tools.IniEditor.Schema.Fields.Object.VoiceMove", false, ReferenceBlockType: IniConstants.SubBlockTypes.AudioEvent),
                    new(IniConstants.FieldKeys.VoiceAttack, "Tools.IniEditor.Schema.Fields.Object.VoiceAttack", false, ReferenceBlockType: IniConstants.SubBlockTypes.AudioEvent),
                    new(IniConstants.FieldKeys.VoiceFear, "Tools.IniEditor.Schema.Fields.Object.VoiceFear", false, ReferenceBlockType: IniConstants.SubBlockTypes.AudioEvent),
                    new(IniConstants.FieldKeys.VoiceGuard, "Tools.IniEditor.Schema.Fields.Object.VoiceGuard", false, ReferenceBlockType: IniConstants.SubBlockTypes.AudioEvent),
                    new(IniConstants.FieldKeys.SoundStealthOn, "Tools.IniEditor.Schema.Fields.Object.SoundStealthOn", false, ReferenceBlockType: IniConstants.SubBlockTypes.AudioEvent),
                    new(IniConstants.SubBlockTypes.UnitSpecificSounds, "Tools.IniEditor.Schema.Fields.Object.UnitSpecificSounds", false, ReferenceBlockType: IniConstants.SubBlockTypes.UnitSpecificSounds),
                    new("Armor", "Tools.IniEditor.Schema.Fields.Object.Armor", false, ReferenceBlockType: IniConstants.BlockTypes.Armor),
                    new("Icon", "Tools.IniEditor.Schema.Fields.Object.Icon", false, IsTexture: true),
                    new(IniConstants.FieldKeys.ButtonImage, "Tools.IniEditor.Schema.Fields.Object.ButtonImage", false, IsTexture: true),
                    new("UpgradeCameo1", "Tools.IniEditor.Schema.Fields.Object.UpgradeCameo1", false, IsTexture: true),
                    new("UpgradeCameo2", "Tools.IniEditor.Schema.Fields.Object.UpgradeCameo2", false, IsTexture: true),
                    new("UpgradeCameo3", "Tools.IniEditor.Schema.Fields.Object.UpgradeCameo3", false, IsTexture: true),
                    new("UpgradeCameo4", "Tools.IniEditor.Schema.Fields.Object.UpgradeCameo4", false, IsTexture: true),
                    new("UpgradeCameo5", "Tools.IniEditor.Schema.Fields.Object.UpgradeCameo5", false, IsTexture: true),
                ]),
            new(
                IniConstants.BlockTypes.Weapon,
                "Tools.IniEditor.Schema.Blocks.Weapon",
                [
                    new(IniConstants.FieldKeys.PrimaryDamage, "Tools.IniEditor.Schema.Fields.Weapon.PrimaryDamage", true),
                    new(IniConstants.FieldKeys.PrimaryDamageRadius, "Tools.IniEditor.Schema.Fields.Weapon.PrimaryDamageRadius", true),
                    new("SecondaryDamage", "Tools.IniEditor.Schema.Fields.Weapon.SecondaryDamage", true),
                    new("SecondaryDamageRadius", "Tools.IniEditor.Schema.Fields.Weapon.SecondaryDamageRadius", true),
                    new("AttackRange", "Tools.IniEditor.Schema.Fields.Weapon.AttackRange", true),
                    new("MinimumAttackRange", "Tools.IniEditor.Schema.Fields.Weapon.MinimumAttackRange", true),
                    new("DelayBetweenShots", "Tools.IniEditor.Schema.Fields.Weapon.DelayBetweenShots", true),
                    new("ClipSize", "Tools.IniEditor.Schema.Fields.Weapon.ClipSize", true),
                    new("ClipReloadTime", "Tools.IniEditor.Schema.Fields.Weapon.ClipReloadTime", true),
                    new("AutoReloadsClip", "Tools.IniEditor.Schema.Fields.Weapon.AutoReloadsClip", false),
                    new(IniConstants.FieldKeys.DamageType, "Tools.IniEditor.Schema.Fields.Weapon.DamageType", false, Options: IniConstants.DamageTypes.All),
                    new(IniConstants.FieldKeys.DeathType, "Tools.IniEditor.Schema.Fields.Weapon.DeathType", false, Options: IniConstants.DeathTypes.All),
                    new("WeaponSpeed", "Tools.IniEditor.Schema.Fields.Weapon.WeaponSpeed", true),
                    new("RadiusDamageAffects", "Tools.IniEditor.Schema.Fields.Weapon.RadiusDamageAffects", false),
                    new("DamageScalar", "Tools.IniEditor.Schema.Fields.Weapon.DamageScalar", true),
                    new("AntiAirborneVehicle", "Tools.IniEditor.Schema.Fields.Weapon.AntiAirborneVehicle", false),
                    new("AntiAirborneInfantry", "Tools.IniEditor.Schema.Fields.Weapon.AntiAirborneInfantry", false),
                    new("AntiGround", "Tools.IniEditor.Schema.Fields.Weapon.AntiGround", false),
                    new("ProjectileObject", "Tools.IniEditor.Schema.Fields.Weapon.ProjectileObject", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new("FireFX", "Tools.IniEditor.Schema.Fields.Weapon.FireFX", false),
                    new("HitGroundFX", "Tools.IniEditor.Schema.Fields.Weapon.HitGroundFX", false),
                    new("HitObjectFX", "Tools.IniEditor.Schema.Fields.Weapon.HitObjectFX", false),
                    new("LaserName", "Tools.IniEditor.Schema.Fields.Weapon.LaserName", false),
                    new("WeaponBonus", "Tools.IniEditor.Schema.Fields.Weapon.WeaponBonus", false),
                    new("HistoricBonusTime", "Tools.IniEditor.Schema.Fields.Weapon.HistoricBonusTime", true),
                    new("HistoricBonusCount", "Tools.IniEditor.Schema.Fields.Weapon.HistoricBonusCount", true),
                    new("HistoricBonusMultiplier", "Tools.IniEditor.Schema.Fields.Weapon.HistoricBonusMultiplier", true),
                    new("ScatterRadius", "Tools.IniEditor.Schema.Fields.Weapon.ScatterRadius", true),
                    new("AcceptableAimDelta", "Tools.IniEditor.Schema.Fields.Weapon.AcceptableAimDelta", true),
                ]),
            new(
                IniConstants.BlockTypes.Armor,
                "Tools.IniEditor.Schema.Blocks.Armor",
                [
                    new("Armor", "Tools.IniEditor.Schema.Fields.Armor.Armor", false, Options: IniConstants.DamageTypes.All),
                ]),
            new(
                IniConstants.BlockTypes.ArmorSet,
                "Tools.IniEditor.Schema.Blocks.ArmorSet",
                [
                    new("Conditions", "Tools.IniEditor.Schema.Fields.ArmorSet.Conditions", false),
                    new("Armor", "Tools.IniEditor.Schema.Fields.ArmorSet.Armor", false, ReferenceBlockType: IniConstants.BlockTypes.Armor),
                    new("DamageFX", "Tools.IniEditor.Schema.Fields.ArmorSet.DamageFX", false, ReferenceBlockType: IniConstants.BlockTypes.DamageFX),
                ]),
            new(
                IniConstants.BlockTypes.WeaponSet,
                "Tools.IniEditor.Schema.Blocks.WeaponSet",
                [
                    new("Conditions", "Tools.IniEditor.Schema.Fields.WeaponSet.Conditions", false),
                    new("Weapon", "Tools.IniEditor.Schema.Fields.WeaponSet.Weapon", false, ReferenceBlockType: IniConstants.BlockTypes.Weapon),
                    new("AutoChooseSources", "Tools.IniEditor.Schema.Fields.WeaponSet.AutoChooseSources", false),
                    new("PreferredAgainst", "Tools.IniEditor.Schema.Fields.WeaponSet.PreferredAgainst", false),
                    new("ShareWeaponReloadTime", "Tools.IniEditor.Schema.Fields.WeaponSet.ShareWeaponReloadTime", false),
                    new("WeaponLockSharedAcrossSets", "Tools.IniEditor.Schema.Fields.WeaponSet.WeaponLockSharedAcrossSets", false),
                ]),
            new(
                IniConstants.BlockTypes.CommandButton,
                "Tools.IniEditor.Schema.Blocks.CommandButton",
                [
                    new("Command", "Tools.IniEditor.Schema.Fields.CommandButton.Command", false, Options: IniConstants.CommandButtonCommands.All),
                    new("Object", "Tools.IniEditor.Schema.Fields.CommandButton.Object", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new(IniConstants.FieldKeys.Upgrade, "Tools.IniEditor.Schema.Fields.CommandButton.Upgrade", false, ReferenceBlockType: IniConstants.BlockTypes.Upgrade),
                    new("Science", "Tools.IniEditor.Schema.Fields.CommandButton.Science", false, ReferenceBlockType: IniConstants.BlockTypes.Science),
                    new("SpecialPower", "Tools.IniEditor.Schema.Fields.CommandButton.SpecialPower", false, ReferenceBlockType: IniConstants.BlockTypes.SpecialPower),
                    new("TextLabel", "Tools.IniEditor.Schema.Fields.CommandButton.TextLabel", false),
                    new(IniConstants.FieldKeys.ButtonImage, "Tools.IniEditor.Schema.Fields.CommandButton.ButtonImage", false, IsTexture: true),
                    new("ButtonBorderType", "Tools.IniEditor.Schema.Fields.CommandButton.ButtonBorderType", false, Options: IniConstants.CommandButtonBorderTypes.All),
                    new("DescriptLabel", "Tools.IniEditor.Schema.Fields.CommandButton.DescriptLabel", false),
                    new("Radial", "Tools.IniEditor.Schema.Fields.CommandButton.Radial", false, Options: IniConstants.BooleanOptions.All),
                    new("InPalantir", "Tools.IniEditor.Schema.Fields.CommandButton.InPalantir", false, Options: IniConstants.BooleanOptions.All),
                    new("NeedUpgrade", "Tools.IniEditor.Schema.Fields.CommandButton.NeedUpgrade", false, ReferenceBlockType: IniConstants.BlockTypes.Upgrade),
                    new("NeedScience", "Tools.IniEditor.Schema.Fields.CommandButton.NeedScience", false, ReferenceBlockType: IniConstants.BlockTypes.Science),
                    new("Options", "Tools.IniEditor.Schema.Fields.CommandButton.Options", false, Options: IniConstants.CommandButtonOptions.All),
                    new("CursorName", "Tools.IniEditor.Schema.Fields.CommandButton.CursorName", false),
                    new("InvalidCursorName", "Tools.IniEditor.Schema.Fields.CommandButton.InvalidCursorName", false),
                    new("LacksPrerequisiteLabel", "Tools.IniEditor.Schema.Fields.CommandButton.LacksPrerequisiteLabel", false),
                ]),
            new(
                IniConstants.BlockTypes.CommandSet,
                "Tools.IniEditor.Schema.Blocks.CommandSet",
                [
                    new("1", "Tools.IniEditor.Schema.Fields.CommandSet.1", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("2", "Tools.IniEditor.Schema.Fields.CommandSet.2", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("3", "Tools.IniEditor.Schema.Fields.CommandSet.3", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("4", "Tools.IniEditor.Schema.Fields.CommandSet.4", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("5", "Tools.IniEditor.Schema.Fields.CommandSet.5", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("6", "Tools.IniEditor.Schema.Fields.CommandSet.6", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("7", "Tools.IniEditor.Schema.Fields.CommandSet.7", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("8", "Tools.IniEditor.Schema.Fields.CommandSet.8", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("9", "Tools.IniEditor.Schema.Fields.CommandSet.9", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("10", "Tools.IniEditor.Schema.Fields.CommandSet.10", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("11", "Tools.IniEditor.Schema.Fields.CommandSet.11", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("12", "Tools.IniEditor.Schema.Fields.CommandSet.12", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("13", "Tools.IniEditor.Schema.Fields.CommandSet.13", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                    new("14", "Tools.IniEditor.Schema.Fields.CommandSet.14", false, ReferenceBlockType: IniConstants.BlockTypes.CommandButton),
                ]),
            new(
                IniConstants.BlockTypes.Upgrade,
                "Tools.IniEditor.Schema.Blocks.Upgrade",
                [
                    new("Type", "Tools.IniEditor.Schema.Fields.Upgrade.Type", false),
                    new(IniConstants.FieldKeys.BuildCost, "Tools.IniEditor.Schema.Fields.Upgrade.BuildCost", true),
                    new(IniConstants.FieldKeys.BuildTime, "Tools.IniEditor.Schema.Fields.Upgrade.BuildTime", true),
                    new(IniConstants.FieldKeys.DisplayName, "Tools.IniEditor.Schema.Fields.Upgrade.DisplayName", false),
                    new(IniConstants.FieldKeys.ButtonImage, "Tools.IniEditor.Schema.Fields.Upgrade.ButtonImage", false, IsTexture: true),
                    new("UpgradeCameo1", "Tools.IniEditor.Schema.Fields.Upgrade.UpgradeCameo1", false, IsTexture: true),
                    new("UpgradeCameo2", "Tools.IniEditor.Schema.Fields.Upgrade.UpgradeCameo2", false, IsTexture: true),
                    new("UpgradeCameo3", "Tools.IniEditor.Schema.Fields.Upgrade.UpgradeCameo3", false, IsTexture: true),
                    new("ResearchSound", "Tools.IniEditor.Schema.Fields.Upgrade.ResearchSound", false),
                    new("UnitSpecificSounds", "Tools.IniEditor.Schema.Fields.Upgrade.UnitSpecificSounds", false),
                    new(IniConstants.FieldKeys.UnitSpecificSound, "Tools.IniEditor.Schema.Fields.Upgrade.UnitSpecificSound", false),
                    new(IniConstants.FieldKeys.AcademyClassify, "Tools.IniEditor.Schema.Fields.Upgrade.AcademyClassify", false),
                    new(IniConstants.FieldKeys.ProductionTimeChange, "Tools.IniEditor.Schema.Fields.Upgrade.ProductionTimeChange", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new("SkirmishAIHeuristic", "Tools.IniEditor.Schema.Fields.Upgrade.SkirmishAIHeuristic", false),
                    new("PersistsInCampaign", "Tools.IniEditor.Schema.Fields.Upgrade.PersistsInCampaign", false, Options: IniConstants.BooleanOptions.All),
                ]),
            new(
                IniConstants.BlockTypes.Science,
                "Tools.IniEditor.Schema.Blocks.Science",
                [
                    new(IniConstants.FieldKeys.DisplayName, "Tools.IniEditor.Schema.Fields.Science.DisplayName", false),
                    new("Description", "Tools.IniEditor.Schema.Fields.Science.Description", false),
                    new("PrerequisiteSciences", "Tools.IniEditor.Schema.Fields.Science.PrerequisiteSciences", false, ReferenceBlockType: IniConstants.BlockTypes.Science),
                    new("SciencePurchasePointCost", "Tools.IniEditor.Schema.Fields.Science.SciencePurchasePointCost", true),
                    new("IsGrantable", "Tools.IniEditor.Schema.Fields.Science.IsGrantable", false, Options: IniConstants.BooleanOptions.All),
                    new(IniConstants.FieldKeys.ButtonImage, "Tools.IniEditor.Schema.Fields.Science.ButtonImage", false, IsTexture: true),
                ]),
            new(
                IniConstants.BlockTypes.SpecialPower,
                "Tools.IniEditor.Schema.Blocks.SpecialPower",
                [
                    new("Enum", "Tools.IniEditor.Schema.Fields.SpecialPower.Enum", false),
                    new("ReloadTime", "Tools.IniEditor.Schema.Fields.SpecialPower.ReloadTime", true),
                    new("RequiredScience", "Tools.IniEditor.Schema.Fields.SpecialPower.RequiredScience", false, ReferenceBlockType: IniConstants.BlockTypes.Science),
                    new("InitiateSound", "Tools.IniEditor.Schema.Fields.SpecialPower.InitiateSound", false),
                    new("RadiusCursorRadius", "Tools.IniEditor.Schema.Fields.SpecialPower.RadiusCursorRadius", true),
                    new("ViewObjectDuration", "Tools.IniEditor.Schema.Fields.SpecialPower.ViewObjectDuration", true),
                    new("ViewObjectRange", "Tools.IniEditor.Schema.Fields.SpecialPower.ViewObjectRange", true),
                    new(IniConstants.FieldKeys.DisplayName, "Tools.IniEditor.Schema.Fields.SpecialPower.DisplayName", false),
                    new(IniConstants.FieldKeys.ButtonImage, "Tools.IniEditor.Schema.Fields.SpecialPower.ButtonImage", false, IsTexture: true),
                    new("OCL", "Tools.IniEditor.Schema.Fields.SpecialPower.OCL", false, ReferenceBlockType: IniConstants.BlockTypes.ObjectCreationList),
                    new("ChangeWeapon", "Tools.IniEditor.Schema.Fields.SpecialPower.ChangeWeapon", false, ReferenceBlockType: IniConstants.BlockTypes.Weapon),
                    new("PublicTimer", "Tools.IniEditor.Schema.Fields.SpecialPower.PublicTimer", false),
                    new("SharedSyncedTimer", "Tools.IniEditor.Schema.Fields.SpecialPower.SharedSyncedTimer", false),
                    new("ShortcutPower", "Tools.IniEditor.Schema.Fields.SpecialPower.ShortcutPower", false),
                ]),
            new(
                IniConstants.BlockTypes.Locomotor,
                "Tools.IniEditor.Schema.Blocks.Locomotor",
                [
                    new("Speed", "Tools.IniEditor.Schema.Fields.Locomotor.Speed", true),
                    new("MinSpeed", "Tools.IniEditor.Schema.Fields.Locomotor.MinSpeed", true),
                    new("Acceleration", "Tools.IniEditor.Schema.Fields.Locomotor.Acceleration", true),
                    new("Braking", "Tools.IniEditor.Schema.Fields.Locomotor.Braking", true),
                    new("TurnRate", "Tools.IniEditor.Schema.Fields.Locomotor.TurnRate", true),
                    new("MinTurnSpeed", "Tools.IniEditor.Schema.Fields.Locomotor.MinTurnSpeed", true),
                    new("TurnRateDamaged", "Tools.IniEditor.Schema.Fields.Locomotor.TurnRateDamaged", true),
                    new("SpeedDamaged", "Tools.IniEditor.Schema.Fields.Locomotor.SpeedDamaged", true),
                    new("Lift", "Tools.IniEditor.Schema.Fields.Locomotor.Lift", true),
                    new("HoverHeight", "Tools.IniEditor.Schema.Fields.Locomotor.HoverHeight", true),
                    new("CrusherLevel", "Tools.IniEditor.Schema.Fields.Locomotor.CrusherLevel", true),
                    new("CrushableLevel", "Tools.IniEditor.Schema.Fields.Locomotor.CrushableLevel", true),
                    new("Appearance", "Tools.IniEditor.Schema.Fields.Locomotor.Appearance", false),
                    new("AccelerationPitch", "Tools.IniEditor.Schema.Fields.Locomotor.AccelerationPitch", true),
                    new("AllowAirborne", "Tools.IniEditor.Schema.Fields.Locomotor.AllowAirborne", false),
                    new("Surfaces", "Tools.IniEditor.Schema.Fields.Locomotor.Surfaces", false),
                    new("GroupMovement", "Tools.IniEditor.Schema.Fields.Locomotor.GroupMovement", false),
                    new("StickToGround", "Tools.IniEditor.Schema.Fields.Locomotor.StickToGround", false),
                ]),
            new(
                IniConstants.BlockTypes.ObjectCreationList,
                "Tools.IniEditor.Schema.Blocks.ObjectCreationList",
                [
                    new("CreateObject", "Tools.IniEditor.Schema.Fields.ObjectCreationList.CreateObject", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new("Disposition", "Tools.IniEditor.Schema.Fields.ObjectCreationList.Disposition", false),
                    new("Count", "Tools.IniEditor.Schema.Fields.ObjectCreationList.Count", true),
                    new("SpreadFormation", "Tools.IniEditor.Schema.Fields.ObjectCreationList.SpreadFormation", false),
                    new("MinDistanceA", "Tools.IniEditor.Schema.Fields.ObjectCreationList.MinDistanceA", true),
                    new("MaxDistanceA", "Tools.IniEditor.Schema.Fields.ObjectCreationList.MaxDistanceA", true),
                    new("MinDistanceB", "Tools.IniEditor.Schema.Fields.ObjectCreationList.MinDistanceB", true),
                    new("MaxDistanceB", "Tools.IniEditor.Schema.Fields.ObjectCreationList.MaxDistanceB", true),
                    new("Offset", "Tools.IniEditor.Schema.Fields.ObjectCreationList.Offset", false),
                    new("IgnorePrimaryObstacle", "Tools.IniEditor.Schema.Fields.ObjectCreationList.IgnorePrimaryObstacle", false),
                    new("IgnoreSecondaryObstacles", "Tools.IniEditor.Schema.Fields.ObjectCreationList.IgnoreSecondaryObstacles", false),
                    new("StartingWeapon", "Tools.IniEditor.Schema.Fields.ObjectCreationList.StartingWeapon", false, ReferenceBlockType: IniConstants.BlockTypes.Weapon),
                    new("InheritVeterancy", "Tools.IniEditor.Schema.Fields.ObjectCreationList.InheritVeterancy", false),
                    new("DeliverPayload", "Tools.IniEditor.Schema.Fields.ObjectCreationList.DeliverPayload", false),
                ]),
            new(
                IniConstants.BlockTypes.DamageFX,
                "Tools.IniEditor.Schema.Blocks.DamageFX",
                [
                    new("Throb", "Tools.IniEditor.Schema.Fields.DamageFX.Throb", false),
                    new("ThrobRate", "Tools.IniEditor.Schema.Fields.DamageFX.ThrobRate", true),
                    new("ThrobIntensity", "Tools.IniEditor.Schema.Fields.DamageFX.ThrobIntensity", true),
                    new("Particle", "Tools.IniEditor.Schema.Fields.DamageFX.Particle", false),
                    new("DamageParticle", "Tools.IniEditor.Schema.Fields.DamageFX.DamageParticle", false),
                    new("Fire", "Tools.IniEditor.Schema.Fields.DamageFX.Fire", false),
                    new("Smoke", "Tools.IniEditor.Schema.Fields.DamageFX.Smoke", false),
                    new("Sparks", "Tools.IniEditor.Schema.Fields.DamageFX.Sparks", false),
                    new("DamageTypes", "Tools.IniEditor.Schema.Fields.DamageFX.DamageTypes", false),
                    new("Veterancy", "Tools.IniEditor.Schema.Fields.DamageFX.Veterancy", false),
                ]),
            new(
                IniConstants.BlockTypes.PlayerTemplate,
                "Tools.IniEditor.Schema.Blocks.PlayerTemplate",
                [
                    new("Side", "Tools.IniEditor.Schema.Fields.PlayerTemplate.Side", false),
                    new("PlayableSide", "Tools.IniEditor.Schema.Fields.PlayerTemplate.PlayableSide", false),
                    new("StartMoney", "Tools.IniEditor.Schema.Fields.PlayerTemplate.StartMoney", true),
                    new("PreferredColor", "Tools.IniEditor.Schema.Fields.PlayerTemplate.PreferredColor", false),
                    new("StartingBuilding", "Tools.IniEditor.Schema.Fields.PlayerTemplate.StartingBuilding", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new("StartingUnit0", "Tools.IniEditor.Schema.Fields.PlayerTemplate.StartingUnit0", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new("StartingUnit1", "Tools.IniEditor.Schema.Fields.PlayerTemplate.StartingUnit1", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new("StartingUnit2", "Tools.IniEditor.Schema.Fields.PlayerTemplate.StartingUnit2", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new("StartingUnitTanks", "Tools.IniEditor.Schema.Fields.PlayerTemplate.StartingUnitTanks", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new("StartingUnitAir", "Tools.IniEditor.Schema.Fields.PlayerTemplate.StartingUnitAir", false, ReferenceBlockType: IniConstants.BlockTypes.Object),
                    new("SciencePurchasePoints", "Tools.IniEditor.Schema.Fields.PlayerTemplate.SciencePurchasePoints", true),
                    new(IniConstants.FieldKeys.DisplayName, "Tools.IniEditor.Schema.Fields.PlayerTemplate.DisplayName", false),
                    new("StartingTaunt", "Tools.IniEditor.Schema.Fields.PlayerTemplate.StartingTaunt", false),
                ]),
            new(
                IniConstants.BlockTypes.ExperienceLevels,
                "Tools.IniEditor.Schema.Blocks.ExperienceLevels",
                [
                    new("Level1", "Tools.IniEditor.Schema.Fields.ExperienceLevels.Level1", true),
                    new("Level2", "Tools.IniEditor.Schema.Fields.ExperienceLevels.Level2", true),
                    new("Level3", "Tools.IniEditor.Schema.Fields.ExperienceLevels.Level3", true),
                    new("Level4", "Tools.IniEditor.Schema.Fields.ExperienceLevels.Level4", true),
                    new("Level5", "Tools.IniEditor.Schema.Fields.ExperienceLevels.Level5", true),
                    new("Level6", "Tools.IniEditor.Schema.Fields.ExperienceLevels.Level6", true),
                    new("Level7", "Tools.IniEditor.Schema.Fields.ExperienceLevels.Level7", true),
                    new("Level8", "Tools.IniEditor.Schema.Fields.ExperienceLevels.Level8", true),
                    new("ExperienceScalar", "Tools.IniEditor.Schema.Fields.ExperienceLevels.ExperienceScalar", true),
                    new("InformSecondLevel", "Tools.IniEditor.Schema.Fields.ExperienceLevels.InformSecondLevel", false),
                ]),
            new(
                IniConstants.BlockTypes.MappedImage,
                "Tools.IniEditor.Schema.Blocks.MappedImage",
                [
                    new("Texture", "Tools.IniEditor.Schema.Fields.MappedImage.Texture", false),
                    new("Coords", "Tools.IniEditor.Schema.Fields.MappedImage.Coords", false),
                    new("Status", "Tools.IniEditor.Schema.Fields.MappedImage.Status", false),
                ]),
            new(
                IniConstants.BlockTypes.Veterancy,
                "Tools.IniEditor.Schema.Blocks.Veterancy",
                [
                    new("VETERAN", "Tools.IniEditor.Schema.Fields.Veterancy.VETERAN", false),
                    new("ELITE", "Tools.IniEditor.Schema.Fields.Veterancy.ELITE", false),
                    new("HERO", "Tools.IniEditor.Schema.Fields.Veterancy.HEROIC", false),
                ]),
            new(
                IniConstants.SubBlockTypes.AudioEvent,
                "Tools.IniEditor.Schema.Blocks.AudioEvent",
                [
                    new("Sounds", "Tools.IniEditor.Schema.Fields.AudioEvent.Sounds", false),
                    new("Control", "Tools.IniEditor.Schema.Fields.AudioEvent.Control", false),
                    new("Attack", "Tools.IniEditor.Schema.Fields.AudioEvent.Attack", false),
                    new("Decay", "Tools.IniEditor.Schema.Fields.AudioEvent.Decay", false),
                    new("Limit", "Tools.IniEditor.Schema.Fields.AudioEvent.Limit", true),
                    new("Priority", "Tools.IniEditor.Schema.Fields.AudioEvent.Priority", false),
                    new("Volume", "Tools.IniEditor.Schema.Fields.AudioEvent.Volume", false),
                    new("VolumeShift", "Tools.IniEditor.Schema.Fields.AudioEvent.VolumeShift", false),
                    new("PitchShift", "Tools.IniEditor.Schema.Fields.AudioEvent.PitchShift", false),
                    new("Type", "Tools.IniEditor.Schema.Fields.AudioEvent.Type", false),
                ]),
            new(
                IniConstants.SubBlockTypes.DialogEvent,
                "Tools.IniEditor.Schema.Blocks.DialogEvent",
                [
                    new("Sounds", "Tools.IniEditor.Schema.Fields.DialogEvent.Sounds", false),
                    new("Control", "Tools.IniEditor.Schema.Fields.DialogEvent.Control", false),
                    new("Priority", "Tools.IniEditor.Schema.Fields.DialogEvent.Priority", false),
                    new("Volume", "Tools.IniEditor.Schema.Fields.DialogEvent.Volume", false),
                ]),
            new(
                IniConstants.SubBlockTypes.ParticleSystem,
                "Tools.IniEditor.Schema.Blocks.ParticleSystem",
                [
                    new("Priority", "Tools.IniEditor.Schema.Fields.ParticleSystem.Priority", false),
                    new("IsOneShot", "Tools.IniEditor.Schema.Fields.ParticleSystem.IsOneShot", false, Options: IniConstants.BooleanOptions.All),
                    new("Shader", "Tools.IniEditor.Schema.Fields.ParticleSystem.Shader", false),
                    new("Type", "Tools.IniEditor.Schema.Fields.ParticleSystem.Type", false),
                    new("ParticleName", "Tools.IniEditor.Schema.Fields.ParticleSystem.ParticleName", false),
                    new("Gravity", "Tools.IniEditor.Schema.Fields.ParticleSystem.Gravity", true),
                ]),
    ];

    private readonly ILocalizationService _localizationService;
    private SchemaSnapshot _snapshot = new([], new Dictionary<string, IniBlockSchema>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Initializes a new instance of the <see cref="IniSchemaService"/> class.
    /// </summary>
    /// <param name="localizationService">The localization service resolving schema descriptions.</param>
    public IniSchemaService(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        RebuildSchemas();
        localizationService.PropertyChanged += OnLocalizationPropertyChanged;
    }

    /// <inheritdoc />
    public IReadOnlyList<IniBlockSchema> BlockSchemas => _snapshot.Blocks;

    /// <inheritdoc />
    public IniBlockSchema? GetBlockSchema(string blockType)
    {
        ArgumentNullException.ThrowIfNull(blockType);
        return _snapshot.Lookup.TryGetValue(blockType, out var schema) ? schema : null;
    }

    /// <inheritdoc />
    public bool TryGetField(string blockType, string key, out IniFieldSchema? schema)
    {
        ArgumentNullException.ThrowIfNull(blockType);
        ArgumentNullException.ThrowIfNull(key);
        schema = null;
        if (!_snapshot.Lookup.TryGetValue(blockType, out var block))
        {
            return false;
        }

        schema = block.Fields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
        return schema != null;
    }

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ILocalizationService.CurrentCulture))
        {
            RebuildSchemas();
        }
    }

    private void RebuildSchemas()
    {
        var schemas = new List<IniBlockSchema>(SchemaTables.Length);
        foreach (var table in SchemaTables)
        {
            schemas.Add(BuildBlockSchema(table));
        }

        _snapshot = new SchemaSnapshot(
            schemas,
            schemas.ToDictionary(schema => schema.BlockType, StringComparer.OrdinalIgnoreCase));
    }

    private IniBlockSchema BuildBlockSchema(SchemaBlockEntry table)
    {
        var description = _localizationService.GetString(table.DescriptionKey);
        var fields = table.Fields
            .Select(entry => new IniFieldSchema(entry.Key, _localizationService.GetString(entry.DescriptionKey), entry.IsNumeric, entry.Options, entry.ReferenceBlockType, entry.IsTexture))
            .ToArray();
        return new IniBlockSchema(table.BlockType, description, fields);
    }
}
