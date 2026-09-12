using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.MapManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.MapManager;

/// <summary>
/// Handles importing maps from various sources.
/// </summary>
public interface IMapImportService
{
    /// <summary>
    /// Maximum allowed size for a single map file (10 MB).
    /// </summary>
    public const long MaxMapSizeBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Imports a map from a URL.
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
    /// Imports map files from local paths.
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
    /// Imports maps from a ZIP archive.
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
    /// Validates a ZIP archive to ensure it contains only map files.
    /// </summary>
    /// <param name="zipPath">The path to the ZIP archive.</param>
    /// <returns>A result indicating whether the ZIP is valid and any error message.</returns>
    (bool IsValid, string? ErrorMessage) ValidateZip(string zipPath);

    /// <summary>
    /// Imports maps from a stream (e.g., for drag-and-drop).
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
