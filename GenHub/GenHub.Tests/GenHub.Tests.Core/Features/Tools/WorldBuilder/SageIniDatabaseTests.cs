// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="SageIniDatabase"/>: boot-order loading with Default-to-override
/// merge, skipped-subsystem reporting, and map.ini loadWB tolerance. All mounts point at
/// temporary fixture directories; no real install is touched.
/// </summary>
public sealed class SageIniDatabaseTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="SageIniDatabaseTests"/> class.
    /// </summary>
    public SageIniDatabaseTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_SageIniTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Cleans up the temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// Tests that one subsystem loads its single file plus directory files, Default first.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadSubsystemsAsync_ObjectRow_LoadsSingleAndDirectoryDefaultFirstAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Default\Object.ini", "Object FromDefaultSingle\nEnd\n");
        WriteLoose(workspace, @"Data\INI\Default\Object\a.ini", "Object FromDefaultDir\nEnd\n");
        WriteLoose(workspace, @"Data\INI\Object.ini", "Object FromOverrideSingle\nEnd\n");
        WriteLoose(workspace, @"Data\INI\Object\b.ini", "Object FromOverrideDir\nEnd\n");
        WriteLoose(workspace, @"Data\INI\Object\sub\c.ini", "Object FromNestedDir\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);

        // Act
        var loaded = await sut.LoadSubsystemsAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeTrue();
        var entry = loaded.Data!.Subsystems.Should().ContainSingle(e => e.Subsystem == "Object").Subject;
        entry.Skipped.Should().BeFalse();
        entry.FilesRead.Should().ContainInOrder(
            @"Data\INI\Default\Object.ini",
            @"Data\INI\Default\Object\a.ini",
            @"Data\INI\Object.ini",
            @"Data\INI\Object\b.ini",
            @"Data\INI\Object\sub\c.ini");
        entry.BlocksLoaded.Should().Be(5);
    }

    /// <summary>
    /// Tests that override directories win over Default directories field by field.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadSubsystemsAsync_DefaultAndOverride_OverrideWinsAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Default\Terrain\t.ini", "Terrain Dirt\nTexture = a.tga\nGlintStrength = 1\nEnd\n");
        WriteLoose(workspace, @"Data\INI\Terrain\u.ini", "Terrain Dirt\nTexture = b.tga\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);

        // Act
        var loaded = await sut.LoadSubsystemsAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeTrue();
        var dirt = sut.FindBlock("Terrain", "Dirt");
        dirt.Should().NotBeNull();
        dirt!.Fields.Should().ContainSingle(f => f.Key == "Texture").Which.Values.Should().BeEquivalentTo("b.tga");
        dirt.Fields.Should().ContainSingle(f => f.Key == "GlintStrength");
    }

    /// <summary>
    /// Tests that subsystems absent from the mount are skipped and reported, never fatal.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadSubsystemsAsync_MissingSubsystems_SkippedAndReportedAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Object\b.ini", "Object Tank\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);

        // Act
        var loaded = await sut.LoadSubsystemsAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeTrue();
        loaded.Data!.Subsystems.Should().HaveCount(20);
        loaded.Data.Subsystems.Should().ContainSingle(e => e.Subsystem == "Object").Which.Skipped.Should().BeFalse();
        var science = loaded.Data.Subsystems.Should().ContainSingle(e => e.Subsystem == "Science").Subject;
        science.Skipped.Should().BeTrue();
        science.SkipReason.Should().Contain("Science");
        sut.FindBlock("Object", "Tank").Should().NotBeNull();
    }

    /// <summary>
    /// Tests that an empty mount succeeds with every subsystem skipped.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadSubsystemsAsync_EmptyMount_AllSkippedSuccessAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Art\Textures\t.tga", "t");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);

        // Act
        var loaded = await sut.LoadSubsystemsAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeTrue();
        loaded.Data!.BlocksLoaded.Should().Be(0);
        loaded.Data.Subsystems.Should().OnlyContain(e => e.Skipped);
        sut.GetBlocks("Object").Should().BeEmpty();
    }

    /// <summary>
    /// Tests that an unparsable file is reported while sibling files still load.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadSubsystemsAsync_BadFile_SiblingLoadsErrorReportedAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Object\bad.ini", "BogusBlock X\nEnd\n");
        WriteLoose(workspace, @"Data\INI\Object\good.ini", "Object Good\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);

        // Act
        var loaded = await sut.LoadSubsystemsAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeTrue();
        sut.FindBlock("Object", "Good").Should().NotBeNull();
        loaded.Data!.Diagnostics.Should().ContainSingle(d => d.SourceFile.EndsWith("bad.ini") && d.Message.Contains("Unknown block"));
        var entry = loaded.Data.Subsystems.Should().ContainSingle(e => e.Subsystem == "Object").Subject;
        entry.Skipped.Should().BeFalse();
        entry.BlocksLoaded.Should().Be(1);
    }

    /// <summary>
    /// Tests that a reskin in a later file resolves its parent from an earlier file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadSubsystemsAsync_CrossFileReskin_ParentResolvesAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Object\a_base.ini", "Object Base\nBuildCost = 100\nEnd\n");
        WriteLoose(workspace, @"Data\INI\Object\b_skin.ini", "ObjectReskin Skin Base\nGeometryHeight = 9\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);

        // Act
        var loaded = await sut.LoadSubsystemsAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeTrue();
        var skin = sut.FindBlock("ObjectReskin", "Skin");
        skin.Should().NotBeNull();
        skin!.Fields.Should().ContainSingle(f => f.Key == "BuildCost").Which.Values.Should().BeEquivalentTo("100");
        skin.Fields.Should().ContainSingle(f => f.Key == "GeometryHeight");
    }

    /// <summary>
    /// Tests that reloading replaces previously loaded subsystem state.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadSubsystemsAsync_Reload_ReplacesStateAsync()
    {
        // Arrange
        var first = NewDir("first");
        var second = NewDir("second");
        WriteLoose(first, @"Data\INI\Object\a.ini", "Object Old\nEnd\n");
        WriteLoose(second, @"Data\INI\Object\a.ini", "Object New\nEnd\n");
        var (firstFs, sut) = await CreateMountedDatabaseAsync(first);
        (await sut.LoadSubsystemsAsync(firstFs)).Success.Should().BeTrue();
        sut.FindBlock("Object", "Old").Should().NotBeNull();
        var secondFs = await MountAsync(second);

        // Act
        var reloaded = await sut.LoadSubsystemsAsync(secondFs);

        // Assert
        reloaded.Success.Should().BeTrue();
        sut.FindBlock("Object", "Old").Should().BeNull();
        sut.FindBlock("Object", "New").Should().NotBeNull();
    }

    /// <summary>
    /// Tests that block lookup is token-sensitive and name-insensitive.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FindBlock_TokenCaseAndNameCase_MatchesEngineAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Object\a.ini", "Object BaseTank\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);
        (await sut.LoadSubsystemsAsync(fileSystem)).Success.Should().BeTrue();

        // Act and assert
        sut.FindBlock("Object", "basetank").Should().NotBeNull();
        sut.FindBlock("object", "BaseTank").Should().BeNull();
        sut.FindBlock("Object", "Ghost").Should().BeNull();
        sut.GetBlocks("object").Should().BeEmpty();
        sut.GetBlocks("Nope").Should().BeEmpty();
    }

    /// <summary>
    /// Tests that cancellation is observed cooperatively.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadSubsystemsAsync_Cancelled_ThrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Object\a.ini", "Object Tank\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var act = () => sut.LoadSubsystemsAsync(fileSystem, cancelled.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests map.ini loadWB semantics: valid blocks apply, unrecognized and unfinishable
    /// blocks are skipped and reported, including ObjectExtend which the engine table omits.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadWorldBuilderIniAsync_MixedContent_SkipsAndReportsAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Object\a.ini", "Object BaseTank\nBuildCost = 100\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);
        (await sut.LoadSubsystemsAsync(fileSystem)).Success.Should().BeTrue();
        var mapDir = NewDir("map");
        var mapText = """
            Object MapTank
              BuildCost = 500
            End
            BogusBlock Something
              Foo = 1
            End
            ObjectReskin OrphanReskin MissingParent
              GeometryHeight = 1
            End
            ObjectExtend MapExtend BaseTank
              BuildCost = 600
            End
            Object MapTank2
              BuildCost = 700
            End
            """;
        var mapIni = WriteMapFile(mapDir, "map.ini", mapText);

        // Act
        var loaded = await sut.LoadWorldBuilderIniAsync(mapIni);

        // Assert
        loaded.Success.Should().BeTrue();
        loaded.Data!.BlocksLoaded.Should().Be(2);
        loaded.Data.SkippedBlocks.Should().ContainSingle().Which.HeaderLine.Should().Contain("ObjectReskin OrphanReskin MissingParent");
        loaded.Data.UnrecognizedBlocks.Select(b => b.HeaderLine).Should().BeEquivalentTo("BogusBlock Something", "ObjectExtend MapExtend BaseTank");
        sut.FindBlock("Object", "MapTank")!.Fields.Should().ContainSingle(f => f.Key == "BuildCost")
            .Which.Values.Should().BeEquivalentTo("500");
        sut.FindBlock("Object", "MapTank2").Should().NotBeNull();
        sut.FindBlock("Object", "BaseTank").Should().NotBeNull();
    }

    /// <summary>
    /// Tests that a map.ini reskin resolves its parent from the subsystem state.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadWorldBuilderIniAsync_ReskinOverSubsystems_InheritsParentAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Object\a.ini", "Object Base\nBuildCost = 100\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);
        (await sut.LoadSubsystemsAsync(fileSystem)).Success.Should().BeTrue();
        var mapDir = NewDir("map");
        var mapIni = WriteMapFile(mapDir, "map.ini", "ObjectReskin Skinned Base\nGeometryHeight = 9\nEnd\n");

        // Act
        var loaded = await sut.LoadWorldBuilderIniAsync(mapIni);

        // Assert
        loaded.Success.Should().BeTrue();
        var skin = sut.FindBlock("ObjectReskin", "Skinned");
        skin.Should().NotBeNull();
        skin!.Fields.Should().ContainSingle(f => f.Key == "BuildCost").Which.Values.Should().BeEquivalentTo("100");
        skin.Fields.Should().ContainSingle(f => f.Key == "GeometryHeight");
    }

    /// <summary>
    /// Tests that map.ini #include resolves relative to the map folder on disk.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadWorldBuilderIniAsync_IncludeRelative_ResolvesBesideMapAsync()
    {
        // Arrange
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(NewDir("workspace"));
        (await sut.LoadSubsystemsAsync(fileSystem)).Success.Should().BeTrue();
        var mapDir = NewDir("map");
        WriteMapFile(mapDir, "extra.ini", "Object Included\nBuildCost = 5\nEnd\n");
        var mapIni = WriteMapFile(mapDir, "map.ini", "#include \"extra.ini\"\nObject Direct\nEnd\n");

        // Act
        var loaded = await sut.LoadWorldBuilderIniAsync(mapIni);

        // Assert
        loaded.Success.Should().BeTrue();
        loaded.Data!.BlocksLoaded.Should().Be(2);
        sut.FindBlock("Object", "Included").Should().NotBeNull();
        sut.FindBlock("Object", "Direct").Should().NotBeNull();
    }

    /// <summary>
    /// Tests that loading a second map.ini replaces the previous map contribution only.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadWorldBuilderIniAsync_SecondMap_ReplacesMapContributionAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Object\a.ini", "Object Base\nEnd\n");
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);
        (await sut.LoadSubsystemsAsync(fileSystem)).Success.Should().BeTrue();
        var firstMap = NewDir("mapA");
        var secondMap = NewDir("mapB");
        var firstIni = WriteMapFile(firstMap, "map.ini", "Object AOnly\nEnd\n");
        var secondIni = WriteMapFile(secondMap, "map.ini", "Object BOnly\nEnd\n");
        (await sut.LoadWorldBuilderIniAsync(firstIni)).Success.Should().BeTrue();
        sut.FindBlock("Object", "AOnly").Should().NotBeNull();

        // Act
        var loaded = await sut.LoadWorldBuilderIniAsync(secondIni);

        // Assert
        loaded.Success.Should().BeTrue();
        sut.FindBlock("Object", "AOnly").Should().BeNull();
        sut.FindBlock("Object", "BOnly").Should().NotBeNull();
        sut.FindBlock("Object", "Base").Should().NotBeNull();
    }

    /// <summary>
    /// Tests that a missing map.ini path returns a failure.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadWorldBuilderIniAsync_MissingFile_ReturnsFailureAsync()
    {
        // Arrange
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(NewDir("workspace"));
        (await sut.LoadSubsystemsAsync(fileSystem)).Success.Should().BeTrue();

        // Act
        var loaded = await sut.LoadWorldBuilderIniAsync(Path.Combine(_tempRoot, "nope", "map.ini"));

        // Assert
        loaded.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that subsystem boot keeps game-shaped Object files: Draw modules with
    /// condition states, one-line alias directives and trailing Behavior modules
    /// must not fail the whole file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadSubsystemsAsync_GameShapedObjectFile_LoadsAllBlocksAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        var gameIni =
            "Object GLATank\n" +
            "RadarPriority = 5\n" +
            "Draw = W3DModelDraw ModuleTag_01\n" +
            "ConditionState = NONE\n" +
            "Model = GLATank\n" +
            "Animation = GLATank_Idle\n" +
            "End\n" +
            "AliasConditionState = NONE DAMAGED\n" +
            "End\n" +
            "Behavior = PhysicsBehavior ModuleTag_Physics\n" +
            "Mass = 1.0\n" +
            "End\n" +
            "End\n" +
            "Object GLAJeep\n" +
            "End\n";
        WriteLoose(workspace, @"Data\INI\Object\game.ini", gameIni);
        var (fileSystem, sut) = await CreateMountedDatabaseAsync(workspace);

        // Act
        var loaded = await sut.LoadSubsystemsAsync(fileSystem);

        // Assert
        loaded.Success.Should().BeTrue();
        sut.GetBlocks("Object").Select(b => b.Name).Should().BeEquivalentTo("GLATank", "GLAJeep");
    }

    private static SageIniDatabase CreateSut()
    {
        return new SageIniDatabase(
            new SageIniParser(NullLogger<SageIniParser>.Instance),
            NullLogger<SageIniDatabase>.Instance);
    }

    private static async Task<GameAssetFileSystem> MountAsync(string workspace)
    {
        var fileSystem = new GameAssetFileSystem(
            Mock.Of<IGameInstallationService>(),
            NullLogger<GameAssetFileSystem>.Instance);
        var mounted = await fileSystem.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace));
        mounted.Success.Should().BeTrue();
        return fileSystem;
    }

    private static void WriteLoose(string root, string relativePath, string contents)
    {
        var full = Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
    }

    private static string WriteMapFile(string mapDir, string fileName, string contents)
    {
        var full = Path.Combine(mapDir, fileName);
        File.WriteAllText(full, contents);
        return full;
    }

    private async Task<(GameAssetFileSystem FileSystem, SageIniDatabase Database)> CreateMountedDatabaseAsync(string workspace)
    {
        var fileSystem = await MountAsync(workspace);
        return (fileSystem, CreateSut());
    }

    private string NewDir(string name)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
