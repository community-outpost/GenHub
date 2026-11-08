using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Workspace;

/// <summary>
/// One live workspace entry owned by GenHub.
/// </summary>
/// <param name="RelativePath">Path relative to the workspace root.</param>
/// <param name="SourcePath">Absolute source path.</param>
/// <param name="IsCopy">Whether the entry is a private copy instead of a link.</param>
public sealed record LiveWorkspaceEntry(
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("sourcePath")] string SourcePath,
    [property: JsonPropertyName("isCopy")] bool IsCopy);
