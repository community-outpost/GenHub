// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using GenHub.Tests.Core.Features.Tools.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="GameAssetFileSystem"/> layering: loose-over-archive,
/// Zero Hour over Generals, mod over base, and bundled-base fallback.
/// All mounts point at temporary fixture directories; no real install is touched.
/// </summary>
public sealed class GameAssetFileSystemTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameAssetFileSystemTests"/> class.
    /// </summary>
    public GameAssetFileSystemTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHub_VfsTests_" + Guid.NewGuid().ToString("N"));
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
    /// Tests that a workspace loose file wins over the same path inside a workspace archive.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_WorkspaceLooseOverArchive_LooseWinsAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\GameData.ini", "loose");
        BigArchiveFixture.Write(
            Path.Combine(workspace, "Test.big"),
            (@"Data\INI\GameData.ini", "archive"),
            (@"Data\INI\ArchiveOnly.ini", "archive-only"));
        var sut = CreateSut();

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace));

        // Assert
        mounted.Success.Should().BeTrue();
        (await ReadTextAsync(sut, @"Data\INI\GameData.ini")).Should().Be("loose");
        (await ReadTextAsync(sut, @"Data\INI\ArchiveOnly.ini")).Should().Be("archive-only");
    }

    /// <summary>
    /// Tests that a Zero Hour archive wins over a Generals archive holding the same path.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_ZeroHourOverGenerals_ZeroHourWinsAsync()
    {
        // Arrange
        var generals = NewDir("generals");
        var zeroHour = NewDir("zerohour");
        BigArchiveFixture.Write(Path.Combine(generals, "G.big"), (@"Data\INI\Shared.ini", "generals"));
        BigArchiveFixture.Write(Path.Combine(zeroHour, "Z.big"), (@"Data\INI\Shared.ini", "zerohour"));
        var sut = CreateSut();

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(ZeroHourRoot: zeroHour, GeneralsRoot: generals));

        // Assert
        mounted.Success.Should().BeTrue();
        (await ReadTextAsync(sut, @"Data\INI\Shared.ini")).Should().Be("zerohour");
    }

    /// <summary>
    /// Tests that INIZH.big wins over INI.big when both live in the same layer.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_IniZhOverIni_SameLayer_ZeroHourWinsAsync()
    {
        // Arrange
        var workspace = NewDir("combined");
        BigArchiveFixture.Write(Path.Combine(workspace, "INI.big"), (@"Data\INI\Shared.ini", "generals"));
        BigArchiveFixture.Write(Path.Combine(workspace, "INIZH.big"), (@"Data\INI\Shared.ini", "zerohour"));
        var sut = CreateSut();

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace));

        // Assert
        mounted.Success.Should().BeTrue();
        (await ReadTextAsync(sut, @"Data\INI\Shared.ini")).Should().Be("zerohour");
    }

    /// <summary>
    /// Tests that an explicit mod directory wins over workspace loose files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_ModDirectoryOverWorkspace_ModWinsAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        var mod = NewDir("mod");
        WriteLoose(workspace, @"Data\INI\Shared.ini", "workspace");
        WriteLoose(mod, @"Data\INI\Shared.ini", "mod");
        var sut = CreateSut();

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(ModPath: mod, WorkspaceRoot: workspace));

        // Assert
        mounted.Success.Should().BeTrue();
        (await ReadTextAsync(sut, @"Data\INI\Shared.ini")).Should().Be("mod");
    }

    /// <summary>
    /// Tests that a mod path pointing at a single .big file mounts its entries.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_ModSingleArchive_MountsEntriesAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\Shared.ini", "workspace");
        var modArchive = Path.Combine(_tempRoot, "Mod.big");
        BigArchiveFixture.Write(modArchive, (@"Data\INI\Shared.ini", "mod"));
        var sut = CreateSut();

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(ModPath: modArchive, WorkspaceRoot: workspace));

        // Assert
        mounted.Success.Should().BeTrue();
        (await ReadTextAsync(sut, @"Data\INI\Shared.ini")).Should().Be("mod");
    }

    /// <summary>
    /// Tests that the bundled Generals base serves files missing from every higher layer.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_BundledBaseFallback_BundledVisibleAsync()
    {
        // Arrange
        var generals = NewDir("generals");
        var bundled = NewDir("bundled");
        WriteLoose(bundled, @"Data\INI\BaseOnly.ini", "bundled");
        WriteLoose(bundled, @"Data\INI\Shared.ini", "bundled");
        WriteLoose(generals, @"Data\INI\Shared.ini", "generals");
        var sut = CreateSut();

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(GeneralsRoot: generals, BundledGeneralsRoot: bundled));

        // Assert
        mounted.Success.Should().BeTrue();
        (await ReadTextAsync(sut, @"Data\INI\BaseOnly.ini")).Should().Be("bundled");
        (await ReadTextAsync(sut, @"Data\INI\Shared.ini")).Should().Be("generals");
    }

    /// <summary>
    /// Tests that an installation id resolves Zero Hour, Generals, and bundled roots.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_InstallationId_ResolvesRootsFromServiceAsync()
    {
        // Arrange
        var generals = NewDir("generals");
        var zeroHour = NewDir("zerohour");
        BigArchiveFixture.Write(Path.Combine(generals, "G.big"), (@"Data\INI\FromGenerals.ini", "generals"));
        BigArchiveFixture.Write(Path.Combine(zeroHour, "Z.big"), (@"Data\INI\FromZeroHour.ini", "zerohour"));
        var bundled = Path.Combine(zeroHour, "ZH_Generals");
        Directory.CreateDirectory(bundled);
        BigArchiveFixture.Write(Path.Combine(bundled, "B.big"), (@"Data\INI\FromBundled.ini", "bundled"));
        var installation = new GameInstallation(_tempRoot, GameInstallationType.Retail)
        {
            GeneralsPath = generals,
            ZeroHourPath = zeroHour,
            HasGenerals = true,
            HasZeroHour = true,
        };
        var service = new Mock<IGameInstallationService>();
        service.Setup(s => s.GetInstallationAsync("install-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));
        var sut = CreateSut(service.Object);

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(InstallationId: "install-1"));

        // Assert
        mounted.Success.Should().BeTrue();
        (await ReadTextAsync(sut, @"Data\INI\FromGenerals.ini")).Should().Be("generals");
        (await ReadTextAsync(sut, @"Data\INI\FromZeroHour.ini")).Should().Be("zerohour");
        (await ReadTextAsync(sut, @"Data\INI\FromBundled.ini")).Should().Be("bundled");
        service.Verify(s => s.GetInstallationAsync("install-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that explicit roots win over installation-resolved roots.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_ExplicitRootOverResolved_ExplicitWinsAsync()
    {
        // Arrange
        var resolved = NewDir("resolved");
        var explicitRoot = NewDir("explicit");
        WriteLoose(resolved, @"Data\INI\Shared.ini", "resolved");
        WriteLoose(explicitRoot, @"Data\INI\Shared.ini", "explicit");
        var installation = new GameInstallation(_tempRoot, GameInstallationType.Retail)
        {
            ZeroHourPath = resolved,
            HasZeroHour = true,
        };
        var service = new Mock<IGameInstallationService>();
        service.Setup(s => s.GetInstallationAsync("install-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));
        var sut = CreateSut(service.Object);

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(InstallationId: "install-1", ZeroHourRoot: explicitRoot));

        // Assert
        mounted.Success.Should().BeTrue();
        (await ReadTextAsync(sut, @"Data\INI\Shared.ini")).Should().Be("explicit");
    }

    /// <summary>
    /// Tests that an unresolvable installation id fails the mount.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_UnknownInstallationId_ReturnsFailureAsync()
    {
        // Arrange
        var service = new Mock<IGameInstallationService>();
        service.Setup(s => s.GetInstallationAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateFailure("Not found."));
        var sut = CreateSut(service.Object);

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(InstallationId: "missing"));

        // Assert
        mounted.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a mount with no roots fails instead of silently mounting nothing.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_NoRoots_ReturnsFailureAsync()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec());

        // Assert
        mounted.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a mount whose roots all miss disk fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_MissingRoots_ReturnsFailureAsync()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var mounted = await sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: Path.Combine(_tempRoot, "nope")));

        // Assert
        mounted.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that file existence is case-insensitive and separator-tolerant.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FileExists_VariousCasingsAndSeparators_MatchesAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\GameData.ini", "x");
        var sut = CreateSut();
        (await sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace))).Success.Should().BeTrue();

        // Act and assert
        sut.FileExists(@"Data\INI\GameData.ini").Should().BeTrue();
        sut.FileExists(@"data\ini\gamedata.ini").Should().BeTrue();
        sut.FileExists("Data/INI/GameData.ini").Should().BeTrue();
        sut.FileExists(@"Data\INI\Missing.ini").Should().BeFalse();
        sut.FileExists("   ").Should().BeFalse();
    }

    /// <summary>
    /// Tests that reading a missing file returns a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReadAllBytesAsync_MissingFile_ReturnsFailureAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\GameData.ini", "x");
        var sut = CreateSut();
        (await sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace))).Success.Should().BeTrue();

        // Act
        var read = await sut.ReadAllBytesAsync(@"Data\INI\Missing.ini");

        // Assert
        read.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests directory, pattern, and recursion filtering of ListFiles.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ListFiles_PatternAndRecurse_FiltersAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\c.ini", "c");
        WriteLoose(workspace, @"Data\INI\Object\a.ini", "a");
        WriteLoose(workspace, @"Data\INI\Object\sub\b.ini", "b");
        WriteLoose(workspace, @"Art\Textures\t.tga", "t");
        var sut = CreateSut();
        (await sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace))).Success.Should().BeTrue();

        // Act and assert
        sut.ListFiles(@"Data\INI", "*.ini", recurse: false).Should().BeEquivalentTo(@"Data\INI\c.ini");
        sut.ListFiles(@"Data\INI", "*.ini", recurse: true).Should().BeEquivalentTo(
            @"Data\INI\c.ini",
            @"Data\INI\Object\a.ini",
            @"Data\INI\Object\sub\b.ini");
        sut.ListFiles(@"data\ini\object", "*.ini", recurse: true).Should().BeEquivalentTo(
            @"Data\INI\Object\a.ini",
            @"Data\INI\Object\sub\b.ini");
        sut.ListFiles(string.Empty, "*.tga", recurse: true).Should().BeEquivalentTo(@"Art\Textures\t.tga");
        sut.ListFiles(@"Data\INI", "c.*", recurse: true).Should().BeEquivalentTo(@"Data\INI\c.ini");
        sut.ListFiles(@"Data\INI", "*.big", recurse: true).Should().BeEmpty();
    }

    /// <summary>
    /// Tests that ListFiles returns paths sorted case-insensitively.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ListFiles_MultipleFiles_SortedCaseInsensitivelyAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\zebra.ini", "z");
        WriteLoose(workspace, @"Data\INI\Apple.ini", "a");
        WriteLoose(workspace, @"Data\INI\mango.ini", "m");
        var sut = CreateSut();
        (await sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace))).Success.Should().BeTrue();

        // Act
        var files = sut.ListFiles(@"Data\INI", "*.ini", recurse: false);

        // Assert
        files.Should().ContainInOrder(@"Data\INI\Apple.ini", @"Data\INI\mango.ini", @"Data\INI\zebra.ini");
    }

    /// <summary>
    /// Tests that a cancelled mount observes cooperative cancellation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_Cancelled_ThrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        var workspace = NewDir("workspace");
        WriteLoose(workspace, @"Data\INI\GameData.ini", "x");
        var sut = CreateSut();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var act = () => sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: workspace), cancelled.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Tests that remounting replaces the previous mount instead of merging.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MountAsync_Remount_ReplacesPreviousStateAsync()
    {
        // Arrange
        var first = NewDir("first");
        var second = NewDir("second");
        WriteLoose(first, @"Data\INI\First.ini", "first");
        WriteLoose(second, @"Data\INI\Second.ini", "second");
        var sut = CreateSut();
        (await sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: first))).Success.Should().BeTrue();
        sut.FileExists(@"Data\INI\First.ini").Should().BeTrue();

        // Act
        var remounted = await sut.MountAsync(new GameAssetMountSpec(WorkspaceRoot: second));

        // Assert
        remounted.Success.Should().BeTrue();
        sut.FileExists(@"Data\INI\First.ini").Should().BeFalse();
        sut.FileExists(@"Data\INI\Second.ini").Should().BeTrue();
    }

    private static GameAssetFileSystem CreateSut(IGameInstallationService? installations = null)
    {
        return new GameAssetFileSystem(
            installations ?? Mock.Of<IGameInstallationService>(),
            NullLogger<GameAssetFileSystem>.Instance);
    }

    private static async Task<string> ReadTextAsync(GameAssetFileSystem sut, string virtualPath)
    {
        var read = await sut.ReadAllBytesAsync(virtualPath);
        read.Success.Should().BeTrue();
        return System.Text.Encoding.ASCII.GetString(read.Data!);
    }

    private static void WriteLoose(string root, string relativePath, string contents)
    {
        var full = Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
    }

    private string NewDir(string name)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
