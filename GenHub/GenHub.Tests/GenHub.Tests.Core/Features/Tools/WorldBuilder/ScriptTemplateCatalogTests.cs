// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="ScriptTemplateCatalog"/>: INI-driven action and condition
/// templates plus the EditParameter picker taxonomy.
/// </summary>
public sealed class ScriptTemplateCatalogTests : IDisposable
{
    private readonly CatalogTestHost _host = CatalogTestHost.Create();

    /// <summary>
    /// Cleans up the test host.
    /// </summary>
    public void Dispose()
    {
        _host.Dispose();
    }

    /// <summary>
    /// Tests that action and condition templates key on InternalName with UI overrides.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Templates_KeyOnInternalNameWithUiOverridesAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var scriptsIni = """
            ScriptAction IgnoredHeader
              InternalName = PLAY_SOUND
              UIName = "Play a sound"
              UIName2 = "At full volume"
              HelpText = "Plays the named sound effect."
            End
            ScriptCondition FlagCheck
              InternalName = FLAG_IS_TRUE
              UIName = "Flag is true"
            End
            """;
        _host.WriteLoose(workspace, @"Data\Scripts\Scripts.ini", scriptsIni);
        var sut = await CreateCatalogAsync(workspace);

        // Act
        var action = sut.FindAction("play_sound");
        var condition = sut.FindCondition("FLAG_IS_TRUE");

        // Assert
        action.Should().NotBeNull();
        action!.InternalName.Should().Be("PLAY_SOUND");
        action.UiName.Should().Be("Play a sound");
        action.UiName2.Should().Be("At full volume");
        action.HelpText.Should().Be("Plays the named sound effect.");
        condition.Should().NotBeNull();
        condition!.UiName.Should().Be("Flag is true");
        condition.UiName2.Should().BeNull();
        sut.GetActions().Should().ContainSingle();
        sut.GetConditions().Should().ContainSingle();
        sut.FindAction("Ghost").Should().BeNull();
        sut.FindCondition("Ghost").Should().BeNull();
    }

    /// <summary>
    /// Tests that store-backed pickers list INI block names.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetPickList_Stores_ListsBlockNamesAsync()
    {
        // Arrange
        var workspace = WritePickerWorkspace();
        var sut = await CreateCatalogAsync(workspace);

        // Act and assert
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Science).Should().BeEquivalentTo("SCIENCE_Tanks", "SCIENCE_Jets");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Upgrade).Should().BeEquivalentTo("UpgradeArmor");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.SpecialPower).Should().BeEquivalentTo("SuperweaponNuke");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.CommandButton).Should().BeEquivalentTo("Command_Attack");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Movie).Should().BeEquivalentTo("IntroMovie");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Emoticon).Should().BeEquivalentTo("HappyFace");
    }

    /// <summary>
    /// Tests that audio pickers separate effects, dialog, and music by block token.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetPickList_Audio_SeparatesByBlockTokenAsync()
    {
        // Arrange
        var workspace = WritePickerWorkspace();
        var sut = await CreateCatalogAsync(workspace);

        // Act and assert
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Sound).Should().BeEquivalentTo("ExplosionLarge", "Gunfire");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Dialog).Should().BeEquivalentTo("MissionBriefing");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Music).Should().BeEquivalentTo("MainTheme");
    }

    /// <summary>
    /// Tests that side, faction, object, unit, and team pickers mix symbolic and INI names.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetPickList_Scoped_MixesSymbolicAndIniNamesAsync()
    {
        // Arrange
        var workspace = WritePickerWorkspace();
        var sut = await CreateCatalogAsync(workspace);

        // Act and assert
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.FactionName).Should().BeEquivalentTo("America", "China");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Side).Should().ContainInOrder(
            "<Local Player>", "<This Player>", "<This Player's Enemy>", "America", "China");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.ObjectType).Should().BeEquivalentTo("AmericaTank");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Unit).Should().BeEquivalentTo("<This Object>");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Team).Should().BeEquivalentTo("<This Team>");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.LocalizedText).Should().BeEmpty();
    }

    /// <summary>
    /// Tests that compiled pickers return the engine tables verbatim.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetPickList_Compiled_ReturnsEngineTablesAsync()
    {
        // Arrange
        var workspace = WritePickerWorkspace();
        var sut = await CreateCatalogAsync(workspace);

        // Act and assert
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Comparison).Should().HaveCount(6);
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.AiMood).Should().BeEquivalentTo(
            "Sleep", "Passive", "Normal", "Alert", "Aggressive");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Relation).Should().BeEquivalentTo("Enemy", "Neutral", "Friend");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.Buildable).Should().BeEquivalentTo(
            "Yes", "Ignore_Prerequisites", "No", "Only_By_AI");
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.ObjectPanelFlag).Should().HaveCount(7);
        sut.GetPickList(WorldBuilderConstants.ScriptParameterType.ScienceAvailability).Should().BeEquivalentTo(
            "Available", "Disabled", "Hidden");
        var kinds = sut.GetPickList(WorldBuilderConstants.ScriptParameterType.KindOf);
        kinds.Should().HaveCount(160);
        kinds.First().Should().Be("OBSTACLE");
        kinds.Last().Should().Be("NO_ATTACK_WARNING");
        var status = sut.GetPickList(WorldBuilderConstants.ScriptParameterType.ObjectStatus);
        status.Should().HaveCount(54);
        status.First().Should().Be("NONE");
        status.Last().Should().Be("SCUTTLING");
    }

    /// <summary>
    /// Tests that per-map and free-entry types return empty lists.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetPickList_MapAndFreeEntry_ReturnsEmptyAsync()
    {
        // Arrange
        var workspace = WritePickerWorkspace();
        var sut = await CreateCatalogAsync(workspace);

        // Act and assert
        foreach (var type in new[]
        {
            WorldBuilderConstants.ScriptParameterType.Script,
            WorldBuilderConstants.ScriptParameterType.ScriptSubroutine,
            WorldBuilderConstants.ScriptParameterType.Counter,
            WorldBuilderConstants.ScriptParameterType.Flag,
            WorldBuilderConstants.ScriptParameterType.Waypoint,
            WorldBuilderConstants.ScriptParameterType.WaypointPath,
            WorldBuilderConstants.ScriptParameterType.TriggerArea,
            WorldBuilderConstants.ScriptParameterType.AttackPrioritySet,
            WorldBuilderConstants.ScriptParameterType.Bridge,
            WorldBuilderConstants.ScriptParameterType.ObjectTypeList,
            WorldBuilderConstants.ScriptParameterType.Boundary,
            WorldBuilderConstants.ScriptParameterType.RevealName,
            WorldBuilderConstants.ScriptParameterType.FontName,
            WorldBuilderConstants.ScriptParameterType.Int,
            WorldBuilderConstants.ScriptParameterType.Real,
            WorldBuilderConstants.ScriptParameterType.TextString,
            WorldBuilderConstants.ScriptParameterType.Coord3D,
            WorldBuilderConstants.ScriptParameterType.Angle,
            WorldBuilderConstants.ScriptParameterType.Boolean,
            WorldBuilderConstants.ScriptParameterType.Color,
            WorldBuilderConstants.ScriptParameterType.Percent,
        })
        {
            sut.GetPickList(type).Should().BeEmpty();
        }
    }

    private string WritePickerWorkspace()
    {
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Data\INI\Science\s.ini", "Science SCIENCE_Tanks\nEnd\nScience SCIENCE_Jets\nEnd\n");
        _host.WriteLoose(workspace, @"Data\INI\Upgrade\u.ini", "Upgrade UpgradeArmor\nEnd\n");
        _host.WriteLoose(workspace, @"Data\INI\SpecialPower\sp.ini", "SpecialPower SuperweaponNuke\nEnd\n");
        var audioIni = """
            AudioEvent ExplosionLarge
            End
            AudioEvent Gunfire
            End
            DialogEvent MissionBriefing
            End
            MusicTrack MainTheme
            End
            CommandButton Command_Attack
            End
            Video IntroMovie
            End
            Animation HappyFace
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\AudioEvents\a.ini", audioIni);
        var playersIni = """
            PlayerTemplate FactionAmerica
              Side = America
            End
            PlayerTemplate FactionChina
              Side = China
            End
            PlayerTemplate FactionAmericaAir
              Side = America
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\PlayerTemplate\p.ini", playersIni);
        _host.WriteLoose(workspace, @"Data\INI\Object\o.ini", "Object AmericaTank\nSide = America\nEnd\n");
        return workspace;
    }

    private async Task<ScriptTemplateCatalog> CreateCatalogAsync(string workspace)
    {
        var (_, database) = await _host.CreateLoadedDatabaseAsync(workspace);
        return new ScriptTemplateCatalog(
            database,
            new ThingTemplateCatalog(
                database,
                new MappedImageRegistry(Mock.Of<ISageMappedImageParser>(), NullLogger<MappedImageRegistry>.Instance),
                NullLogger<ThingTemplateCatalog>.Instance),
            new StringTableService(NullLogger<StringTableService>.Instance),
            NullLogger<ScriptTemplateCatalog>.Instance);
    }
}
