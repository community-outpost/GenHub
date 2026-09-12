using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Workspace;
using Xunit;

namespace GenHub.Tests.Core.Models.Workspace;

/// <summary>
/// Tests that workspace metadata written by releases up to v0.0.3 still resolves to the strategy it
/// was persisted with. A mismatch between the persisted strategy and the profile strategy makes
/// WorkspaceManager discard and rebuild the workspace.
/// </summary>
public class WorkspaceMetadataDeserializationTests
{
    private static readonly JsonSerializerOptions MetadataOptions = new() { WriteIndented = true };

    /// <summary>
    /// Verifies that the raw ordinals stored in workspaces.json map back to their original strategies.
    /// </summary>
    [Fact]
    public void Deserialize_LegacyWorkspacesFile_MapsOrdinalsToOriginalStrategies()
    {
        var json = """
        [
          {
            "Id": "symlink-workspace",
            "WorkspacePath": "/data/workspaces/symlink-workspace",
            "GameClientId": "generals-zh",
            "Strategy": 0,
            "IsPrepared": true
          },
          {
            "Id": "fullcopy-workspace",
            "WorkspacePath": "/data/workspaces/fullcopy-workspace",
            "GameClientId": "generals-zh",
            "Strategy": 1,
            "IsPrepared": true
          },
          {
            "Id": "hybrid-workspace",
            "WorkspacePath": "/data/workspaces/hybrid-workspace",
            "GameClientId": "generals-zh",
            "Strategy": 2,
            "IsPrepared": true
          },
          {
            "Id": "hardlink-workspace",
            "WorkspacePath": "/data/workspaces/hardlink-workspace",
            "GameClientId": "generals-zh",
            "Strategy": 3,
            "IsPrepared": true
          }
        ]
        """;

        var workspaces = JsonSerializer.Deserialize<List<WorkspaceInfo>>(json, MetadataOptions);

        Assert.NotNull(workspaces);
        Assert.Equal(
            new[]
            {
                WorkspaceStrategy.SymlinkOnly,
                WorkspaceStrategy.FullCopy,
                WorkspaceStrategy.HybridCopySymlink,
                WorkspaceStrategy.HardLink,
            },
            workspaces.Select(workspace => workspace.Strategy));
    }

    /// <summary>
    /// Verifies that a legacy workspace and the profile that owns it agree on the strategy, which is
    /// the comparison that decides whether an existing workspace can be reused.
    /// </summary>
    /// <param name="workspaceOrdinal">The ordinal persisted in workspaces.json.</param>
    /// <param name="profileStrategyName">The strategy name persisted in the profile.</param>
    [Theory]
    [InlineData(0, "SymlinkOnly")]
    [InlineData(1, "FullCopy")]
    [InlineData(2, "HybridCopySymlink")]
    [InlineData(3, "HardLink")]
    public void Deserialize_LegacyWorkspaceAndProfile_AgreeOnStrategy(int workspaceOrdinal, string profileStrategyName)
    {
        var workspaceJson = $$"""
        { "Id": "workspace", "Strategy": {{workspaceOrdinal}} }
        """;
        var profileJson = $"\"{profileStrategyName}\"";

        var workspace = JsonSerializer.Deserialize<WorkspaceInfo>(workspaceJson, MetadataOptions);
        var profileStrategy = JsonSerializer.Deserialize<WorkspaceStrategy>(profileJson);

        Assert.NotNull(workspace);
        Assert.Equal(profileStrategy, workspace.Strategy);
    }

    /// <summary>
    /// Verifies that newly written workspace metadata stores the strategy name, so a future
    /// reordering of the enum cannot corrupt it.
    /// </summary>
    [Fact]
    public void Serialize_WorkspaceMetadata_WritesStrategyName()
    {
        var workspaces = new List<WorkspaceInfo>
        {
            new() { Id = "workspace", Strategy = WorkspaceStrategy.HardLink },
        };

        var json = JsonSerializer.Serialize(workspaces, MetadataOptions);

        Assert.Contains("\"Strategy\": \"HardLink\"", json);
    }
}
