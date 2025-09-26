using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Workspace;

/// <summary>
/// Complete workspace management service with persistence and cleanup.
/// Manages workspace operations including preparation, retrieval, and cleanup.
/// </summary>
public class WorkspaceManager(
    IEnumerable<IWorkspaceStrategy> strategies,
    IConfigurationProviderService configurationProvider,
    ILogger<WorkspaceManager> logger,
    CasReferenceTracker casReferenceTracker,
    IWorkspaceValidator workspaceValidator
) : IWorkspaceManager
{
    private readonly string _workspaceMetadataPath = Path.Combine(configurationProvider.GetContentStoragePath(), "workspaces.json");

    private readonly IEnumerable<IWorkspaceStrategy> _strategies = strategies;
    private readonly ILogger<WorkspaceManager> _logger = logger;
    private readonly CasReferenceTracker _casReferenceTracker = casReferenceTracker;
    private readonly IWorkspaceValidator _workspaceValidator = workspaceValidator;

    /// <summary>
    /// Prepares a workspace using the specified configuration and strategy.
    /// </summary>
    /// <param name="configuration">The workspace configuration.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The prepared workspace information.</returns>
    public async Task<OperationResult<WorkspaceInfo>> PrepareWorkspaceAsync(WorkspaceConfiguration configuration, IProgress<WorkspacePreparationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Preparing workspace for configuration {Id} using strategy {Strategy}", configuration.Id, configuration.Strategy);

        var strategy = _strategies.FirstOrDefault(s => s.CanHandle(configuration)) ?? throw new InvalidOperationException($"No strategy available for workspace configuration {configuration.Id} with strategy {configuration.Strategy}");
        _logger.LogDebug("Using strategy {Strategy} for workspace {Id}", strategy.Name, configuration.Id);

        // Clean up existing workspace if force recreate is requested
        if (configuration.ForceRecreate)
        {
            await CleanupWorkspaceAsync(configuration.Id, cancellationToken);
        }

        var workspaceInfo = await strategy.PrepareAsync(configuration, progress, cancellationToken);

        if (!workspaceInfo.IsPrepared)
        {
            var messages = workspaceInfo.ValidationIssues?.Select(i => i.Message)
                           ?? new[] { "Workspace preparation failed" };
            return OperationResult<WorkspaceInfo>.CreateFailure(string.Join(", ", messages) ?? "Workspace preparation failed");
        }

        // Validate workspace after preparation if requested
        if (configuration.ValidateAfterPreparation)
        {
            var validationResult = await _workspaceValidator.ValidateWorkspaceAsync(workspaceInfo, cancellationToken);
            if (!validationResult.Success || !validationResult.Data!.IsValid)
            {
                var errors = validationResult.Data!.Issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.Message);
                return OperationResult<WorkspaceInfo>.CreateFailure($"Workspace validation failed: {string.Join(", ", errors)}");
            }
        }

        // Save workspace metadata
        await SaveWorkspaceMetadataAsync(workspaceInfo, cancellationToken);

        // Track CAS references for the workspace
        await TrackWorkspaceCasReferencesAsync(configuration.Id, configuration.Manifests, cancellationToken);

        _logger.LogInformation("Workspace {Id} prepared successfully at {Path}", workspaceInfo.Id, workspaceInfo.WorkspacePath);
        return OperationResult<WorkspaceInfo>.CreateSuccess(workspaceInfo);
    }

    /// <summary>
    /// Retrieves all workspaces asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An operation result containing all prepared workspaces.</returns>
    public async Task<OperationResult<IEnumerable<WorkspaceInfo>>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving all workspaces");

        try
        {
            if (!File.Exists(_workspaceMetadataPath))
            {
                return OperationResult<IEnumerable<WorkspaceInfo>>.CreateSuccess(Enumerable.Empty<WorkspaceInfo>());
            }

            var json = await File.ReadAllTextAsync(_workspaceMetadataPath, cancellationToken);
            var workspaces = JsonSerializer.Deserialize<List<WorkspaceInfo>>(json) ?? [];

            // Filter out workspaces that no longer exist
            var validWorkspaces = workspaces.Where(w => Directory.Exists(w.WorkspacePath)).ToList();

            if (validWorkspaces.Count != workspaces.Count)
            {
                await SaveAllWorkspacesAsync(validWorkspaces, cancellationToken);
            }

            return OperationResult<IEnumerable<WorkspaceInfo>>.CreateSuccess(validWorkspaces.AsEnumerable());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve workspaces");
            return OperationResult<IEnumerable<WorkspaceInfo>>.CreateFailure($"Failed to retrieve workspaces: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up the specified workspace asynchronously.
    /// </summary>
    /// <param name="workspaceId">The workspace identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An operation result indicating whether the workspace was cleaned up successfully.</returns>
    public async Task<OperationResult<bool>> CleanupWorkspaceAsync(string workspaceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var workspacesResult = await GetAllWorkspacesAsync(cancellationToken);
            if (!workspacesResult.Success)
            {
                return OperationResult<bool>.CreateFailure($"Failed to get workspaces for cleanup: {workspacesResult.FirstError}");
            }

            var workspaces = workspacesResult.Data!.ToList();
            var workspace = workspaces.FirstOrDefault(w => w.Id == workspaceId);

            if (workspace == null)
            {
                _logger.LogWarning("Workspace {Id} not found for cleanup", workspaceId);
                return OperationResult<bool>.CreateSuccess(false);
            }

            if (FileOperationsService.DeleteDirectoryIfExists(workspace.WorkspacePath))
            {
                _logger.LogInformation("Deleted workspace directory {Path}", workspace.WorkspacePath);
            }

            workspaces.Remove(workspace);
            await SaveAllWorkspacesAsync(workspaces, cancellationToken);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup workspace {Id}", workspaceId);
            return OperationResult<bool>.CreateFailure($"Failed to cleanup workspace: {ex.Message}");
        }
    }

    private async Task SaveAllWorkspacesAsync(IEnumerable<WorkspaceInfo> workspaces, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_workspaceMetadataPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(workspaces, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_workspaceMetadataPath, json, cancellationToken);
    }

    private async Task SaveWorkspaceMetadataAsync(WorkspaceInfo workspaceInfo, CancellationToken cancellationToken)
    {
        var workspacesResult = await GetAllWorkspacesAsync(cancellationToken);
        var workspaces = workspacesResult.Data?.ToList() ?? new List<WorkspaceInfo>();
        var existing = workspaces.FirstOrDefault(w => w.Id == workspaceInfo.Id);

        if (existing != null)
        {
            workspaces.Remove(existing);
        }

        workspaces.Add(workspaceInfo);
        await SaveAllWorkspacesAsync(workspaces, cancellationToken);
    }

    private async Task TrackWorkspaceCasReferencesAsync(string workspaceId, IEnumerable<ContentManifest> manifests, CancellationToken cancellationToken)
    {
        var casReferences = manifests.SelectMany(m => m.Files ?? Enumerable.Empty<ManifestFile>())
            .Where(f => f.SourceType == ContentSourceType.ContentAddressable && !string.IsNullOrEmpty(f.Hash))
            .Select(f => f.Hash!)
            .ToList();

        if (casReferences.Any())
        {
            await _casReferenceTracker.TrackWorkspaceReferencesAsync(workspaceId, casReferences, cancellationToken);
        }
    }
}
