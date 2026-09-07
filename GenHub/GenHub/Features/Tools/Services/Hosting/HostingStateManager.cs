using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Default implementation of IHostingStateManager.
/// </summary>
public class HostingStateManager(ILogger<HostingStateManager> logger) : IHostingStateManager
{
    private const string StateFileName = "hosting_state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
        try
        {
            var stateFilePath = GetStateFilePath(projectPath);

            if (!File.Exists(stateFilePath))
            {
                logger.LogDebug("No hosting state file found at {Path}", stateFilePath);
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var json = await File.ReadAllTextAsync(stateFilePath, cancellationToken);
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load hosting state from {ProjectPath}", projectPath);
            return OperationResult<HostingState?>.CreateFailure($"Failed to load hosting state: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SaveStateAsync(string projectPath, HostingState state, CancellationToken cancellationToken = default)
    {
        try
        {
            var stateFilePath = GetStateFilePath(projectPath);

            var json = JsonSerializer.Serialize(state, JsonOptions);
            await File.WriteAllTextAsync(stateFilePath, json, cancellationToken);

            logger.LogInformation(
                "Saved hosting state to {Path}: Provider={Provider}, Catalogs={CatalogCount}",
                stateFilePath,
                state.ProviderId,
                state.Catalogs.Count);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save hosting state to {ProjectPath}", projectPath);
            return OperationResult<bool>.CreateFailure($"Failed to save hosting state: {ex.Message}");
        }
    }
}
