using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Json;

namespace GenHub.Features.Workspace.Live;

/// <summary>
/// Stores live workspace ownership state under the application data directory.
/// </summary>
public sealed class LiveWorkspaceStateStore(
    IConfigurationProviderService configurationProvider,
    ILogger<LiveWorkspaceStateStore> logger) : ILiveWorkspaceStateStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    /// <inheritdoc/>
    public LiveWorkspaceState Load(string workspaceId)
    {
        var state = new LiveWorkspaceState { WorkspaceId = workspaceId };
        var path = GetStatePath(workspaceId);
        if (path == null)
        {
            return state;
        }

        try
        {
            if (!File.Exists(path))
            {
                return state;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LiveWorkspaceState>(json) ?? state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "[Live] Could not load workspace state {Path}; starting empty", path);
            return state;
        }
    }

    /// <inheritdoc/>
    public void Save(LiveWorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var path = GetStatePath(state.WorkspaceId);
        if (path == null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, _jsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "[Live] Could not save workspace state {Path}", path);
        }
    }

    /// <inheritdoc/>
    public void Delete(string workspaceId)
    {
        var path = GetStatePath(workspaceId);
        if (path == null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "[Live] Could not delete workspace state {Path}", path);
        }
    }

    private string? GetStatePath(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId) ||
            Path.GetFileName(workspaceId) != workspaceId ||
            workspaceId is "." or "..")
        {
            logger.LogWarning("[Live] Refusing to use workspace id '{WorkspaceId}' as a state file name", workspaceId);
            return null;
        }

        return Path.Combine(
            configurationProvider.GetApplicationDataPath(),
            "live",
            workspaceId + ".json");
    }
}
