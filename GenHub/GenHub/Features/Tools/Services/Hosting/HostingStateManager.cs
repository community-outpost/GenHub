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
/// Manages loading and saving of hosting state for publisher projects.
/// The hosting state tracks file IDs and URLs for published content.
/// </summary>
public interface IHostingStateManager
{
    /// <summary>
    /// Loads hosting state from the hosting_state.json file alongside a project.
    /// </summary>
    /// <param name="projectPath">Path to the .genhub-project file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded hosting state, or null if no state file exists.</returns>
    Task<OperationResult<HostingState?>> LoadStateAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves hosting state to the hosting_state.json file alongside a project.
    /// </summary>
    /// <param name="projectPath">Path to the .genhub-project file.</param>
    /// <param name="state">The hosting state to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OperationResult<bool>> SaveStateAsync(string projectPath, HostingState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the path to the hosting state file for a project.
    /// </summary>
    /// <param name="projectPath">Path to the .genhub-project file.</param>
    /// <returns>Path to the hosting_state.json file.</returns>
    string GetStateFilePath(string projectPath);

    /// <summary>
    /// Checks if a hosting state file exists for a project.
    /// </summary>
    /// <param name="projectPath">Path to the .genhub-project file.</param>
    /// <returns>True if a state file exists.</returns>
    bool StateFileExists(string projectPath);
}

/// <summary>
/// Default implementation of IHostingStateManager.
/// </summary>
public class HostingStateManager : IHostingStateManager
{
    private const string StateFileName = "hosting_state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<HostingStateManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostingStateManager"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public HostingStateManager(ILogger<HostingStateManager> logger)
    {
        _logger = logger;
    }

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
                _logger.LogDebug("No hosting state file found at {Path}", stateFilePath);
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            var json = await File.ReadAllTextAsync(stateFilePath, cancellationToken);
            var state = JsonSerializer.Deserialize<HostingState>(json, JsonOptions);

            if (state == null)
            {
                _logger.LogWarning("Failed to deserialize hosting state from {Path}", stateFilePath);
                return OperationResult<HostingState?>.CreateSuccess(null);
            }

            _logger.LogInformation(
                "Loaded hosting state: Provider={Provider}, Definition={HasDef}, Catalogs={CatalogCount}",
                state.ProviderId,
                state.Definition != null,
                state.Catalogs.Count);

            return OperationResult<HostingState?>.CreateSuccess(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load hosting state from {ProjectPath}", projectPath);
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

            _logger.LogInformation(
                "Saved hosting state to {Path}: Provider={Provider}, Catalogs={CatalogCount}",
                stateFilePath,
                state.ProviderId,
                state.Catalogs.Count);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save hosting state to {ProjectPath}", projectPath);
            return OperationResult<bool>.CreateFailure($"Failed to save hosting state: {ex.Message}");
        }
    }
}
