using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Handles importing replays from various sources.
/// </summary>
public interface IReplayImportService
{
    /// <summary>
    /// Maximum size for a single replay file in bytes (1 MB).
    /// </summary>
    public const long MaxReplaySizeBytes = ReplayManagerConstants.MaxReplaySizeBytes;

    /// <summary>
    /// Imports a replay from a URL.
    /// </summary>
    /// <param name="url">The URL to import from.</param>
    /// <param name="targetVersion">The target game version.</param>
    /// <param name="progress">Progress reporter for download updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the import operation.</returns>
    Task<ImportResult> ImportFromUrlAsync(
        string url,
        GameType targetVersion,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Imports replay files from local paths.
    /// </summary>
    /// <param name="filePaths">The paths to the local files.</param>
    /// <param name="targetVersion">The target game version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the import operation.</returns>
    Task<ImportResult> ImportFromFilesAsync(
        IEnumerable<string> filePaths,
        GameType targetVersion,
        CancellationToken ct = default);

    /// <summary>
    /// Imports replays from a ZIP archive.
    /// </summary>
    /// <param name="zipPath">The path to the ZIP archive.</param>
    /// <param name="targetVersion">The target game version.</param>
    /// <param name="progress">Progress reporter for extraction updates.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the import operation.</returns>
    Task<ImportResult> ImportFromZipAsync(
        string zipPath,
        GameType targetVersion,
        IProgress<double>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a ZIP archive to ensure it contains only a single layer of replay files.
    /// </summary>
    /// <param name="zipPath">The path to the ZIP archive.</param>
    /// <returns>A result indicating whether the ZIP is valid and any error message.</returns>
    (bool IsValid, string? ErrorMessage) ValidateZip(string zipPath);

    /// <summary>
    /// Imports replays from a stream (e.g., for drag-and-drop).
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="fileName">The name of the file being imported.</param>
    /// <param name="targetVersion">The target game version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the import operation.</returns>
    Task<ImportResult> ImportFromStreamAsync(
        Stream stream,
        string fileName,
        GameType targetVersion,
        CancellationToken ct = default);
}
