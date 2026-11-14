using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// Describes one live content root mounted into a workspace.
/// </summary>
/// <param name="ManifestId">The manifest that owns the live folder.</param>
/// <param name="SourceRoot">The absolute folder linked into the workspace.</param>
public sealed record LiveSourceBinding(
    [property: JsonPropertyName("manifestId")] string ManifestId,
    [property: JsonPropertyName("sourceRoot")] string SourceRoot);
