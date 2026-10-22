using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Default implementation of IHostingStateManager.
/// </summary>
public class HostingStateManager(ILogger<HostingStateManager> logger) : IHostingStateManager
{
    private sealed class RefCountedLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int RefCount { get; set; }
    }

    private const string StateFileName = "hosting_state.json";
    private const string UnknownProviderKey = "unknown";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly object StateLocksSync = new();

    private static readonly Dictionary<string, RefCountedLock> StateLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string GetStateFilePath(string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
        {
            throw new ArgumentException("Project path cannot be empty", nameof(projectPath));
        }

        var projectDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrEmpty(projectDir))
        {
            projectDir = ".";
        }

        return Path.Combine(projectDir, StateFileName);
    }

    /// <inheritdoc />
    public bool StateFileExists(string projectPath)
    {
        var stateFilePath = GetStateFilePath(projectPath);
        return File.Exists(stateFilePath);
    }

    /// <inheritdoc />
    public async Task<OperationResult<HostingState?>> LoadStateAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var stateFilePath = GetStateFilePath(projectPath);
        var stateLock = AcquireStateLock(stateFilePath);
        var semaphoreAcquired = false;

        try
        {
            await stateLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            semaphoreAcquired = true;

            if (!File.Exists(stateFilePath))
            {
                logger.LogDebug("No hosting state file found at {Path}", stateFilePath);
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var json = await File.ReadAllTextAsync(stateFilePath, cancellationToken).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<HostingState>(json, JsonOptions);

            if (state == null)
            {
                logger.LogWarning("Failed to deserialize hosting state from {Path}", stateFilePath);
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            logger.LogInformation(
                "Loaded hosting state: Provider={Provider}, Definition={HasDef}, Catalogs={CatalogCount}",
                state.ProviderId,
                state.Definition != null,
                state.Catalogs.Count);

            return OperationResult<HostingState?>.CreateSuccess(state);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load hosting state from {ProjectPath}", projectPath);
            return OperationResult<HostingState?>.CreateFailure($"Failed to load hosting state: {ex.Message}");
        }
        finally
        {
            ReleaseStateLock(stateFilePath, stateLock, semaphoreAcquired);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SaveStateAsync(string projectPath, HostingState state, CancellationToken cancellationToken = default)
    {
        var stateFilePath = GetStateFilePath(projectPath);
        var stateLock = AcquireStateLock(stateFilePath);
        var semaphoreAcquired = false;

        try
        {
            await stateLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            semaphoreAcquired = true;

            var json = JsonSerializer.Serialize(state, JsonOptions);
            await WriteJsonFileAsync(stateFilePath, json, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Saved hosting state to {Path}: Provider={Provider}, Catalogs={CatalogCount}",
                stateFilePath,
                state.ProviderId,
                state.Catalogs.Count);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save hosting state to {ProjectPath}", projectPath);
            return OperationResult<bool>.CreateFailure($"Failed to save hosting state: {ex.Message}");
        }
        finally
        {
            ReleaseStateLock(stateFilePath, stateLock, semaphoreAcquired);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<PublisherHostingStates>> LoadStatesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var stateFilePath = GetStateFilePath(projectPath);
        var stateLock = AcquireStateLock(stateFilePath);
        var semaphoreAcquired = false;

        try
        {
            await stateLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            semaphoreAcquired = true;

            if (!File.Exists(stateFilePath))
            {
                logger.LogDebug("No hosting state file found at {Path}", stateFilePath);
                return OperationResult<PublisherHostingStates>.CreateSuccess(new PublisherHostingStates());
            }

            var json = await File.ReadAllTextAsync(stateFilePath, cancellationToken).ConfigureAwait(false);
            var states = ParseStatesDocument(json);

            logger.LogInformation(
                "Loaded hosting states for {ProviderCount} provider(s) from {Path}",
                states.States.Count,
                stateFilePath);

            return OperationResult<PublisherHostingStates>.CreateSuccess(states);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load hosting states from {ProjectPath}", projectPath);
            return OperationResult<PublisherHostingStates>.CreateFailure($"Failed to load hosting states: {ex.Message}");
        }
        finally
        {
            ReleaseStateLock(stateFilePath, stateLock, semaphoreAcquired);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SaveStatesAsync(string projectPath, PublisherHostingStates states, CancellationToken cancellationToken = default)
    {
        var stateFilePath = GetStateFilePath(projectPath);
        var stateLock = AcquireStateLock(stateFilePath);
        var semaphoreAcquired = false;

        try
        {
            await stateLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            semaphoreAcquired = true;

            var json = JsonSerializer.Serialize(states, JsonOptions);
            await WriteJsonFileAsync(stateFilePath, json, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Saved hosting states for {ProviderCount} provider(s) to {Path}",
                states.States.Count,
                stateFilePath);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save hosting states to {ProjectPath}", projectPath);
            return OperationResult<bool>.CreateFailure($"Failed to save hosting states: {ex.Message}");
        }
        finally
        {
            ReleaseStateLock(stateFilePath, stateLock, semaphoreAcquired);
        }
    }

    private static RefCountedLock AcquireStateLock(string stateFilePath)
    {
        lock (StateLocksSync)
        {
            if (!StateLocks.TryGetValue(stateFilePath, out var entry))
            {
                entry = new RefCountedLock();
                StateLocks[stateFilePath] = entry;
            }

            entry.RefCount++;
            return entry;
        }
    }

    private static void ReleaseStateLock(string stateFilePath, RefCountedLock entry, bool releaseSemaphore)
    {
        if (releaseSemaphore)
        {
            entry.Semaphore.Release();
        }

        lock (StateLocksSync)
        {
            entry.RefCount--;
            if (entry.RefCount == 0 &&
                StateLocks.TryGetValue(stateFilePath, out var current) &&
                ReferenceEquals(current, entry))
            {
                StateLocks.Remove(stateFilePath);
                entry.Semaphore.Dispose();
            }
        }
    }

    private PublisherHostingStates ParseStatesDocument(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("states", out _))
        {
            var container = JsonSerializer.Deserialize<PublisherHostingStates>(json, JsonOptions);
            if (container != null)
            {
                container.States = new Dictionary<string, HostingState>(container.States, StringComparer.OrdinalIgnoreCase);
                return container;
            }
        }

        var states = new PublisherHostingStates();
        var legacy = JsonSerializer.Deserialize<HostingState>(json, JsonOptions);
        if (legacy != null)
        {
            var key = string.IsNullOrWhiteSpace(legacy.ProviderId) ? UnknownProviderKey : legacy.ProviderId;
            states.States[key] = legacy;
            logger.LogInformation("Migrated legacy single-provider hosting state to the multi-provider container");
        }

        return states;
    }

    private async Task WriteJsonFileAsync(string stateFilePath, string json, CancellationToken cancellationToken)
    {
        var tempPath = $"{stateFilePath}.tmp";

        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, stateFilePath, overwrite: true);
        }
        catch
        {
            DeleteTempFile(tempPath);
            throw;
        }
    }

    private void DeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to clean up temporary hosting state file {TempPath}", tempPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Failed to clean up temporary hosting state file {TempPath}", tempPath);
        }
    }
}
