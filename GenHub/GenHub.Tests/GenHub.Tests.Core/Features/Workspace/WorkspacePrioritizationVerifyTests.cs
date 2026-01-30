using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Extensions;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using Xunit;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Verification tests for workspace file prioritization logic.
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
        var installationFile = new ManifestFile { RelativePath = "data.ini", Size = 100, SourcePath = "installation" };
        var clientFile = new ManifestFile { RelativePath = "data.ini", Size = 200, SourcePath = "client" };

        var installationManifest = new ContentManifest
        {
            Id = new ManifestId("install"),
            ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
            Files = [installationFile],
        };

        var clientManifest = new ContentManifest
        {
            Id = new ManifestId("client"),
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            Files = [clientFile],
        };

        // Order matters for the BUG: usually installation comes first
        var config = new WorkspaceConfiguration
        {
            Manifests = [installationManifest, clientManifest],
        };

        // Act
        var result = config.GetAllUniqueFiles().ToList();

        // Assert
        Assert.Single(result);
        var chosenFile = result.First();
        Assert.Equal(200, chosenFile.Size);
        Assert.Equal("client", chosenFile.SourcePath);
    }

    /// <summary>
    /// Verifies that high-priority content (like mods) correctly overwrites low-priority content (like installations).
    /// </summary>
    [Fact]
    public void GetAllUniqueFiles_ShouldPrioritizeHighPriorityContent()
    {
        // Arrange
        var lowPriorityFile = new ManifestFile { RelativePath = "config.ini", Size = 100, SourcePath = "low" };
        var highPriorityFile = new ManifestFile { RelativePath = "config.ini", Size = 200, SourcePath = "high" };

        var installationManifest = new ContentManifest
        {
            Id = new ManifestId("install"),
            ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
            Files = [lowPriorityFile],
        };

        var modManifest = new ContentManifest
        {
            Id = new ManifestId("mod"),
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Files = [highPriorityFile],
        };

        // Put installation first to trigger the potential bug (if it picks first)
        var config = new WorkspaceConfiguration
        {
            Manifests = [installationManifest, modManifest],
        };

        // Act
        var uniqueFiles = config.GetAllUniqueFiles().ToList();

        // Assert
        Assert.Single(uniqueFiles);
        var chosenFile = uniqueFiles.First();

        // Should be the mod file (size 200)
        Assert.Equal(200, chosenFile.Size);
        Assert.Equal("high", chosenFile.SourcePath);
    }
}
