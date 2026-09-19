using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace.Strategies;
using GenHub.Tests.Core.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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
    public void TryGetSupplementalArchiveNames_WithMissingDirectory_ReturnsTrueAndEmpty()
    {
        // Act
        var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchiveNames(
            Path.Combine(_tempDir, "does-not-exist"),
            out var names);

        // Assert
        result.Should().BeTrue();
        names.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a null supplemental root enumerates as an empty set.
    /// </summary>
    [Fact]
    public void TryGetSupplementalArchiveNames_WithNullRoot_ReturnsTrueAndEmpty()
    {
        // Act
        var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchiveNames(null, out var names);

        // Assert
        result.Should().BeTrue();
        names.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that archive names enumerate with their on-disk spelling while comparing
    /// case-insensitively.
    /// </summary>
    [Fact]
    public void TryGetSupplementalArchiveNames_WithArchives_ReturnsCaseInsensitiveNames()
    {
        // Arrange
        var supplementalRoot = Path.Combine(_tempDir, "generals");
        Directory.CreateDirectory(supplementalRoot);
        Directory.CreateDirectory(Path.Combine(supplementalRoot, "Data"));
        File.WriteAllText(Path.Combine(supplementalRoot, "Textures.big"), "generals textures");
        File.WriteAllText(Path.Combine(supplementalRoot, "Models.BIG"), "generals models");
        File.WriteAllText(Path.Combine(supplementalRoot, "Data", "Nested.big"), "nested archive");

        // Act
        var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchiveNames(supplementalRoot, out var names);

        // Assert
        result.Should().BeTrue();
        names.Should().BeEquivalentTo("Textures.big", "Models.BIG");
        names.Contains("textures.BIG").Should().BeTrue();
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
    public void TryGetSupplementalArchiveNames_WithUnreadableRoot_ReturnsFalse()
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
            var result = WorkspaceCompatibilityHelper.TryGetSupplementalArchiveNames(root, out var names);

            // Assert
            result.Should().BeFalse();
            names.Should().BeEmpty();
        }
        finally
        {
            File.SetUnixFileMode(
                parent,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
