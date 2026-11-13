using System.Collections.Generic;

namespace GenHub.Features.Launching;

/// <summary>
/// Describes how a stored installation manifest differs from the installation folder.
/// </summary>
/// <param name="HasDrift">Whether any difference was detected.</param>
/// <param name="AddedFiles">Files on disk that regeneration would newly capture.</param>
/// <param name="RemovedFiles">Manifest files missing from disk.</param>
/// <param name="ChangedFiles">Manifest files whose size differs on disk.</param>
internal sealed record InstallationManifestDrift(
    bool HasDrift,
    IReadOnlyList<string> AddedFiles,
    IReadOnlyList<string> RemovedFiles,
    IReadOnlyList<string> ChangedFiles);
