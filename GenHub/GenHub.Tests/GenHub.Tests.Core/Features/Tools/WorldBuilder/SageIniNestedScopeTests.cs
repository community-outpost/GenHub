// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for nested-scope parsing in <see cref="SageIniParser"/>: bare ArmorSet,
/// WeaponSet, and Prerequisites scopes at block level plus condition-state scopes
/// nested inside Draw modules.
/// </summary>
public sealed class SageIniNestedScopeTests
{
    /// <summary>
    /// Tests that bare scopes become sub-blocks and trailing fields stay on the block.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_BareScopes_SubBlocksAndTrailingFieldsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = """
            Object Scoped
              Side = America
              ArmorSet
                Armor = TankArmor 100
                Armor = FlakArmor 50
              End
              BuildCost = 100
            End
            """;

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.Fields.Select(field => field.Key).Should().BeEquivalentTo("Side", "BuildCost");
        var armor = block.SubBlocks.Should().ContainSingle().Subject;
        armor.Key.Should().Be("ArmorSet");
        armor.ModuleType.Should().BeEmpty();
        armor.Tag.Should().BeEmpty();
        armor.Fields.Should().HaveCount(2);
    }

    /// <summary>
    /// Tests that condition-state scopes inside a Draw module flatten in source order
    /// without closing the module or the block.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_ConditionStates_FlattenedInOrderAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = """
            Object Tank
              Side = America
              Draw = W3DModelDraw ModuleTag_01
                DefaultConditionState
                  Model = default_model
                End
                ConditionState = DAMAGED
                  Model = damaged_model
                End
              End
              BuildCost = 100
            End
            """;

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeTrue();
        var block = parsed.Data!.Blocks.Should().ContainSingle().Subject;
        block.Fields.Select(field => field.Key).Should().BeEquivalentTo("Side", "BuildCost");
        var draw = block.SubBlocks.Should().ContainSingle().Subject;
        draw.ModuleType.Should().Be("W3DModelDraw");
        draw.Fields.Select(field => field.Key).Should().ContainInOrder(
            "DefaultConditionState", "Model", "ConditionState", "Model");
    }

    /// <summary>
    /// Tests that an unterminated bare scope fails the block in strict mode.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ParseAsync_UnterminatedScope_StrictFailsAsync()
    {
        // Arrange
        var sut = CreateSut();
        var text = "Object Tank\nArmorSet\nArmor = X\n";

        // Act
        var parsed = await sut.ParseAsync(text, "test.ini", new SageIniParseOptions());

        // Assert
        parsed.Success.Should().BeFalse();
        parsed.FirstError.Should().Contain("End");
    }

    private static SageIniParser CreateSut()
    {
        return new SageIniParser(NullLogger<SageIniParser>.Instance);
    }
}
