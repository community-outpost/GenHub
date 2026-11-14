using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Persisted state of one live workspace: which entries GenHub created, and whether each
/// is a link or a private copy.
/// </summary>
public sealed class LiveWorkspaceState
{
    /// <summary>
    /// Gets or sets the state schema version. Version 0 is the pre-net-contribution
    /// format and must be rebuilt before incremental accounting is trusted.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>Gets or sets the workspace identifier.</summary>
    [JsonPropertyName("workspaceId")]
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the entries created by the live materializer.</summary>
    [JsonPropertyName("entries")]
    public List<LiveWorkspaceEntry> Entries { get; set; } = [];
}
