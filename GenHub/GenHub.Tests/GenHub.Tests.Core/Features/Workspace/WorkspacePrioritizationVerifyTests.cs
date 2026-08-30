using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Verification tests for workspace file prioritization logic and user directory workspace isolation.
/// </summary>
public class WorkspacePrioritizationVerifyTests
{
    /// <summary>
    /// Verifies that game client files are prioritized over installation files when they have the same relative path.
    /// </summary>
    [Fact]
    public void GetAllUniqueFiles_ShouldPrioritizeGameClientOverInstallation()
    {
        // Arrange
        var lowPriorityFile = new ManifestFile { RelativePath = "data.ini", Size = 100, SourcePath = "install" };
        var highPriorityFile = new ManifestFile { RelativePath = "data.ini", Size = 150, SourcePath = "client" };

        var installationManifest = new ContentManifest
        {
            Id = new ManifestId("install"),
            ContentType = ContentType.GameInstallation,
            Files = [lowPriorityFile],
        };

        var clientManifest = new ContentManifest
        {
            Id = new ManifestId("client"),
            ContentType = ContentType.GameClient,
            Files = [highPriorityFile],
        };

        var config = new WorkspaceConfiguration
        {
            Manifests = [installationManifest, clientManifest],
        };

        // Act
        var result = config.GetAllUniqueFiles().ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(150, result[0].Size);
        Assert.Equal("client", result[0].SourcePath);
    }

    /// <summary>
    /// Verifies that high-priority content (such as mods) correctly overwrites low-priority content (such as installations),
    /// regardless of whether the installation or the mod appears first in the manifests list.
    /// </summary>
    /// <param name="modFirst">Whether the mod manifest appears first in the collection.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetAllUniqueFiles_ShouldPrioritizeHighPriorityContent_RegardlessOfOrder(bool modFirst)
    {
        // Arrange
        var lowPriorityFile = new ManifestFile { RelativePath = "config.ini", Size = 100, SourcePath = "low" };
        var highPriorityFile = new ManifestFile { RelativePath = "config.ini", Size = 200, SourcePath = "high" };

        var installationManifest = new ContentManifest
        {
            Id = new ManifestId("install"),
            ContentType = ContentType.GameInstallation,
            Files = [lowPriorityFile],
        };

        var modManifest = new ContentManifest
        {
            Id = new ManifestId("mod"),
            ContentType = ContentType.Mod,
            Files = [highPriorityFile],
        };

        var config = new WorkspaceConfiguration
        {
            Manifests = modFirst ? [modManifest, installationManifest] : [installationManifest, modManifest],
        };

        // Act
        var uniqueFiles = config.GetAllUniqueFiles().ToList();

        // Assert
        Assert.Single(uniqueFiles);
        var chosenFile = uniqueFiles[0];

        // Should always be the mod file (size 200)
        Assert.Equal(200, chosenFile.Size);
        Assert.Equal("high", chosenFile.SourcePath);
    }

    /// <summary>
    /// Verifies that GetPrioritizedWorkspaceFiles returns the winning file and manifest pair for all content types.
    /// </summary>
    [Fact]
    public void GetPrioritizedWorkspaceFiles_FullHierarchy_ResolvesCorrectWinningManifests()
    {
        // Arrange: Mod (100) > Patch (90) > GameClient (50) > ModdingTool (45) > Addon (40) > LanguagePack (35) > Map (30) > GameInstallation (10)
        var fileMod = new ManifestFile { RelativePath = "shared.ini", Size = 1000, InstallTarget = ContentInstallTarget.Workspace };
        var filePatch = new ManifestFile { RelativePath = "shared.ini", Size = 900, InstallTarget = ContentInstallTarget.Workspace };
        var fileClient = new ManifestFile { RelativePath = "shared.ini", Size = 500, InstallTarget = ContentInstallTarget.Workspace };
        var fileInstall = new ManifestFile { RelativePath = "shared.ini", Size = 100, InstallTarget = ContentInstallTarget.Workspace };

        var manifestMod = new ContentManifest { Id = new ManifestId("mod"), ContentType = ContentType.Mod, Files = [fileMod] };
        var manifestPatch = new ContentManifest { Id = new ManifestId("patch"), ContentType = ContentType.Patch, Files = [filePatch] };
        var manifestClient = new ContentManifest { Id = new ManifestId("client"), ContentType = ContentType.GameClient, Files = [fileClient] };
        var manifestInstall = new ContentManifest { Id = new ManifestId("install"), ContentType = ContentType.GameInstallation, Files = [fileInstall] };

        // Test with installation first
        var config1 = new WorkspaceConfiguration
        {
            Manifests = [manifestInstall, manifestClient, manifestPatch, manifestMod],
        };

        var result1 = config1.GetPrioritizedWorkspaceFiles();
        Assert.Single(result1);
        Assert.Equal("mod", result1[0].Manifest.Id.Value);
        Assert.Equal(1000, result1[0].File.Size);

        // Test with patch beating client and install
        var config2 = new WorkspaceConfiguration
        {
            Manifests = [manifestInstall, manifestPatch, manifestClient],
        };

        var result2 = config2.GetPrioritizedWorkspaceFiles();
        Assert.Single(result2);
        Assert.Equal("patch", result2[0].Manifest.Id.Value);
        Assert.Equal(900, result2[0].File.Size);
    }

