using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace.Strategies;
using GenHub.Tests.Core.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Xunit;
using ContentInstallTarget = GenHub.Core.Models.Enums.ContentInstallTarget;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Unit tests for <see cref="WorkspaceCompatibilityHelper"/>.
/// </summary>
public class WorkspaceCompatibilityHelperTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _workspaceDir;
    private readonly string _gameInstallDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceCompatibilityHelperTests"/> class.
    /// </summary>
    public WorkspaceCompatibilityHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _workspaceDir = Path.Combine(_tempDir, "workspaces", "test-workspace");
        _gameInstallDir = Path.Combine(_tempDir, "game-install");

        Directory.CreateDirectory(_workspaceDir);
        Directory.CreateDirectory(_gameInstallDir);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    /// <summary>
    /// Verifies that Steam DRM marker directory is created in workspace parent folder on Windows.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_EnsuresDrmMarkerDirectoryInParent()
    {
        // Arrange
        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        // Assert
        if (OperatingSystem.IsWindows())
        {
            var parentDir = Path.GetDirectoryName(_workspaceDir)!;
            var installerDir = Path.Combine(parentDir, GameClientConstants.SteamDrmMarkerDirectory);
            Directory.Exists(installerDir).Should().BeTrue();
        }
    }

    /// <summary>
    /// Verifies that d3d8.dll is materialized into workspace when present in source manifest.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithD3d8Source_MaterializesDll()
    {
        // Arrange
        var d3d8Source = Path.Combine(_gameInstallDir, GameClientConstants.Direct3D8WrapperDll);
        File.WriteAllText(d3d8Source, "test d3d8 content");

        var manifest = new ContentManifest
        {
            Id = "1.104.ea.gameinstallation.zerohour",
            ContentType = ContentType.GameInstallation,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = GameClientConstants.Direct3D8WrapperDll,
                    SourcePath = d3d8Source,
                    InstallTarget = ContentInstallTarget.Workspace,
                },
            ],
        };

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [manifest],
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        // Assert
        if (OperatingSystem.IsWindows())
        {
            var targetDll = Path.Combine(_workspaceDir, GameClientConstants.Direct3D8WrapperDll);
            File.Exists(targetDll).Should().BeTrue();
        }
    }

    /// <summary>
    /// Verifies that d3d8.dll is NOT materialized into workspace when NOT declared in manifests,
    /// even if present in the base game installation directory (protects workspace isolation).
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithoutD3d8InManifest_DoesNotMaterializeDllEvenIfPresentInBaseInstallation()
    {
        // Arrange - base install has d3d8.dll (e.g. GenTool installed in host Steam directory)
        var d3d8Source = Path.Combine(_gameInstallDir, GameClientConstants.Direct3D8WrapperDll);
        File.WriteAllText(d3d8Source, "host gentool d3d8 content");

        // Profile manifest does NOT declare d3d8.dll
        var manifest = new ContentManifest
        {
            Id = "1.23072026.communityoutpost.gameclient.communitypatch",
            ContentType = ContentType.GameClient,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "generalszh.exe",
                    SourcePath = Path.Combine(_gameInstallDir, "generalszh.exe"),
                    InstallTarget = ContentInstallTarget.Workspace,
                },
            ],
        };

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generalszh.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [manifest],
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        // Assert - target d3d8.dll must NOT exist in workspace
        var targetDll = Path.Combine(_workspaceDir, GameClientConstants.Direct3D8WrapperDll);
        File.Exists(targetDll).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that stale d3d8.dll and GenToolUpdater.exe are removed from workspace if manifests do not declare d3d8.dll.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithoutD3d8InManifest_RemovesStaleD3d8AndGenToolFromWorkspace()
    {
        // Arrange - workspace already has stale d3d8.dll and GenToolUpdater.exe
        var staleDll = Path.Combine(_workspaceDir, GameClientConstants.Direct3D8WrapperDll);
        File.WriteAllText(staleDll, "stale d3d8 content");
        var staleGenToolUpdater = Path.Combine(_workspaceDir, GameClientConstants.GenToolUpdaterExe);
        File.WriteAllText(staleGenToolUpdater, "stale gentool updater");

        var manifest = new ContentManifest
        {
            Id = "1.104.steam.gameinstallation.zerohour",
            ContentType = ContentType.GameInstallation,
            Files = [],
        };

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generalszh.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [manifest],
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        // Assert
        File.Exists(staleDll).Should().BeFalse();
        File.Exists(staleGenToolUpdater).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that unrequested d3d8.dll and GenTool files are preserved when SkipCleanup is true.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WhenSkipCleanupIsTrue_PreservesUnrequestedD3d8Files()
    {
        // Arrange - workspace already has d3d8.dll and GenToolUpdater.exe
        var staleDll = Path.Combine(_workspaceDir, GameClientConstants.Direct3D8WrapperDll);
        File.WriteAllText(staleDll, "custom d3d8 content");
        var staleGenToolUpdater = Path.Combine(_workspaceDir, GameClientConstants.GenToolUpdaterExe);
        File.WriteAllText(staleGenToolUpdater, "custom gentool updater");

        var manifest = new ContentManifest
        {
            Id = "1.104.steam.gameinstallation.zerohour",
            ContentType = ContentType.GameInstallation,
            Files = [],
        };

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generalszh.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [manifest],
            SkipCleanup = true,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        // Assert
        File.Exists(staleDll).Should().BeTrue();
        File.Exists(staleGenToolUpdater).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a manifest declaring a subdirectory d3d8.dll does not count as declaring root d3d8.dll,
    /// so stale root d3d8.dll is properly cleaned up.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WhenManifestDeclaresSubdirectoryD3d8_DoesNotTreatAsRootD3d8AndRemovesStaleRootD3d8()
    {
        // Arrange - workspace has stale root d3d8.dll
        var staleDll = Path.Combine(_workspaceDir, GameClientConstants.Direct3D8WrapperDll);
        File.WriteAllText(staleDll, "stale d3d8 content");

        var manifest = new ContentManifest
        {
            Id = "1.104.test.mod.testmod",
            ContentType = ContentType.Mod,
            Files =
            [
                new ManifestFile { RelativePath = "Support/d3d8.dll", Hash = "dummy" },
            ],
        };

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generalszh.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [manifest],
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        // Assert
        File.Exists(staleDll).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that GenToolUpdater.exe is preserved when declared by a manifest, even if d3d8.dll is unrequested.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WhenManifestDeclaresGenToolUpdater_PreservesGenToolUpdater()
    {
        // Arrange - workspace has stale d3d8.dll and GenToolUpdater.exe
        var staleDll = Path.Combine(_workspaceDir, GameClientConstants.Direct3D8WrapperDll);
        File.WriteAllText(staleDll, "stale d3d8 content");
        var genToolUpdaterPath = Path.Combine(_workspaceDir, GameClientConstants.GenToolUpdaterExe);
        File.WriteAllText(genToolUpdaterPath, "declared updater content");

        var manifest = new ContentManifest
        {
            Id = "1.104.test.addon.updater",
            ContentType = ContentType.Addon,
            Files =
            [
                new ManifestFile { RelativePath = GameClientConstants.GenToolUpdaterExe, Hash = "dummy" },
            ],
        };

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generalszh.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [manifest],
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        // Assert - d3d8.dll is removed, but GenToolUpdater.exe is preserved
        File.Exists(staleDll).Should().BeFalse();
        File.Exists(genToolUpdaterPath).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that ZH_Generals source does not crash, throw InvalidOperationException, or hang.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithZhGeneralsSource_DoesNotThrowAndMaterializesOrGracefullySkips()
    {
        // Arrange
        var zhGeneralsSource = Path.Combine(_gameInstallDir, GameClientConstants.ZhGeneralsDirectory);
        Directory.CreateDirectory(zhGeneralsSource);
        File.WriteAllText(Path.Combine(zhGeneralsSource, "game.dat"), "mock dat");

        var manifest = new ContentManifest
        {
            Id = "1.104.ea.gameinstallation.zerohour",
            ContentType = ContentType.GameInstallation,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "generals.exe",
                    SourcePath = Path.Combine(_gameInstallDir, "generals.exe"),
                    InstallTarget = ContentInstallTarget.Workspace,
                },
            ],
        };

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [manifest],
        };

        // Act & Assert - must not throw InvalidOperationException or hang
        var act = () => WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        act.Should().NotThrow();

        if (OperatingSystem.IsWindows())
        {
            Directory.Exists(Path.Combine(_workspaceDir, GameClientConstants.ZhGeneralsDirectory)).Should().BeTrue();
        }
    }

    /// <summary>
    /// Verifies that Core directory source does not crash, throw InvalidOperationException, or hang.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithCoreDirectorySource_DoesNotThrowAndMaterializesOrGracefullySkips()
    {
        // Arrange
        var coreSource = Path.Combine(_gameInstallDir, GameClientConstants.CoreDirectory);
        Directory.CreateDirectory(coreSource);
        File.WriteAllText(Path.Combine(coreSource, "Activation.dll"), "mock dll");

        var manifest = new ContentManifest
        {
            Id = "1.104.ea.gameinstallation.zerohour",
            ContentType = ContentType.GameInstallation,
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "generals.exe",
                    SourcePath = Path.Combine(_gameInstallDir, "generals.exe"),
                    InstallTarget = ContentInstallTarget.Workspace,
                },
            ],
        };

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [manifest],
        };

        // Act & Assert - must not throw InvalidOperationException or hang
        var act = () => WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        act.Should().NotThrow();

        if (OperatingSystem.IsWindows())
        {
            Directory.Exists(Path.Combine(_workspaceDir, GameClientConstants.CoreDirectory)).Should().BeTrue();
        }
    }

    /// <summary>
    /// Verifies that ResolveSourcePath returns absolute SourcePath directly.
    /// </summary>
    [Fact]
    public void ResolveSourcePath_WithRootedSourcePath_ReturnsSourcePath()
    {
        // Arrange
        var rootedSourcePath = Path.Combine(_gameInstallDir, "test.exe");
        var file = new ManifestFile { SourcePath = rootedSourcePath, RelativePath = "test.exe" };
        var manifest = new ContentManifest();
        var config = new WorkspaceConfiguration { BaseInstallationPath = _gameInstallDir };

        // Act
        var result = WorkspaceCompatibilityHelper.ResolveSourcePath(file, manifest, config);

        // Assert
        result.Should().Be(rootedSourcePath);
    }

    /// <summary>
    /// Verifies that ResolveSourcePath uses manifest-specific source path mapping.
    /// </summary>
    [Fact]
    public void ResolveSourcePath_WithManifestSourcePath_UsesManifestDirectory()
    {
        // Arrange
        const string manifestId = "1.0.test.gameclient.testclient";
        var customSourceDir = Path.Combine(_tempDir, "custom-source");
        var relativeFilePath = Path.Combine("sub", "test.exe");
        var file = new ManifestFile { RelativePath = relativeFilePath };
        var manifest = new ContentManifest { Id = manifestId };
        var config = new WorkspaceConfiguration
        {
            BaseInstallationPath = _gameInstallDir,
            ManifestSourcePaths = new Dictionary<string, string>
            {
                [manifestId] = customSourceDir,
            },
        };

        // Act
        var result = WorkspaceCompatibilityHelper.ResolveSourcePath(file, manifest, config);

        // Assert
        result.Should().Be(Path.Combine(customSourceDir, relativeFilePath));
    }

    /// <summary>
    /// Verifies that a supplemental archive root materializes its top-level archives into the
    /// workspace root, matching archive extensions case-insensitively and ignoring subdirectories
    /// and non-archive files.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithSupplementalRoot_LinksTopLevelArchives()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        Directory.CreateDirectory(Path.Combine(supplementalRoot, "Data"));
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");
        File.WriteAllText(Path.Combine(supplementalRoot, "Models.BIG"), "generals models");
        File.WriteAllText(Path.Combine(supplementalRoot, "Data", "Nested.big"), "nested archive");
        File.WriteAllText(Path.Combine(supplementalRoot, "readme.txt"), "not an archive");

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
            FileCount = 5,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        File.ReadAllText(Path.Combine(_workspaceDir, "Textures.big")).Should().Be("generals textures");
        File.ReadAllText(Path.Combine(_workspaceDir, "Models.BIG")).Should().Be("generals models");
        File.Exists(Path.Combine(_workspaceDir, "Nested.big")).Should().BeFalse();
        File.Exists(Path.Combine(_workspaceDir, "readme.txt")).Should().BeFalse();
        workspaceInfo.FileCount.Should().Be(7);

        // Act: a second run (workspace reuse) must not count the same links again.
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        workspaceInfo.FileCount.Should().Be(7);
    }

    /// <summary>
    /// Verifies that existing workspace entries always win over supplemental archives sharing
    /// their name, so Zero Hour and mod content is never shadowed by base Generals files.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithSupplementalRoot_SkipsExistingEntries()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");
        File.WriteAllText(Path.Combine(_workspaceDir, "Textures.big"), "zero hour textures");

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
            FileCount = 1,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        File.ReadAllText(Path.Combine(_workspaceDir, "Textures.big")).Should().Be("zero hour textures");
        workspaceInfo.FileCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies that an existing workspace entry wins over a supplemental archive even when
    /// their casing differs, instead of gaining a second entry on case-sensitive filesystems.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithSupplementalRoot_SkipsCaseVariantEntries()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");
        File.WriteAllText(Path.Combine(_workspaceDir, "textures.big"), "zero hour textures");

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
            FileCount = 1,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        File.ReadAllText(Path.Combine(_workspaceDir, "textures.big")).Should().Be("zero hour textures");
        Directory.GetFiles(_workspaceDir).Should().HaveCount(1);
        workspaceInfo.FileCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies that a supplemental link whose workspace casing differs from its source is
    /// repaired to the canonical source path rather than a casing-reconstructed one.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithSupplementalRoot_RelinksCaseVariantOwnLinksToCanonicalSource()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");
        File.WriteAllText(Path.Combine(supplementalRoot, "B.big"), "archive b");

        var workspaceLink = Path.Combine(_workspaceDir, "textures.big");
        if (!SymlinkTestHelper.TryCreateFileSymlink(workspaceLink, Path.Combine(supplementalRoot, "B.big")))
        {
            return;
        }

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        new FileInfo(workspaceLink).LinkTarget.Should().Be(Path.Combine(supplementalRoot, "Textures.big"));
        File.ReadAllText(workspaceLink).Should().Be("generals textures");
    }

    /// <summary>
    /// Verifies that no supplemental content is materialized when the resolved workspace
    /// executable is a native binary, which resolves its archive roots from the environment.
    /// The configured client deliberately declares a Windows executable so the test pins that
    /// the workspace-resolved path takes precedence over the client fallback.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithSupplementalRootAndNativeExecutable_SkipsLinking()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generalszh"),
            FileCount = 3,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            GameClient = new GameClient { ExecutablePath = "generals.exe" },
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        Directory.GetFiles(_workspaceDir).Should().BeEmpty();
        workspaceInfo.FileCount.Should().Be(3);
    }

    /// <summary>
    /// Verifies that an empty workspace executable falls back to the configured game client's
    /// executable when deciding whether supplemental archives apply.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithEmptyWorkspaceExecutable_FallsBackToClientExecutable()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            GameClient = new GameClient { ExecutablePath = "generals.exe" },
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        File.ReadAllText(Path.Combine(_workspaceDir, "Textures.big")).Should().Be("generals textures");
    }

    /// <summary>
    /// Verifies that no supplemental content is materialized when no root is configured.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithoutSupplementalRoot_CreatesNothing()
    {
        // Arrange
        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        Directory.GetFiles(_workspaceDir).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that links pointing into the supplemental root are removed once the root no
    /// longer provides their name.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithSupplementalRoot_RemovesStaleLinks()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "New.big"), "current archive");

        if (!SymlinkTestHelper.TryCreateFileSymlink(
            Path.Combine(_workspaceDir, "Old.big"),
            Path.Combine(supplementalRoot, "Old.big")))
        {
            return;
        }

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        Directory.GetFiles(_workspaceDir).Should().BeEquivalentTo(Path.Combine(_workspaceDir, "New.big"));
    }

    /// <summary>
    /// Verifies that links owned by manifests, mods, or the user are never touched even when
    /// they share a supplemental archive's name.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithSupplementalRoot_LeavesForeignLinksAlone()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");

        var modDir = Path.Combine(_tempDir, "mod");
        Directory.CreateDirectory(modDir);
        var modArchive = Path.Combine(modDir, "Textures.big");
        File.WriteAllText(modArchive, "mod textures");

        var workspaceLink = Path.Combine(_workspaceDir, "Textures.big");
        if (!SymlinkTestHelper.TryCreateFileSymlink(workspaceLink, modArchive))
        {
            return;
        }

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        new FileInfo(workspaceLink).LinkTarget.Should().Be(modArchive);
        File.ReadAllText(workspaceLink).Should().Be("mod textures");
    }

    /// <summary>
    /// Verifies that a supplemental link pointing at the wrong file inside its own root is
    /// repaired to the expected archive.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithSupplementalRoot_RelinksMismatchedOwnLinks()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "A.big"), "archive a");
        File.WriteAllText(Path.Combine(supplementalRoot, "B.big"), "archive b");

        var workspaceLink = Path.Combine(_workspaceDir, "A.big");
        if (!SymlinkTestHelper.TryCreateFileSymlink(workspaceLink, Path.Combine(supplementalRoot, "B.big")))
        {
            return;
        }

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        new FileInfo(workspaceLink).LinkTarget.Should().Be(Path.Combine(supplementalRoot, "A.big"));
        File.ReadAllText(workspaceLink).Should().Be("archive a");
    }

    /// <summary>
    /// Verifies that a missing supplemental root enumerates as an empty set rather than an error.
    /// </summary>
    [Fact]
    public void TryGetSupplementalArchives_WithMissingDirectory_ReturnsTrueAndEmpty()
    {
        // Act
        var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchives(
            Path.Combine(_tempDir, "does-not-exist"),
            out var archives);

        // Assert
        result.Should().BeTrue();
        archives.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a null supplemental root enumerates as an empty set.
    /// </summary>
    [Fact]
    public void TryGetSupplementalArchives_WithNullRoot_ReturnsTrueAndEmpty()
    {
        // Act
        var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchives(null, out var archives);

        // Assert
        result.Should().BeTrue();
        archives.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that archives enumerate with their on-disk spelling mapped to canonical source
    /// paths while comparing case-insensitively.
    /// </summary>
    [Fact]
    public void TryGetSupplementalArchives_WithArchives_ReturnsCaseInsensitiveSources()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        Directory.CreateDirectory(Path.Combine(supplementalRoot, "Data"));
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");
        File.WriteAllText(Path.Combine(supplementalRoot, "Models.BIG"), "generals models");
        File.WriteAllText(Path.Combine(supplementalRoot, "Data", "Nested.big"), "nested archive");

        // Act
        var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchives(supplementalRoot, out var archives);

        // Assert
        result.Should().BeTrue();
        archives.Keys.Should().BeEquivalentTo("Textures.big", "Models.BIG");
        archives.ContainsKey("textures.BIG").Should().BeTrue();
        archives["textures.big"].Should().Be(Path.Combine(supplementalRoot, "Textures.big"));
    }

    /// <summary>
    /// Verifies link ownership detection: only absolute targets directly inside the root match,
    /// so manifest, mod, and user links are never mistaken for supplemental content.
    /// </summary>
    /// <param name="targetKind">How the link target relates to the root.</param>
    /// <param name="expected">The expected ownership verdict.</param>
    [Theory]
    [InlineData("inside", true)]
    [InlineData("inside-trailing-separator", true)]
    [InlineData("elsewhere", false)]
    [InlineData("relative", false)]
    [InlineData("null", false)]
    public void IsLinkTargetUnderRoot_ClassifiesOwnershipCorrectly(string targetKind, bool expected)
    {
        // Arrange
        var root = Path.Combine(_tempDir, "generals");
        var linkTarget = targetKind switch
        {
            "inside" => Path.Combine(root, "Textures.big"),
            "inside-trailing-separator" => Path.Combine(root, "Textures.big"),
            "elsewhere" => Path.Combine(_tempDir, "other", "Textures.big"),
            "relative" => Path.Combine("..", "generals", "Textures.big"),
            _ => null,
        };
        var effectiveRoot = targetKind == "inside-trailing-separator" ? root + Path.DirectorySeparatorChar : root;

        // Act
        var result = WorkspaceCompatibilityHelper.IsLinkTargetUnderRoot(linkTarget, effectiveRoot);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies that an unreadable supplemental root fails enumeration so the caller warns
    /// instead of silently launching without the archives.
    /// </summary>
    [Fact]
    public void TryGetSupplementalArchives_WithUnreadableRoot_ReturnsFalse()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || Environment.UserName == "root")
        {
            return;
        }

        // Arrange
        var parent = Directory.CreateDirectory(Path.Combine(_tempDir, "locked")).FullName;
        var root = Directory.CreateDirectory(Path.Combine(parent, "generals")).FullName;
        File.WriteAllText(Path.Combine(root, "Textures.big"), "generals textures");
        File.SetUnixFileMode(parent, UnixFileMode.UserWrite);

        try
        {
            // Act
            var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchives(root, out var archives);

            // Assert
            result.Should().BeFalse();
            archives.Should().BeEmpty();
        }
        finally
        {
            File.SetUnixFileMode(
                parent,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Verifies that case-variant duplicates in the root resolve to a deterministic canonical
    /// source instead of whichever entry the filesystem enumerates first.
    /// </summary>
    [Fact]
    public void TryGetSupplementalArchives_WithCaseVariantDuplicates_ResolvesDeterministically()
    {
        // Arrange
        var root = Directory.CreateDirectory(Path.Combine(_tempDir, "duplicates")).FullName;
        File.WriteAllText(Path.Combine(root, "textures.big"), "lowercase");
        File.WriteAllText(Path.Combine(root, "Textures.big"), "capitalized");

        if (Directory.GetFiles(root).Length != 2)
        {
            // A case-insensitive volume cannot hold both variants, so the scenario is unreachable here.
            return;
        }

        // Act
        var first = WorkspaceCompatibilityHelper.TryGetSupplementalArchives(root, out var firstArchives);
        var second = WorkspaceCompatibilityHelper.TryGetSupplementalArchives(root, out var secondArchives);

        // Assert
        first.Should().BeTrue();
        second.Should().BeTrue();
        firstArchives.Count.Should().Be(1);
        secondArchives.Count.Should().Be(1);
        firstArchives["textures.big"].Should().Be(secondArchives["textures.big"]);
        Path.GetFileName(firstArchives["textures.big"]).Should().Be("Textures.big");
    }

    /// <summary>
    /// Verifies that conflicting base Generals archives (INI, Patch, Window, Shader, SafeDisc gensec, ZH, Language archives)
    /// are excluded from supplemental archives to prevent breaking Zero Hour game data and crashing the engine.
    /// </summary>
    [Fact]
    public void TryGetSupplementalArchives_ExcludesConflictingArchives()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals-conflicts");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "safe");
        File.WriteAllText(Path.Combine(supplementalRoot, "W3D.big"), "safe");
        File.WriteAllText(Path.Combine(supplementalRoot, "Terrain.big"), "safe");
        File.WriteAllText(Path.Combine(supplementalRoot, "AudioEnglish.big"), "safe");
        File.WriteAllText(Path.Combine(supplementalRoot, "SpeechEnglish.big"), "safe");
        File.WriteAllText(Path.Combine(supplementalRoot, "maps.big"), "safe");
        File.WriteAllText(Path.Combine(supplementalRoot, "English.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "German.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "INI.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "PatchINI.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "Patch.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "PatchData.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "PatchWindow.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "Window.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "shaders.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "gensec.big"), "unsafe");
        File.WriteAllText(Path.Combine(supplementalRoot, "GeneralsZH.big"), "unsafe");

        // Act
        var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchives(supplementalRoot, out var archives);

        // Assert
        result.Should().BeTrue();
        archives.Keys.Should().BeEquivalentTo(
            "Textures.big",
            "W3D.big",
            "Terrain.big",
            "AudioEnglish.big",
            "SpeechEnglish.big",
            "maps.big");
    }

    /// <summary>
    /// Verifies that base language archives containing generals.csf are flagged unsafe by IsSafeSupplementalArchive,
    /// while audio and speech language archives remain safe to link.
    /// </summary>
    /// <param name="fileName">The archive file name under test.</param>
    /// <param name="expectedSafe">Whether the archive is expected to be considered safe.</param>
    [Theory]
    [InlineData("English.big", false)]
    [InlineData("english.big", false)]
    [InlineData("German.big", false)]
    [InlineData("german.big", false)]
    [InlineData("French.big", false)]
    [InlineData("Spanish.big", false)]
    [InlineData("Italian.big", false)]
    [InlineData("Korean.big", false)]
    [InlineData("Polish.big", false)]
    [InlineData("Chinese.big", false)]
    [InlineData("ChineseTraditional.big", false)]
    [InlineData("Brazilian.big", false)]
    [InlineData("Russian.big", false)]
    [InlineData("AudioEnglish.big", true)]
    [InlineData("SpeechEnglish.big", true)]
    [InlineData("AudioGerman.big", true)]
    [InlineData("SpeechGerman.big", true)]
    public void IsSafeSupplementalArchive_LanguageArchives_IdentifiedCorrectly(string fileName, bool expectedSafe)
    {
        WorkspaceCompatibilityHelper.IsSafeSupplementalArchive(fileName).Should().Be(expectedSafe);
    }

    /// <summary>
    /// Verifies that previously linked base language archives (e.g. English.big)
    /// are purged as stale links during reconciliation so Zero Hour strings are restored.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_PurgesConflictingLanguageSupplementalLinks()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals-lang-purge");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "safe textures");
        File.WriteAllText(Path.Combine(supplementalRoot, "English.big"), "unsafe english csf");

        var englishLink = Path.Combine(_workspaceDir, "English.big");
        if (!SymlinkTestHelper.TryCreateFileSymlink(englishLink, Path.Combine(supplementalRoot, "English.big")))
        {
            return;
        }

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
            FileCount = 10,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        File.Exists(englishLink).Should().BeFalse();
        File.Exists(Path.Combine(_workspaceDir, "Textures.big")).Should().BeTrue();
        workspaceInfo.FileCount.Should().Be(10); // 1 removed (English.big), 1 created (Textures.big)
    }

    /// <summary>
    /// Verifies that deep archive inspection catches archives with non-standard names
    /// that contain string tables (.csf), window definitions (.wnd), or INIs (.ini).
    /// </summary>
    [Fact]
    public void IsSafeSupplementalArchive_DeepArchiveInspection_ExcludesArchiveContainingCsfOrWndOrIni()
    {
        var tempBigDir = Path.Combine(_tempDir, "deep-inspection");
        Directory.CreateDirectory(tempBigDir);

        var safeBig = Path.Combine(tempBigDir, "CustomModels.big");
        CreateDummyBigArchive(safeBig, "Data/Art/Model.w3d", "Data/Art/Texture.dds");

        var csfBig = Path.Combine(tempBigDir, "CustomStrings.big");
        CreateDummyBigArchive(csfBig, "Data/English/generals.csf");

        var wndBig = Path.Combine(tempBigDir, "CustomUI.big");
        CreateDummyBigArchive(wndBig, "Data/English/GUI.wnd");

        var iniBig = Path.Combine(tempBigDir, "CustomRules.big");
        CreateDummyBigArchive(iniBig, "Data/INI/GameData.ini");

        WorkspaceCompatibilityHelper.IsSafeSupplementalArchive(safeBig).Should().BeTrue();
        WorkspaceCompatibilityHelper.IsSafeSupplementalArchive(csfBig).Should().BeFalse();
        WorkspaceCompatibilityHelper.IsSafeSupplementalArchive(wndBig).Should().BeFalse();
        WorkspaceCompatibilityHelper.IsSafeSupplementalArchive(iniBig).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that previously linked conflicting archives (like PatchINI.big or gensec.big)
    /// are purged as stale links during reconciliation.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_PurgesConflictingSupplementalLinks()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals-purge");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "safe textures");
        File.WriteAllText(Path.Combine(supplementalRoot, "PatchINI.big"), "unsafe patch ini");
        File.WriteAllText(Path.Combine(supplementalRoot, "gensec.big"), "unsafe gensec");

        var patchIniLink = Path.Combine(_workspaceDir, "PatchINI.big");
        var gensecLink = Path.Combine(_workspaceDir, "gensec.big");
        if (!SymlinkTestHelper.TryCreateFileSymlink(patchIniLink, Path.Combine(supplementalRoot, "PatchINI.big")) ||
            !SymlinkTestHelper.TryCreateFileSymlink(gensecLink, Path.Combine(supplementalRoot, "gensec.big")))
        {
            return;
        }

        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
            FileCount = 10,
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
            SupplementalArchiveRoot = supplementalRoot,
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(workspaceInfo, config, NullLogger.Instance);

        // Assert
        File.Exists(patchIniLink).Should().BeFalse();
        File.Exists(gensecLink).Should().BeFalse();
        File.Exists(Path.Combine(_workspaceDir, "Textures.big")).Should().BeTrue();
        workspaceInfo.FileCount.Should().Be(9);
    }

    /// <summary>
    /// Verifies that deliberately skipped archives are logged at information level so a root
    /// holding an excluded archive leaves a trace in default logs instead of silently
    /// missing content. A skip deliberately costs content, so debug level would hide the
    /// names a missing-content report needs.
    /// </summary>
    [Fact]
    public void TryGetSupplementalArchives_UnsafeArchive_LogsSkippedCandidate()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals-logged-skips");
        Directory.CreateDirectory(supplementalRoot);
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "safe");
        File.WriteAllText(Path.Combine(supplementalRoot, "PatchINI.big"), "unsafe");
        var mockLogger = new Mock<ILogger>();

        // Act
        var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchives(supplementalRoot, out var archives, mockLogger.Object);

        // Assert
        result.Should().BeTrue();
        archives.Keys.Should().BeEquivalentTo("Textures.big");
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("PatchINI.big")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that Steam DRM marker directory is created in workspace parent folder when targeting Windows executable.
    /// </summary>
    [Fact]
    public void EnsureDrmAndAssetCompatibility_WithWindowsExecutableTarget_CreatesDrmMarker()
    {
        // Arrange
        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generals.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [],
        };

        // Act
        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);

        // Assert
        var parentDir = Path.GetDirectoryName(_workspaceDir)!;
        var installerDir = Path.Combine(parentDir, GameClientConstants.SteamDrmMarkerDirectory);
        Directory.Exists(installerDir).Should().BeTrue();
    }

    private void RunCompatibilityScenario(ContentManifest manifest, bool skipCleanup = false)
    {
        var workspaceInfo = new WorkspaceInfo
        {
            Id = "test-workspace",
            WorkspacePath = _workspaceDir,
            ExecutablePath = Path.Combine(_workspaceDir, "generalszh.exe"),
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            BaseInstallationPath = _gameInstallDir,
            Manifests = [manifest],
            SkipCleanup = skipCleanup,
        };

        WorkspaceCompatibilityHelper.EnsureDrmAndAssetCompatibility(
            workspaceInfo,
            config,
            NullLogger.Instance);
    }

    private void CreateDummyBigArchive(string filePath, params string[] entryPaths)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        int dirSize = entryPaths.Sum(p => 8 + Encoding.ASCII.GetByteCount(p) + 1);
        int headerSize = 16 + dirSize;
        int dummyDataOffset = headerSize;
        const int dummyDataSize = 4;
        int totalFileSize = headerSize + (entryPaths.Length * dummyDataSize);

        // Magic "BIGF"
        writer.Write([(byte)'B', (byte)'I', (byte)'G', (byte)'F']);

        // File length: little-endian 4 bytes
        writer.Write(totalFileSize);

        // Entry count: big-endian 4 bytes
        var countBytes = BitConverter.GetBytes((uint)entryPaths.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(countBytes);
        }

        writer.Write(countBytes);

        // Header size: big-endian 4 bytes
        var headerSizeBytes = BitConverter.GetBytes((uint)headerSize);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(headerSizeBytes);
        }

        writer.Write(headerSizeBytes);

        // Directory entries
        int currentDataOffset = dummyDataOffset;
        foreach (var entryPath in entryPaths)
        {
            var offsetBytes = BitConverter.GetBytes((uint)currentDataOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(offsetBytes);
            }

            writer.Write(offsetBytes);

            var sizeBytes = BitConverter.GetBytes((uint)dummyDataSize);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(sizeBytes);
            }

            writer.Write(sizeBytes);

            writer.Write(Encoding.ASCII.GetBytes(entryPath));
            writer.Write((byte)0); // null terminator

            currentDataOffset += dummyDataSize;
        }

        // Dummy data for each entry
        for (int i = 0; i < entryPaths.Length; i++)
        {
            writer.Write(new byte[] { 1, 2, 3, 4 });
        }

        File.WriteAllBytes(filePath, ms.ToArray());
    }
}
