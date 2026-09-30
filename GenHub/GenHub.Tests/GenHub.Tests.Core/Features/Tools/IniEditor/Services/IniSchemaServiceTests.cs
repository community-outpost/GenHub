using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.Tools.IniEditor.Services;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.IniEditor.Services;

/// <summary>
/// Unit tests for <see cref="IniSchemaService"/>.
/// </summary>
public sealed class IniSchemaServiceTests
{
    /// <summary>
    /// Damage types in Zero Hour engine order, matching
    /// <c>DamageTypeFlags::s_bitNameList</c> in the public game code.
    /// </summary>
    private static readonly string[] EngineDamageTypes =
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

    private readonly IniSchemaService _service = new(CreateLocalizationService());

    /// <summary>
    /// Verifies that core block types have schema assistance.
    /// </summary>
    /// <param name="blockType">The block type under test.</param>
    [Theory]
    [InlineData(IniConstants.BlockTypes.Object)]
    [InlineData(IniConstants.BlockTypes.Weapon)]
    [InlineData(IniConstants.BlockTypes.Armor)]
    [InlineData(IniConstants.BlockTypes.ArmorSet)]
    [InlineData(IniConstants.BlockTypes.WeaponSet)]
    [InlineData(IniConstants.BlockTypes.CommandButton)]
    [InlineData(IniConstants.BlockTypes.CommandSet)]
    [InlineData(IniConstants.BlockTypes.Upgrade)]
    [InlineData(IniConstants.BlockTypes.Science)]
    [InlineData(IniConstants.BlockTypes.SpecialPower)]
    [InlineData(IniConstants.BlockTypes.Locomotor)]
    [InlineData(IniConstants.BlockTypes.ObjectCreationList)]
    [InlineData(IniConstants.BlockTypes.PlayerTemplate)]
    [InlineData(IniConstants.BlockTypes.DamageFX)]
    [InlineData(IniConstants.BlockTypes.ExperienceLevels)]
    [InlineData(IniConstants.BlockTypes.Veterancy)]
    public void GetBlockSchema_KnownBlock_ReturnsSchema(string blockType)
    {
        var schema = _service.GetBlockSchema(blockType);

        schema.Should().NotBeNull();
        schema!.Fields.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies that every declared block type resolves to a schema.
    /// </summary>
    [Fact]
    public void BlockSchemas_CoversEveryDeclaredBlockType()
    {
        foreach (var blockType in IniConstants.BlockTypes.All)
        {
            _service.GetBlockSchema(blockType).Should().NotBeNull($"block type {blockType} should have a schema");
        }

        _service.BlockSchemas.Should().HaveCount(IniConstants.BlockTypes.All.Length);
    }

    /// <summary>
    /// Verifies that block lookup is case insensitive.
    /// </summary>
    [Fact]
    public void GetBlockSchema_MixedCasing_ReturnsSchema()
    {
        _service.GetBlockSchema("oBjEcT").Should().NotBeNull();
        _service.GetBlockSchema("weaponset").Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that unknown blocks return no schema but stay editable.
    /// </summary>
    [Fact]
    public void GetBlockSchema_UnknownBlock_ReturnsNull()
    {
        _service.GetBlockSchema("SomeCommunityBlock").Should().BeNull();
    }

    /// <summary>
    /// Verifies that object health and cost fields are known.
    /// </summary>
    [Fact]
    public void TryGetField_ObjectHealthAndCost_ReturnsSchemas()
    {
        _service.TryGetField(IniConstants.BlockTypes.Object, "Health", out var health).Should().BeTrue();
        health.Should().NotBeNull();
        _service.TryGetField(IniConstants.BlockTypes.Object, "BuildCost", out var cost).Should().BeTrue();
        cost!.IsNumeric.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that weapon damage fields are known.
    /// </summary>
    [Fact]
    public void TryGetField_WeaponDamage_ReturnsSchema()
    {
        _service.TryGetField(IniConstants.BlockTypes.Weapon, "PrimaryDamage", out var damage).Should().BeTrue();
        damage!.IsNumeric.Should().BeTrue();
        _service.TryGetField(IniConstants.BlockTypes.Weapon, "DamageType", out var damageType).Should().BeTrue();
        damageType!.IsNumeric.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the damage type catalog matches the engine token list exactly.
    /// </summary>
    [Fact]
    public void DamageTypes_All_MatchesEngineTokenList()
    {
        IniConstants.DamageTypes.All.Should().Equal(EngineDamageTypes);
    }

    /// <summary>
    /// Verifies that armor blocks expose a single repeated armor key suggesting
    /// every engine damage type, matching the engine armor coefficients parser.
    /// </summary>
    [Fact]
    public void GetBlockSchema_Armor_ExposesRepeatedArmorKey()
    {
        var schema = _service.GetBlockSchema(IniConstants.BlockTypes.Armor);

        schema.Should().NotBeNull();
        schema!.Fields.Should().ContainSingle();
        schema!.Fields[0].Key.Should().Be("Armor");
        schema!.Fields[0].Options.Should().Equal(EngineDamageTypes);
    }

    /// <summary>
    /// Verifies that special powers follow the engine parse table.
    /// </summary>
    [Fact]
    public void GetBlockSchema_SpecialPower_MatchesEngineParseTable()
    {
        var schema = _service.GetBlockSchema(IniConstants.BlockTypes.SpecialPower);

        schema.Should().NotBeNull();
        var keys = schema!.Fields.Select(field => field.Key).ToList();
        keys.Should().Contain(["Enum", "RadiusCursorRadius", "RequiredScience", "InitiateSound"]);
        keys.Should().NotContain(["Cursor", "InvalidCursor", "RadiusCursor"]);
    }

    /// <summary>
    /// Verifies that command buttons use the engine cursor key names.
    /// </summary>
    [Fact]
    public void GetBlockSchema_CommandButton_UsesEngineCursorKeys()
    {
        var schema = _service.GetBlockSchema(IniConstants.BlockTypes.CommandButton);

        schema.Should().NotBeNull();
        var keys = schema!.Fields.Select(field => field.Key).ToList();
        keys.Should().Contain(["CursorName", "InvalidCursorName"]);
        keys.Should().NotContain(["Cursor", "InvalidCursor"]);
    }

    /// <summary>
    /// Verifies that command buttons suggest command verbs, border types, and options.
    /// </summary>
    [Fact]
    public void TryGetField_CommandButtonDropdowns_ExposeOptions()
    {
        _service.TryGetField(IniConstants.BlockTypes.CommandButton, "Command", out var command).Should().BeTrue();
        command!.Options.Should().Equal(IniConstants.CommandButtonCommands.All);
        _service.TryGetField(IniConstants.BlockTypes.CommandButton, "ButtonBorderType", out var border).Should().BeTrue();
        border!.Options.Should().Equal(IniConstants.CommandButtonBorderTypes.All);
        _service.TryGetField(IniConstants.BlockTypes.CommandButton, "Options", out var options).Should().BeTrue();
        options!.Options.Should().Equal(IniConstants.CommandButtonOptions.All);
    }

    /// <summary>
    /// Verifies that weapon sets follow the engine parse table instead of slot names.
    /// </summary>
    [Fact]
    public void GetBlockSchema_WeaponSet_MatchesEngineParseTable()
    {
        var schema = _service.GetBlockSchema(IniConstants.BlockTypes.WeaponSet);

        schema.Should().NotBeNull();
        var keys = schema!.Fields.Select(field => field.Key).ToList();
        keys.Should().BeEquivalentTo(
            "Conditions",
            "Weapon",
            "AutoChooseSources",
            "PreferredAgainst",
            "ShareWeaponReloadTime",
            "WeaponLockSharedAcrossSets");
    }

    /// <summary>
    /// Verifies that experience levels use numeric level keys.
    /// </summary>
    [Fact]
    public void GetBlockSchema_ExperienceLevels_UsesNumericLevelKeys()
    {
        var schema = _service.GetBlockSchema(IniConstants.BlockTypes.ExperienceLevels);

        schema.Should().NotBeNull();
        for (var level = 1; level <= 8; level++)
        {
            var key = $"Level{level}";
            schema!.Fields.Should().Contain(field => field.Key == key && field.IsNumeric);
        }
    }

    /// <summary>
    /// Verifies that command sets expose numbered slots.
    /// </summary>
    [Fact]
    public void GetBlockSchema_CommandSet_ExposesNumberedSlots()
    {
        var schema = _service.GetBlockSchema(IniConstants.BlockTypes.CommandSet);

        schema.Should().NotBeNull();
        schema!.Fields.Should().Contain(field => field.Key == "1");
    }

    /// <summary>
    /// Verifies that field lookup is case insensitive.
    /// </summary>
    [Fact]
    public void TryGetField_DifferentCasing_ReturnsSchema()
    {
        _service.TryGetField(IniConstants.BlockTypes.Object, "health", out var schema).Should().BeTrue();
        schema.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that unknown fields return false.
    /// </summary>
    [Fact]
    public void TryGetField_UnknownField_ReturnsFalse()
    {
        _service.TryGetField(IniConstants.BlockTypes.Object, "NotARealField", out var schema).Should().BeFalse();
        schema.Should().BeNull();
    }

    private static ILocalizationService CreateLocalizationService()
    {
        var mock = new Mock<ILocalizationService>();
        mock.Setup(service => service.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => key);
        return mock.Object;
    }
}