    /// <summary>
    /// Verifies that GetPrioritizedWorkspaceFiles filters out files not targeted to the workspace.
    /// </summary>
    [Fact]
    public void GetPrioritizedWorkspaceFiles_FiltersNonWorkspaceInstallTargets()
    {
        // Arrange
        var workspaceFile = new ManifestFile { RelativePath = "game.exe", Size = 1000, InstallTarget = ContentInstallTarget.Workspace };
        var userDataFile = new ManifestFile { RelativePath = "options.ini", Size = 200, InstallTarget = ContentInstallTarget.UserData };

        var manifest = new ContentManifest
        {
            Id = new ManifestId("test"),
            ContentType = ContentType.Mod,
            Files = [workspaceFile, userDataFile],
        };

        var config = new WorkspaceConfiguration
        {
            Manifests = [manifest],
        };

        // Act
        var result = config.GetPrioritizedWorkspaceFiles();

        // Assert
        Assert.Single(result);
        Assert.Equal("game.exe", result[0].File.RelativePath);
    }

    /// <summary>
    /// Verifies that FullCopyStrategy materializes custom files with priority over base installation files in user workspaces.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task FullCopyStrategy_CustomFileOverInstallation_CopiesWinningFileAsync()
    {
        // Arrange
        var tempBase = Path.Combine(Path.GetTempPath(), $"genhub_base_{Guid.NewGuid():N}");
        var tempUserWorkspace = Path.Combine(Path.GetTempPath(), $"genhub_userws_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBase);
        Directory.CreateDirectory(tempUserWorkspace);

        try
        {
            var baseFilePath = Path.Combine(tempBase, "GameData.ini");
            var modFilePath = Path.Combine(tempBase, "ModGameData.ini");
            var unmoddedBaseFilePath = Path.Combine(tempBase, "Generals.big");

            await File.WriteAllTextAsync(baseFilePath, "BASE GAME CONTENT");
            await File.WriteAllTextAsync(modFilePath, "CUSTOM MOD CONTENT");
            await File.WriteAllTextAsync(unmoddedBaseFilePath, "UNTOUCHED BIG FILE");

            var baseFile = new ManifestFile { RelativePath = "GameData.ini", SourcePath = baseFilePath, Size = 17, InstallTarget = ContentInstallTarget.Workspace };
            var modFile = new ManifestFile { RelativePath = "GameData.ini", SourcePath = modFilePath, Size = 18, InstallTarget = ContentInstallTarget.Workspace };
            var unchangedFile = new ManifestFile { RelativePath = "Generals.big", SourcePath = unmoddedBaseFilePath, Size = 18, InstallTarget = ContentInstallTarget.Workspace };

            var installManifest = new ContentManifest { Id = new ManifestId("install"), ContentType = ContentType.GameInstallation, Files = [baseFile, unchangedFile] };
            var modManifest = new ContentManifest { Id = new ManifestId("mod"), ContentType = ContentType.Mod, Files = [modFile] };

            var config = new WorkspaceConfiguration
            {
                Id = "test-ws-priority",
                WorkspaceRootPath = tempUserWorkspace,
                BaseInstallationPath = tempBase,
                Strategy = WorkspaceStrategy.FullCopy,
                GameClient = new GameClient { Id = "test-client", ExecutablePath = "game.exe" },
                Manifests = [installManifest, modManifest],
            };

            var mockFileOps = new Mock<IFileOperationsService>();
            mockFileOps.Setup(f => f.CopyFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, CancellationToken>(async (src, dst, ct) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src, dst, true);
                    await Task.CompletedTask;
                });

            var strategy = new FullCopyStrategy(mockFileOps.Object, NullLogger<FullCopyStrategy>.Instance);

            // Act
            var workspaceInfo = await strategy.PrepareAsync(config);

            // Assert
            Assert.True(workspaceInfo.IsPrepared);
            var destGameData = Path.Combine(workspaceInfo.WorkspacePath, "GameData.ini");
            var destBig = Path.Combine(workspaceInfo.WorkspacePath, "Generals.big");

            Assert.True(File.Exists(destGameData));
            Assert.True(File.Exists(destBig));

            // Custom file content should be the mod content, not base game content
            var gameDataContent = await File.ReadAllTextAsync(destGameData);
            Assert.Equal("CUSTOM MOD CONTENT", gameDataContent);

            // Base game directory files must be completely unmodified
            Assert.Equal("BASE GAME CONTENT", await File.ReadAllTextAsync(baseFilePath));
            Assert.Equal("UNTOUCHED BIG FILE", await File.ReadAllTextAsync(unmoddedBaseFilePath));
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, true);
            }

