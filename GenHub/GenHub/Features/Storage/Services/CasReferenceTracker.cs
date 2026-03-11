using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// Tracks references to CAS objects for garbage collection purposes.
/// </summary>
public class CasReferenceTracker(
    IOptions<CasConfiguration> config,
    ILogger<CasReferenceTracker> logger) : ICasReferenceTracker
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
    public async Task<OperationResult> TrackManifestReferencesAsync(string manifestId, ContentManifest manifest, CancellationToken cancellationToken = default)
    {
        // Validate parameters before acquiring semaphore
        if (string.IsNullOrWhiteSpace(manifestId))
            throw new ArgumentException("Manifest ID cannot be null or empty", nameof(manifestId));

        ArgumentNullException.ThrowIfNull(manifest);

        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Sanitize manifestId to prevent path traversal
            var safeManifestId = Path.GetFileName(manifestId);
            if (string.IsNullOrWhiteSpace(safeManifestId) || !string.Equals(safeManifestId, manifestId, StringComparison.OrdinalIgnoreCase))
            {
                 // If getting filename changes the ID (other than maybe case if filesys is insensitive, but here IDs are usually strict),
                 // or if it's empty, we reject it. The ID should be a simple name, not a path.
                 throw new ArgumentException($"Invalid Manifest ID '{manifestId}' - must be a valid filename without path characters", nameof(manifestId));
            }

            var manifestRefsPath = Path.Combine(_refsDirectory, "manifests", $"{safeManifestId}.refs");

            EnsureRefsDirectory();

            var references = manifest.Files
                .Where(f => f.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(f.Hash))
                .Select(f => f.Hash!)
                .ToHashSet();

            var refData = new
            {
                ManifestId = manifestId,
                References = references,
                TrackedAt = DateTime.UtcNow,
                manifest.ManifestVersion,
            };

            var json = JsonSerializer.Serialize(refData, JsonOptions);

            // Atomic write: write to temp file then move
            var tempFile = $"{manifestRefsPath}.tmp";
            await File.WriteAllTextAsync(tempFile, json, cancellationToken);
            File.Move(tempFile, manifestRefsPath, overwrite: true);

            _logger.LogDebug("Tracked {ReferenceCount} CAS references for manifest {ManifestId}", references.Count, manifestId);
            return OperationResult.CreateSuccess();
        }
        catch (OperationCanceledException)
        {
            // Re-throw to allow callers to honor cancellation
            throw;
        }
        catch (IOException ioEx)
        {
            _logger.LogError(ioEx, "IO error while tracking manifest references for {ManifestId}", manifestId);
            return OperationResult.CreateFailure($"IO error tracking manifest references: {ioEx.Message}");
        }
        catch (UnauthorizedAccessException uaEx)
        {
            _logger.LogError(uaEx, "Access denied while tracking manifest references for {ManifestId}", manifestId);
            return OperationResult.CreateFailure($"Access denied tracking manifest references: {uaEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track manifest references for {ManifestId}", manifestId);
            return OperationResult.CreateFailure($"Failed to track manifest references: {ex.Message}");
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
    /// <param name="referencedHashes">The set of CAS hashes referenced by workspace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task<OperationResult> TrackWorkspaceReferencesAsync(string workspaceId, IEnumerable<string> referencedHashes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID cannot be null or empty", nameof(workspaceId));

        ArgumentNullException.ThrowIfNull(referencedHashes);

        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            EnsureRefsDirectory();

            // Sanitize workspaceId to prevent path traversal
            var safeWorkspaceId = Path.GetFileName(workspaceId);
            if (string.IsNullOrWhiteSpace(safeWorkspaceId) || !string.Equals(safeWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            {
                 throw new ArgumentException($"Invalid Workspace ID '{workspaceId}' - must be a valid filename without path characters", nameof(workspaceId));
            }

            var workspaceRefsPath = Path.Combine(_refsDirectory, "workspaces", $"{safeWorkspaceId}.refs");

            var refData = new
            {
                WorkspaceId = workspaceId,
                References = referencedHashes.ToHashSet(),
                TrackedAt = DateTime.UtcNow,
            };

            var json = JsonSerializer.Serialize(refData, JsonOptions);

            // Atomic write: write to temp file then move
            var tempFile = $"{workspaceRefsPath}.tmp";
            await File.WriteAllTextAsync(tempFile, json, cancellationToken);
            File.Move(tempFile, workspaceRefsPath, overwrite: true);

            _logger.LogDebug("Tracked {ReferenceCount} CAS references for workspace {WorkspaceId}", refData.References.Count, workspaceId);
            return OperationResult.CreateSuccess();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ioEx)
        {
            _logger.LogError(ioEx, "IO error while tracking workspace references for {WorkspaceId}", workspaceId);
            return OperationResult.CreateFailure($"IO error tracking workspace references: {ioEx.Message}");
        }
        catch (UnauthorizedAccessException uaEx)
        {
            _logger.LogError(uaEx, "Access denied while tracking workspace references for {WorkspaceId}", workspaceId);
            return OperationResult.CreateFailure($"Access denied tracking workspace references: {uaEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track workspace references for {WorkspaceId}", workspaceId);
            return OperationResult.CreateFailure($"Failed to track workspace references: {ex.Message}");
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Removes tracking for a manifest.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task<OperationResult> UntrackManifestAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        // Validate parameters before acquiring semaphore
        if (string.IsNullOrWhiteSpace(manifestId))
            throw new ArgumentException("Manifest ID cannot be null or empty", nameof(manifestId));

        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Sanitize manifestId to prevent path traversal & validate
            var safeManifestId = Path.GetFileName(manifestId);
            if (string.IsNullOrWhiteSpace(safeManifestId) || !string.Equals(safeManifestId, manifestId, StringComparison.OrdinalIgnoreCase))
            {
                 return OperationResult.CreateFailure($"Invalid Manifest ID '{manifestId}' - must be a valid filename without path characters");
            }

            var manifestRefsPath = Path.Combine(_refsDirectory, "manifests", $"{safeManifestId}.refs");

            if (File.Exists(manifestRefsPath))
            {
                await Task.Run(() => File.Delete(manifestRefsPath), cancellationToken);
                _logger.LogDebug("Removed CAS reference tracking for manifest {ManifestId}", manifestId);
            }

            return OperationResult.CreateSuccess();
        }
        catch (OperationCanceledException)
        {
            // Re-throw to allow callers to honor cancellation
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove reference tracking for manifest {ManifestId}", manifestId);
            return OperationResult.CreateFailure($"Failed to remove manifest tracking: {ex.Message}");
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    /// <summary>
    /// Removes tracking for a workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task<OperationResult> UntrackWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        // Validate parameters before acquiring semaphore
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID cannot be null or empty", nameof(workspaceId));

        await _writeSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Sanitize workspaceId to prevent path traversal & validate
            var safeWorkspaceId = Path.GetFileName(workspaceId);
            if (string.IsNullOrWhiteSpace(safeWorkspaceId) || !string.Equals(safeWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
            {
                 return OperationResult.CreateFailure($"Invalid Workspace ID '{workspaceId}' - must be a valid filename without path characters");
            }

            var workspaceRefsPath = Path.Combine(_refsDirectory, "workspaces", $"{safeWorkspaceId}.refs");

            if (File.Exists(workspaceRefsPath))
            {
                await Task.Run(() => File.Delete(workspaceRefsPath), cancellationToken);
                _logger.LogDebug("Removed CAS reference tracking for workspace {WorkspaceId}", workspaceId);
            }

            return OperationResult.CreateSuccess();
        }
        catch (OperationCanceledException)
        {
            // Re-throw to allow callers to honor cancellation
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove reference tracking for workspace {WorkspaceId}", workspaceId);
            return OperationResult.CreateFailure($"Failed to remove workspace tracking: {ex.Message}");
        }
        finally
        {
            _writeSemaphore.Release();
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect CAS references");
            throw; // Re-throw to abort GC when reference enumeration fails
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read references from {RefFile}", refFile);
            throw; // Fail closed: if we can't read refs, we shouldn't assume empty and risk GCing live data
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
            Directory.CreateDirectory(directory);
        }
    }
}
