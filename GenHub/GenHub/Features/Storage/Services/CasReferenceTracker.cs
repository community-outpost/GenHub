using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Storage;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// Tracks references to CAS objects for garbage collection purposes.
/// </summary>
public class CasReferenceTracker(
    IOptions<CasConfiguration> config,
    ILogger<CasReferenceTracker> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly CasConfiguration _config = config.Value;
    private readonly ILogger<CasReferenceTracker> _logger = logger;
    private readonly string _refsDirectory = Path.Combine(config.Value.CasRootPath, "refs");

    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

    /// <summary>
    /// Tracks references from a game manifest.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="manifest">The game manifest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task TrackManifestReferencesAsync(string manifestId, ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        // Validate parameters before acquiring semaphore
        if (string.IsNullOrWhiteSpace(manifestId))
            throw new ArgumentException("Manifest ID cannot be null or empty", nameof(manifestId));
        if (manifest == null)
            throw new ArgumentNullException(nameof(manifest));

        EnsureRefsDirectory();
        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            try
            {
                EnsureRefsDirectory();

                // Sanitize manifestId to prevent path traversal
                var safeManifestId = Path.GetFileName(manifestId);
                var manifestRefsPath = Path.Combine(_refsDirectory, "manifests", $"{safeManifestId}.refs");
                var directoryPath = Path.GetDirectoryName(manifestRefsPath);
                if (directoryPath != null)
                    Directory.CreateDirectory(directoryPath);

                var references = manifest.Files
                    .Where(f => f.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(f.Hash))
                    .Select(f => f.Hash!)
                    .ToHashSet();

                var refData = new
                {
                    ManifestId = manifestId,
                    References = references,
                    TrackedAt = DateTime.UtcNow,
                    ManifestVersion = manifest.ManifestVersion,
                };

                var json = JsonSerializer.Serialize(refData, JsonOptions);
                await File.WriteAllTextAsync(manifestRefsPath, json, cancellationToken);

                _logger.LogDebug("Tracked {ReferenceCount} CAS references for manifest {ManifestId}", references.Count, manifestId);
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "IO error while tracking manifest references for {ManifestId}", manifestId);
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogError(uaEx, "Access denied while tracking manifest references for {ManifestId}", manifestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to track manifest references for {ManifestId}", manifestId);
            }
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Tracks references from a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace ID.</param>
    /// <param name="referencedHashes">The set of CAS hashes referenced by the workspace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task TrackWorkspaceReferencesAsync(string workspaceId, IEnumerable<string> referencedHashes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID cannot be null or empty", nameof(workspaceId));
        if (referencedHashes == null)
            throw new ArgumentNullException(nameof(referencedHashes));

        try
        {
            EnsureRefsDirectory();

            // Sanitize workspaceId to prevent path traversal
            var safeWorkspaceId = Path.GetFileName(workspaceId);
            var workspaceRefsPath = Path.Combine(_refsDirectory, "workspaces", $"{safeWorkspaceId}.refs");
            var directoryPath = Path.GetDirectoryName(workspaceRefsPath);
            if (directoryPath != null)
                Directory.CreateDirectory(directoryPath);

            var refData = new
            {
                WorkspaceId = workspaceId,
                References = referencedHashes as HashSet<string> ?? referencedHashes.ToHashSet(),
                TrackedAt = DateTime.UtcNow,
            };

            var json = JsonSerializer.Serialize(refData, JsonOptions);
            await File.WriteAllTextAsync(workspaceRefsPath, json, cancellationToken);

            _logger.LogDebug("Tracked {ReferenceCount} CAS references for workspace {WorkspaceId}", refData.References.Count, workspaceId);
        }
        catch (IOException ioEx)
        {
            _logger.LogError(ioEx, "IO error while tracking workspace references for {WorkspaceId}", workspaceId);
        }
        catch (UnauthorizedAccessException uaEx)
        {
            _logger.LogError(uaEx, "Access denied while tracking workspace references for {WorkspaceId}", workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track workspace references for {WorkspaceId}", workspaceId);
        }
    }

    /// <summary>
    /// Removes tracking for a manifest.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task UntrackManifestAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        try
        {
            var manifestRefsPath = Path.Combine(_refsDirectory, "manifests", $"{manifestId}.refs");

            if (File.Exists(manifestRefsPath))
            {
                await Task.Run(() => File.Delete(manifestRefsPath), cancellationToken);
                _logger.LogDebug("Removed CAS reference tracking for manifest {ManifestId}", manifestId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove reference tracking for manifest {ManifestId}", manifestId);
        }
    }

    /// <summary>
    /// Removes tracking for a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task UntrackWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var workspaceRefsPath = Path.Combine(_refsDirectory, "workspaces", $"{workspaceId}.refs");

            if (File.Exists(workspaceRefsPath))
            {
                await Task.Run(() => File.Delete(workspaceRefsPath), cancellationToken);
                _logger.LogDebug("Removed CAS reference tracking for workspace {WorkspaceId}", workspaceId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove reference tracking for workspace {WorkspaceId}", workspaceId);
        }
    }

    /// <summary>
    /// Gets all CAS hashes that are currently referenced.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Set of all referenced hashes.</returns>
    public async Task<HashSet<string>> GetAllReferencedHashesAsync(CancellationToken cancellationToken = default)
    {
        var allReferences = new HashSet<string>();

        try
        {
            var subdirectories = new[] { "manifests", "workspaces" };
            var tasks = new List<Task<HashSet<string>>>();

            foreach (var subdirectory in subdirectories)
            {
                var refsDir = Path.Combine(_refsDirectory, subdirectory);
                if (Directory.Exists(refsDir))
                {
                    var refFiles = Directory.GetFiles(refsDir, "*.refs");

                    // Limit parallelism to avoid overwhelming the system
                    var batchSize = Math.Min(10, refFiles.Length);
                    var semaphore = new SemaphoreSlim(batchSize);

                    var fileTasks = refFiles.Select(async refFile =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            return await ReadReferencesFromFileAsync(refFile, cancellationToken);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    });

                    tasks.AddRange(fileTasks);
                }
            }

            var results = await Task.WhenAll(tasks);
            foreach (var references in results)
            {
                allReferences.UnionWith(references);
            }

            _logger.LogDebug("Collected {ReferenceCount} total CAS references", allReferences.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect CAS references");
        }

        return allReferences;
    }

    private async Task<HashSet<string>> ReadReferencesFromFileAsync(string refFile, CancellationToken cancellationToken)
    {
        var references = new HashSet<string>();
        try
        {
            var json = await File.ReadAllTextAsync(refFile, cancellationToken);
            var refData = JsonSerializer.Deserialize<JsonElement>(json);

            if (refData.TryGetProperty("References", out var referencesElement))
            {
                foreach (var reference in referencesElement.EnumerateArray())
                {
                    var hash = reference.GetString();
                    if (!string.IsNullOrEmpty(hash))
                    {
                        references.Add(hash);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read references from {RefFile}", refFile);
        }

        return references;
    }

    private void EnsureRefsDirectory()
    {
        var requiredDirectories = new[]
        {
            _refsDirectory,
            Path.Combine(_refsDirectory, "manifests"),
            Path.Combine(_refsDirectory, "workspaces"),
        };

        foreach (var directory in requiredDirectories)
        {
            FileOperationsService.EnsureDirectoryExists(directory);
        }
    }
}
