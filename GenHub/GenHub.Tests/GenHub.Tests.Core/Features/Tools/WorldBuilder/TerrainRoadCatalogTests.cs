// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="TerrainTypeCatalog"/> and <see cref="RoadCatalog"/>.
/// </summary>
public sealed class TerrainRoadCatalogTests : IDisposable
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
    /// Tests that terrain fields are typed per the TerrainTypes field table.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Terrain_All_TypesFieldsAndDefaultsAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var terrainIni = """
            Terrain Dirt
              Texture = dirt.tga
              BlendEdges = No
              Class = DIRT
              RestrictConstruction = Yes
              GlintStrength = 0.5
              GlintGloss = 2.0
            End
            Terrain Plain
              Texture = plain.tga
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\Terrain\t.ini", terrainIni);
        var (_, database) = await _host.CreateLoadedDatabaseAsync(workspace);
        var sut = new TerrainTypeCatalog(database, NullLogger<TerrainTypeCatalog>.Instance);

        // Act
        var dirt = sut.FindByName("dirt");

        // Assert
        sut.GetAll().Should().HaveCount(2);
        dirt.Should().NotBeNull();
        dirt!.Texture.Should().Be("dirt.tga");
        dirt.IsBlendEdge.Should().BeFalse();
        dirt.Class.Should().Be("DIRT");
        dirt.RestrictConstruction.Should().BeTrue();
        dirt.GlintStrength.Should().BeApproximately(0.5f, 0.001f);
        dirt.GlintGloss.Should().BeApproximately(2.0f, 0.001f);
        var plain = sut.FindByName("Plain")!;
        plain.IsBlendEdge.Should().BeFalse();
        plain.Class.Should().BeNull();
        plain.RestrictConstruction.Should().BeFalse();
        plain.GlintStrength.Should().BeApproximately(1.0f, 0.001f);
        plain.GlintGloss.Should().BeApproximately(0.0f, 0.001f);
        sut.FindByName("Ghost").Should().BeNull();
    }

    /// <summary>
    /// Tests that the palette skips blend edges and sorts by class, then name.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Terrain_PaletteEntries_SkipsBlendEdgesSortsByClassAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var paletteIni = """
            Terrain ZebraGrass
              Texture = zg.tga
              Class = GRASS
            End
            Terrain AlphaDirt
              Texture = ad.tga
              Class = DIRT
            End
            Terrain EdgeBlend
              Texture = eb.tga
              BlendEdges = Yes
              Class = GRASS
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\Terrain\t.ini", paletteIni);
        var (_, database) = await _host.CreateLoadedDatabaseAsync(workspace);
        var sut = new TerrainTypeCatalog(database, NullLogger<TerrainTypeCatalog>.Instance);

        // Act
        var palette = sut.GetPaletteEntries();

        // Assert
        palette.Select(terrain => terrain.Name).Should().ContainInOrder("AlphaDirt", "ZebraGrass");
    }

    /// <summary>
    /// Tests that road fields are typed per the TerrainRoads field table.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Road_Roads_TypesFieldsAsync()
    {
        // Arrange
        var workspace = _host.NewDir("workspace");
        var roadsIni = """
            Road DirtRoad
              Texture = dirtroad.tga
              RoadWidth = 12
              RoadWidthInTexture = 0.8
            End
            Bridge StoneBridge
              BridgeScale = 1.5
              ScaffoldObjectName = Scaffold
              ScaffoldSupportObjectName = ScaffoldSupport
              RadarColor = R:10 G:20 B:30
              BridgeModelName = bridge_deck
              Texture = bridge.tga
              BridgeModelNameDamaged = bridge_deck_d
              TextureDamaged = bridge_d.tga
              BridgeModelNameBroken = bridge_deck_b
              TextureBroken = bridge_b.tga
              TowerObjectNameFromLeft = TowerFL
              TowerObjectNameToRight = TowerTR
              DamagedToSound = BridgeCreak
              BridgeHoleAreaPercentage = 25%
            End
            """;
        _host.WriteLoose(workspace, @"Data\INI\Roads\r.ini", roadsIni);
        var (_, database) = await _host.CreateLoadedDatabaseAsync(workspace);
        var sut = new RoadCatalog(database, NullLogger<RoadCatalog>.Instance);

        // Act
        var road = sut.FindRoad("dirtroad");
        var bridge = sut.FindBridge("StoneBridge");

        // Assert
        road.Should().NotBeNull();
        road!.Texture.Should().Be("dirtroad.tga");
        road.RoadWidth.Should().BeApproximately(12.0f, 0.001f);
        road.RoadWidthInTexture.Should().BeApproximately(0.8f, 0.001f);
        bridge.Should().NotBeNull();
        bridge!.BridgeScale.Should().BeApproximately(1.5f, 0.001f);
        bridge.ScaffoldObjectName.Should().Be("Scaffold");
        bridge.ScaffoldSupportObjectName.Should().Be("ScaffoldSupport");
        bridge.RadarColor.Should().Be(unchecked((int)0xFF0A141E));
        bridge.BridgeModelName.Should().Be("bridge_deck");
        bridge.Texture.Should().Be("bridge.tga");
        bridge.BridgeModelNameDamaged.Should().Be("bridge_deck_d");
        bridge.TextureDamaged.Should().Be("bridge_d.tga");
        bridge.BridgeModelNameReallyDamaged.Should().BeNull();
        bridge.BridgeModelNameBroken.Should().Be("bridge_deck_b");
        bridge.TextureBroken.Should().Be("bridge_b.tga");
        bridge.TowerObjectNames.Should().BeEquivalentTo("TowerFL", "TowerTR");
        bridge.DamagedToSound.Should().Be("BridgeCreak");
        bridge.RepairedToSound.Should().BeNull();
        bridge.BridgeHoleAreaPercentage.Should().BeApproximately(0.25f, 0.001f);
        sut.GetRoads().Should().ContainSingle();
        sut.GetBridges().Should().ContainSingle();
        sut.FindRoad("Ghost").Should().BeNull();
        sut.FindBridge("Ghost").Should().BeNull();
    }
}
