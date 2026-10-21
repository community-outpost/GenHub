// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="ThingTemplateCatalog"/>: typed fields, reskin inheritance,
/// Draw-module model names, ButtonImage resolution, and the object-tree tiers.
/// </summary>
public sealed class ThingTemplateCatalogTests : IDisposable
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
    /// Tests that typed fields are read per the ThingTemplate field table.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FindByName_FullBlock_TypesAllFieldsAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var tankIni = """
            Object AmericaTank
              DisplayName = LABEL:NameTank
              Side = America
              EditorSorting = VEHICLE
              KindOf = SELECTABLE CAN_ATTACK VEHICLE
              Buildable = Yes
              BuildCost = 800
              BuildTime = 7.5
              DisplayColor = R:255 G:128 B:0
              ButtonImage = CameoTank
              SelectPortrait = PortraitTank
              CommandSet = TankCommandSet
              BuildVariations = AmericaTankV2 AmericaTankV3
              Scale = 1.25
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\Object\tank.ini", tankIni);
        var catalog = await CreateCatalogAsync(workspace);

        // Act
        var tank = catalog.FindByName("americatank");

        // Assert
        tank.Should().NotBeNull();
        tank!.DisplayName.Should().Be("LABEL:NameTank");
        tank.Side.Should().Be("America");
        tank.EditorSorting.Should().Be("VEHICLE");
        tank.KindOf.Should().BeEquivalentTo("SELECTABLE", "CAN_ATTACK", "VEHICLE");
        tank.Buildable.Should().Be("Yes");
        tank.BuildCost.Should().Be(800);
        tank.BuildTime.Should().BeApproximately(7.5f, 0.001f);
        tank.DisplayColor.Should().Be(unchecked((int)0xFFFF8000));
        tank.ButtonImage.Should().Be("CameoTank");
        tank.SelectPortrait.Should().Be("PortraitTank");
        tank.CommandSet.Should().Be("TankCommandSet");
        tank.BuildVariations.Should().BeEquivalentTo("AmericaTankV2", "AmericaTankV3");
        tank.AssetScale.Should().BeApproximately(1.25f, 0.001f);
        tank.IsReskin.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a reskin inherits gameplay fields and keeps its own visuals.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FindByName_Reskin_InheritsParentKeepsOwnDrawAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var shipsIni = """
            Object NavalBase
              Side = America
              EditorSorting = VEHICLE
              BuildCost = 100
              Draw = W3DModelDraw ModuleTag_01
                DefaultConditionState
                  Model = base_ship
                End
              End
            End
            ObjectReskin NavalSkinned NavalBase
              Draw = W3DModelDraw ModuleTag_01
                DefaultConditionState
                  Model = skinned_ship
                End
              End
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\Object\ships.ini", shipsIni);
        var catalog = await CreateCatalogAsync(workspace);

        // Act
        var skin = catalog.FindByName("NavalSkinned");

        // Assert
        skin.Should().NotBeNull();
        skin!.IsReskin.Should().BeTrue();
        skin.ParentName.Should().Be("NavalBase");
        skin.Side.Should().Be("America");
        skin.EditorSorting.Should().Be("VEHICLE");
        skin.BuildCost.Should().Be(100);
        skin.ModelName.Should().Be("skinned_ship");
    }

    /// <summary>
    /// Tests that the first Draw module supplies the model: first condition-state
    /// Model for model draws, ModelName for tree draws, null without a Draw module.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FindByName_DrawModules_ResolvesModelNamesAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var modelsIni = """
            Object ConditionTank
              Side = America
              Draw = W3DModelDraw ModuleTag_01
                DefaultConditionState
                  Model = default_model
                End
                ConditionState = DAMAGED
                  Model = damaged_model
                End
              End
            End
            Object OakTree
              Side = Neutral
              Draw = W3DTreeDraw ModuleTag_01
                ModelName = oak_mesh
              End
            End
            Object Siren
              Side = America
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\Object\models.ini", modelsIni);
        var catalog = await CreateCatalogAsync(workspace);

        // Act and assert
        catalog.FindByName("ConditionTank")!.ModelName.Should().Be("default_model");
        catalog.FindByName("OakTree")!.ModelName.Should().Be("oak_mesh");
        catalog.FindByName("Siren")!.ModelName.Should().BeNull();
    }

    /// <summary>
    /// Tests that ArmorSet, WeaponSet, and Prerequisites scopes parse without
    /// truncating the block or leaking End tokens.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FindByName_NestedScopes_BlockSurvivesWithTrailingFieldsAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var scopedIni = """
            Object ScopedTank
              Side = America
              ArmorSet
                Armor = TankArmor 100
              End
              WeaponSet
                Weapon = TankCannon
              End
              Prerequisites
                Science = SCIENCE_Tanks
              End
              BuildCost = 900
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\Object\scoped.ini", scopedIni);
        var catalog = await CreateCatalogAsync(workspace);

        // Act
        var tank = catalog.FindByName("ScopedTank");

        // Assert
        tank.Should().NotBeNull();
        tank!.Side.Should().Be("America");
        tank.BuildCost.Should().Be(900);
    }

    /// <summary>
    /// Tests the tree tiers: TEST root, Side, EditorSorting or UNSORTED, leaf.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildObjectTree_Tiers_SideSortingLeafAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var tiersIni = """
            Object TankA
              Side = America
              EditorSorting = STRUCTURE
            End
            Object TankB
              Side = America
              EditorSorting = VEHICLE
            End
            Object NoSorting
              Side = China
            End
            Object TestUnit
              Side = America
              EditorSorting = TEST
            End
            Object NoSide
              EditorSorting = STRUCTURE
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\Object\tree.ini", tiersIni);
        var catalog = await CreateCatalogAsync(workspace);

        // Act
        var root = catalog.BuildObjectTree();

        // Assert
        root.Kind.Should().Be(ObjectTreeNodeKind.Root);
        var test = root.Children.Should().ContainSingle(n => n.Name == "TEST").Subject;
        test.Kind.Should().Be(ObjectTreeNodeKind.Test);
        LeafNames(test, "America", "TEST").Should().BeEquivalentTo("TestUnit");
        LeafNames(root, "America", "STRUCTURE").Should().BeEquivalentTo("TankA");
        LeafNames(root, "America", "VEHICLE").Should().BeEquivalentTo("TankB");
        LeafNames(root, "China", "UNSORTED").Should().BeEquivalentTo("NoSorting");
        LeafNames(root, "UNSORTED", "STRUCTURE").Should().BeEquivalentTo("NoSide");
    }

    /// <summary>
    /// Tests that legacy model names land under the legacy models branch.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildObjectTree_LegacyNames_LegacyBranchAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Data\INI\Object\tree.ini", "Object TankA\nSide = America\nEnd\n");
        var catalog = await CreateCatalogAsync(workspace);

        // Act
        var root = catalog.BuildObjectTree(["old_bridge", "old_tower"]);

        // Assert
        var legacy = root.Children.Should().ContainSingle(n => n.Name == "**TEST MODELS").Subject;
        legacy.Kind.Should().Be(ObjectTreeNodeKind.LegacyModels);
        legacy.Children.Select(leaf => leaf.Name).Should().BeEquivalentTo("old_bridge", "old_tower");
        legacy.Children.Should().OnlyContain(leaf => leaf.Kind == ObjectTreeNodeKind.Template && leaf.Template == null);
    }

    /// <summary>
    /// Tests that the TEST and legacy branches are omitted when empty.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuildObjectTree_NoTestOrLegacy_OmitsBranchesAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        _host.WriteLoose(workspace, @"Data\INI\Object\tree.ini", "Object TankA\nSide = America\nEnd\n");
        var catalog = await CreateCatalogAsync(workspace);

        // Act
        var root = catalog.BuildObjectTree();

        // Assert
        root.Children.Should().ContainSingle().Which.Name.Should().Be("America");
    }

    /// <summary>
    /// Tests that ButtonImage and SelectPortrait resolve through the mapped-image registry.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FindButtonImage_KnownAndUnknown_ResolvesOrNullAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var iconsIni = """
            Object IconTank
              Side = America
              ButtonImage = CameoTank
              SelectPortrait = PortraitTank
            End
            Object PlainTank
              Side = America
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\Object\icons.ini", iconsIni);
        var registry = new MappedImageRegistry(Mock.Of<ISageMappedImageParser>(), NullLogger<MappedImageRegistry>.Instance);
        registry.ImportDefinitions([new MappedImageDefinition("CameoTank", "Cameos.tga", 256, 256, 0, 0, 64, 64)]);
        var catalog = await CreateCatalogAsync(workspace, registry);

        // Act and assert
        var icon = catalog.FindByName("IconTank")!;
        catalog.FindButtonImage(icon)!.TextureFileName.Should().Be("Cameos.tga");
        catalog.FindSelectPortrait(icon).Should().BeNull();
        catalog.FindButtonImage(catalog.FindByName("PlainTank")!).Should().BeNull();
    }

    private static IReadOnlyList<string> LeafNames(ObjectTreeNode root, string side, string sorting)
    {
        var sideNode = root.Children.Should().ContainSingle(n => n.Name == side).Subject;
        sideNode.Kind.Should().Be(ObjectTreeNodeKind.Side);
        var sortingNode = sideNode.Children.Should().ContainSingle(n => n.Name == sorting).Subject;
        sortingNode.Kind.Should().Be(ObjectTreeNodeKind.Sorting);
        sortingNode.Children.Should().OnlyContain(leaf => leaf.Kind == ObjectTreeNodeKind.Template && leaf.Template != null);
        return sortingNode.Children.Select(leaf => leaf.Name).ToList();
    }

    private async Task<ThingTemplateCatalog> CreateCatalogAsync(string workspace, MappedImageRegistry? registry = null)
    {
        var (_, database) = await _host.CreateLoadedDatabaseAsync(workspace);
        return new ThingTemplateCatalog(
            database,
            registry ?? new MappedImageRegistry(Mock.Of<ISageMappedImageParser>(), NullLogger<MappedImageRegistry>.Instance),
            NullLogger<ThingTemplateCatalog>.Instance);
    }
}
