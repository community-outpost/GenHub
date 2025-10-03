using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Facade for game profile editing operations, coordinating between multiple services
/// to provide a simplified interface for profile management.
/// </summary>
public class ProfileEditorFacade(
    IGameProfileManager profileManager,
    IContentOrchestrator contentOrchestrator,
    IGameInstallationService installationService,
    IWorkspaceManager workspaceManager,
    IContentManifestPool manifestPool,
    IConfigurationProviderService config,
    IDependencyResolver dependencyResolver,
    ILogger<ProfileEditorFacade> logger) : IProfileEditorFacade
{
    private readonly IGameProfileManager _profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
    private readonly IContentOrchestrator _contentOrchestrator = contentOrchestrator ?? throw new ArgumentNullException(nameof(contentOrchestrator));
    private readonly IGameInstallationService _installationService = installationService ?? throw new ArgumentNullException(nameof(installationService));
    private readonly IWorkspaceManager _workspaceManager = workspaceManager ?? throw new ArgumentNullException(nameof(workspaceManager));
    private readonly IContentManifestPool _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
    private readonly IConfigurationProviderService _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly IDependencyResolver _dependencyResolver = dependencyResolver ?? throw new ArgumentNullException(nameof(dependencyResolver));
    private readonly ILogger<ProfileEditorFacade> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileWithWorkspaceAsync(CreateProfileRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating profile with workspace for game installation: {InstallationId}", request.GameInstallationId);

            // First create the profile
            var createResult = await _profileManager.CreateProfileAsync(request, cancellationToken);
            if (createResult.Failed)
            {
                return createResult;
            }

            var profile = createResult.Data!;

            // Discover available content for this profile
            var contentResult = await _profileManager.GetAvailableContentAsync(profile.GameClient, cancellationToken);
            if (contentResult.Success && contentResult.Data != null)
            {
                // Auto-enable some basic content (game installation content)
                var gameInstallationContent = contentResult.Data
                    .Where(c => c.ContentType == Core.Models.Enums.ContentType.GameInstallation)
                    .ToList();

                if (gameInstallationContent.Any())
                {
                    profile.EnabledContentIds = gameInstallationContent.Select(c => c.Id.ToString()).ToList();

                    // Also auto-enable a GameClient content if available
                    var gameClientContent = contentResult.Data
                        .FirstOrDefault(c => c.ContentType == Core.Models.Enums.ContentType.GameClient);
                    if (gameClientContent != null && !profile.EnabledContentIds.Contains(gameClientContent.Id.ToString()))
                    {
                        profile.EnabledContentIds.Add(gameClientContent.Id.ToString());
                    }

                    // Update the profile with enabled content
                    var updateRequest = new UpdateProfileRequest
                    {
                        EnabledContentIds = profile.EnabledContentIds,
                    };

                    var updateResult = await _profileManager.UpdateProfileAsync(profile.Id, updateRequest, cancellationToken);
                    if (updateResult.Success)
                    {
                        profile = updateResult.Data!;
                    }
                }
            }

            // Prepare workspace for the profile
            var workspaceConfig = new WorkspaceConfiguration
            {
                Id = profile.Id,
                Manifests = new List<ContentManifest>(),
                GameClient = profile.GameClient,
                Strategy = profile.WorkspaceStrategy,
                ForceRecreate = false,
                ValidateAfterPreparation = true,
            };

            // resolve installation path and workspace root
            var install = await _installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
            if (install.Failed || install.Data == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"Failed to resolve installation '{profile.GameInstallationId}': {install.FirstError}");
            }

            workspaceConfig.BaseInstallationPath = install.Data.InstallationPath;
            workspaceConfig.WorkspaceRootPath = _config.GetWorkspacePath();

            // Build manifests from enabled content IDs
            if (profile.EnabledContentIds != null && profile.EnabledContentIds.Any())
            {
                var resolutionResult = await _dependencyResolver.ResolveDependenciesWithManifestsAsync(profile.EnabledContentIds, cancellationToken);
                if (!resolutionResult.Success)
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure(string.Join(", ", resolutionResult.Errors));
                }

                workspaceConfig.Manifests = resolutionResult.ResolvedManifests.ToList();
                profile.EnabledContentIds = resolutionResult.ResolvedContentIds.ToList();
            }

            var workspaceResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig, cancellationToken: cancellationToken);
            if (workspaceResult.Success && workspaceResult.Data != null)
            {
                profile.ActiveWorkspaceId = workspaceResult.Data.Id;

                // persist ActiveWorkspaceId
                var updateRequest = new UpdateProfileRequest
                {
                    ActiveWorkspaceId = profile.ActiveWorkspaceId,
                };
                await _profileManager.UpdateProfileAsync(profile.Id, updateRequest, cancellationToken);
                _logger.LogInformation("Created workspace {WorkspaceId} for profile {ProfileId}", workspaceResult.Data.Id, profile.Id);
            }

            _logger.LogInformation("Successfully created profile {ProfileId} with workspace", profile.Id);
            return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create profile with workspace");
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to create profile with workspace: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> UpdateProfileWithWorkspaceAsync(string profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating profile {ProfileId} with workspace refresh", profileId);

            // Update the profile
            var updateResult = await _profileManager.UpdateProfileAsync(profileId, request, cancellationToken);
            if (updateResult.Failed)
            {
                return updateResult;
            }

            var profile = updateResult.Data!;

            // If content changed, refresh the workspace
            if (request.EnabledContentIds != null)
            {
                var workspaceConfig = new WorkspaceConfiguration
                {
                    Id = profileId,
                    Manifests = new List<ContentManifest>(),
                    GameClient = profile.GameClient,
                    Strategy = profile.WorkspaceStrategy,
                    ForceRecreate = true, // Force recreate since content changed
                    ValidateAfterPreparation = true,
                };

                // resolve installation path and workspace root
                var install = await _installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
                if (install.Failed || install.Data == null)
                {
                    return ProfileOperationResult<GameProfile>.CreateFailure(
                        $"Failed to resolve installation '{profile.GameInstallationId}': {install.FirstError}");
                }

                workspaceConfig.BaseInstallationPath = install.Data.InstallationPath;
                workspaceConfig.WorkspaceRootPath = _config.GetWorkspacePath();

                // Build manifests from enabled content IDs
                if (profile.EnabledContentIds != null && profile.EnabledContentIds.Any())
                {
                    var resolutionResult = await _dependencyResolver.ResolveDependenciesWithManifestsAsync(profile.EnabledContentIds, cancellationToken);
                    if (!resolutionResult.Success)
                    {
                        return ProfileOperationResult<GameProfile>.CreateFailure(string.Join(", ", resolutionResult.Errors));
                    }

                    workspaceConfig.Manifests = resolutionResult.ResolvedManifests.ToList();
                    profile.EnabledContentIds = resolutionResult.ResolvedContentIds.ToList();
                }

                var workspaceResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig, cancellationToken: cancellationToken);
                if (workspaceResult.Success && workspaceResult.Data != null)
                {
                    profile.ActiveWorkspaceId = workspaceResult.Data.Id;

                    // persist ActiveWorkspaceId
                    var updateRequest = new UpdateProfileRequest
                    {
                        ActiveWorkspaceId = profile.ActiveWorkspaceId,
                    };
                    await _profileManager.UpdateProfileAsync(profile.Id, updateRequest, cancellationToken);
                    _logger.LogInformation("Refreshed workspace {WorkspaceId} for profile {ProfileId}", workspaceResult.Data.Id, profile.Id);
                }
            }

            _logger.LogInformation("Successfully updated profile {ProfileId} with workspace", profileId);
            return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update profile {ProfileId} with workspace", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to update profile with workspace: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> GetProfileWithWorkspaceAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting profile {ProfileId} with workspace information", profileId);

            var profileResult = await _profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed)
            {
                return profileResult;
            }

            var profile = profileResult.Data!;

            // Get workspace information if available
            if (!string.IsNullOrEmpty(profile.ActiveWorkspaceId))
            {
                var allWorkspacesResult = await _workspaceManager.GetAllWorkspacesAsync(cancellationToken);
                if (allWorkspacesResult.Success && allWorkspacesResult.Data != null)
                {
                    var workspace = allWorkspacesResult.Data.FirstOrDefault(w => w.Id == profile.ActiveWorkspaceId);
                    if (workspace != null)
                    {
                        // Could enrich profile with workspace info here
                        _logger.LogDebug("Profile {ProfileId} has active workspace {WorkspaceId}", profileId, profile.ActiveWorkspaceId);
                    }
                }
            }

            return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get profile {ProfileId} with workspace", profileId);
            return ProfileOperationResult<GameProfile>.CreateFailure($"Failed to get profile with workspace: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<IReadOnlyList<ContentManifest>>> DiscoverContentForClientAsync(string gameClientId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Discovering content for game client: {GameClientId}", gameClientId);

            // Get all manifests from the pool
            var manifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
            if (manifestsResult.Failed)
            {
                return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure(string.Join(", ", manifestsResult.Errors));
            }

            // TODO: Implement proper mapping of GameClientId to GameType.
            // This requires retrieving the GameClient by ID from a service and extracting its GameType.
            // For now, return all manifests as a temporary measure until the service is implemented.
            var relevantContent = manifestsResult.Data?.ToList() ?? new List<ContentManifest>();

            _logger.LogInformation("Discovered {Count} content items for game version {GameClientId}", relevantContent.Count, gameClientId);
            return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateSuccess(relevantContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover content for game client {GameClientId}", gameClientId);
            return ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateFailure($"Failed to discover content: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<bool>> ValidateProfileAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating profile {ProfileId}", profile.Id);

            var errors = new List<string>();

            // Basic validation
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                errors.Add("Profile name is required");
            }

            if (string.IsNullOrWhiteSpace(profile.GameInstallationId))
            {
                errors.Add("Game installation is required");
            }

            // Validate that the game installation exists
            if (!string.IsNullOrWhiteSpace(profile.GameInstallationId))
            {
                var installationResult = await _installationService.GetInstallationAsync(profile.GameInstallationId, cancellationToken);
                if (installationResult.Failed)
                {
                    errors.Add($"Game installation not found: {profile.GameInstallationId}");
                }
            }

            // Validate content manifests exist
            if (profile.EnabledContentIds != null && profile.EnabledContentIds.Any())
            {
                var manifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
                if (manifestsResult.Success && manifestsResult.Data != null)
                {
                    var availableManifestIds = manifestsResult.Data.Select(m => m.Id.ToString()).ToHashSet();
                    var missingContent = profile.EnabledContentIds.Where(id => !availableManifestIds.Contains(id)).ToList();

                    if (missingContent.Any())
                    {
                        errors.Add($"Content manifests not found: {string.Join(", ", missingContent)}");
                    }
                }
            }

            if (errors.Any())
            {
                _logger.LogWarning("Profile {ProfileId} validation failed: {Errors}", profile.Id, string.Join(", ", errors));
                return ProfileOperationResult<bool>.CreateFailure(string.Join(", ", errors));
            }

            _logger.LogDebug("Profile {ProfileId} validation successful", profile.Id);
            return ProfileOperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate profile {ProfileId}", profile?.Id);
            return ProfileOperationResult<bool>.CreateFailure($"Profile validation failed: {ex.Message}");
        }
    }
}