            if (Directory.Exists(tempUserWorkspace))
            {
                Directory.Delete(tempUserWorkspace, true);
            }
        }
    }

    /// <summary>
    /// Verifies that HybridCopySymlinkStrategy correctly prioritizes custom files over installation data,
    /// even when the mod manifest is placed before or after the installation manifest.
    /// </summary>
    /// <param name="modFirst">Whether the mod manifest appears first in the collection.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HybridCopySymlinkStrategy_CustomFileOverInstallation_PrioritizesCorrectlyAsync(bool modFirst)
    {
        // Arrange
        var tempBase = Path.Combine(Path.GetTempPath(), $"genhub_hybrid_base_{Guid.NewGuid():N}");
        var tempUserWorkspace = Path.Combine(Path.GetTempPath(), $"genhub_hybrid_userws_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBase);
        Directory.CreateDirectory(tempUserWorkspace);

        try
        {
            var baseFilePath = Path.Combine(tempBase, "config.ini");
            var modFilePath = Path.Combine(tempBase, "mod_config.ini");

            await File.WriteAllTextAsync(baseFilePath, "BASE CONFIG");
            await File.WriteAllTextAsync(modFilePath, "MOD CONFIG");

            var baseFile = new ManifestFile { RelativePath = "config.ini", SourcePath = baseFilePath, Size = 11, InstallTarget = ContentInstallTarget.Workspace };
            var modFile = new ManifestFile { RelativePath = "config.ini", SourcePath = modFilePath, Size = 10, InstallTarget = ContentInstallTarget.Workspace };

            var installManifest = new ContentManifest { Id = new ManifestId("install"), ContentType = ContentType.GameInstallation, Files = [baseFile] };
            var modManifest = new ContentManifest { Id = new ManifestId("mod"), ContentType = ContentType.Mod, Files = [modFile] };

            var config = new WorkspaceConfiguration
            {
                Id = "test-ws-hybrid",
                WorkspaceRootPath = tempUserWorkspace,
                BaseInstallationPath = tempBase,
                Strategy = WorkspaceStrategy.HybridCopySymlink,
                GameClient = new GameClient { Id = "test-client", ExecutablePath = "game.exe" },
                Manifests = modFirst ? [modManifest, installManifest] : [installManifest, modManifest],
            };

            var mockFileOps = new Mock<IFileOperationsService>();
            mockFileOps.Setup(f => f.CopyFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, CancellationToken>(async (src, dst, ct) =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(src, dst, true);
                    await Task.CompletedTask;
                });

            var strategy = new HybridCopySymlinkStrategy(mockFileOps.Object, NullLogger<HybridCopySymlinkStrategy>.Instance);

            // Act
            var workspaceInfo = await strategy.PrepareAsync(config);

            // Assert
            Assert.True(workspaceInfo.IsPrepared);
            var destConfig = Path.Combine(workspaceInfo.WorkspacePath, "config.ini");
            Assert.True(File.Exists(destConfig));

            var configContent = await File.ReadAllTextAsync(destConfig);
            Assert.Equal("MOD CONFIG", configContent);

            // Original base file remains completely untouched
            Assert.Equal("BASE CONFIG", await File.ReadAllTextAsync(baseFilePath));
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                Directory.Delete(tempBase, true);
            }

            if (Directory.Exists(tempUserWorkspace))
            {
                Directory.Delete(tempUserWorkspace, true);
            }
        }
    }
}

