using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging.Abstractions;
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
            Directory.Exists(Path.Combine(_workspaceDir, GameClientConstants.CoreDirectory)).Should().BeTrue();
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
}
