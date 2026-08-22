using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service for managing content-to-profile operations including adding content,
/// detecting conflicts, and creating profiles with pre-enabled content.
/// </summary>
public sealed class ProfileContentService(
    IGameProfileManager profileManager,
    IContentManifestPool manifestPool,
    IDependencyResolver dependencyResolver,
    IGameInstallationService installationService,
    IContentOrchestrator contentOrchestrator,
    INotificationService notificationService,
    ILogger<ProfileContentService> logger) : IProfileContentService
{
    /// <summary>
    /// Content types that are exclusive (only one can be enabled at a time per profile).
    /// </summary>
    private static readonly HashSet<ContentType> ExclusiveContentTypes =
    [
        ContentType.GameClient,
        ContentType.GameInstallation,
    ];

    private sealed record ProfileContentResolution(
        List<string> EnabledContentIds,
        Core.Models.Manifest.ContentManifest? RequiredGameClient,
        GameType? RequiredGameType);

    private sealed record ProfileFoundation(
        List<string> EnabledContentIds,
        GameClient GameClient,
        string InstallationId);

    /// <inheritdoc/>
    public Task<AddToProfileResult> AddContentToProfileAsync(
        string profileId,
        string manifestId,
        CancellationToken cancellationToken = default) =>
        AddContentToProfileAsync(profileId, [manifestId], cancellationToken);

    /// <inheritdoc/>
    public async Task<AddToProfileResult> AddContentToProfileAsync(
        string profileId,
        IReadOnlyList<string> manifestIds,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var requestedIds = (manifestIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedIds.Count == 0)
        {
            return AddToProfileResult.CreateFailure("No content was selected.", sw.Elapsed);
        }

        var primaryManifestId = requestedIds[0];

        try
        {
            logger.LogInformation(
                "Adding {Count} content item(s) starting with {ManifestId} to profile {ProfileId}",
                requestedIds.Count,
                primaryManifestId,
                profileId);

            var contextResult = await LoadProfileAndPrimaryManifestAsync(profileId, primaryManifestId, requestedIds.Count, cancellationToken);
            if (contextResult.Failed)
            {
                return AddToProfileResult.CreateFailure(contextResult.FirstError ?? "Failed to load profile or manifest", sw.Elapsed);
            }

            var (profile, _, contentName) = contextResult.Data;

            var candidateConflictError = await ValidateCandidateSetPairwiseConflictsAsync(requestedIds, cancellationToken);
            if (candidateConflictError != null)
            {
                return AddToProfileResult.CreateFailure(candidateConflictError, sw.Elapsed);
            }

            return await PerformContentAddAndReconciliationAsync(profileId, profile, requestedIds, primaryManifestId, contentName, sw, cancellationToken);
        }
        catch (ManifestNotFoundException ex)
        {
            logger.LogWarning(ex, "Content {ManifestId} not found: {Message}", primaryManifestId, ex.Message);
            return AddToProfileResult.CreateFailure("Content not found. Please download it again and retry.", sw.Elapsed);
        }
        catch (ManifestValidationException ex)
        {
            logger.LogWarning(ex, "Content {ManifestId} validation failed: {Message}", primaryManifestId, ex.Message);
            return AddToProfileResult.CreateFailure("Content validation failed. Please re-download and retry.", sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Add content operation was canceled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error adding content to profile {ProfileId}", profileId);
            return AddToProfileResult.CreateFailure("An unexpected error occurred. Please try again.", sw.Elapsed);
        }
    }

    /// <inheritdoc/>
    public async Task<ContentConflictInfo> CheckContentConflictsAsync(
        string profileId,
        string manifestId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Checking conflicts for adding {ManifestId} to profile {ProfileId}", manifestId, profileId);

            // Get the profile
            var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed || profileResult.Data == null)
            {
                return ContentConflictInfo.NoConflict();
            }

            var profile = profileResult.Data;

            // Get the manifest to add
            var manifestResult = await manifestPool.GetManifestAsync(
                Core.Models.Manifest.ManifestId.Create(manifestId),
                cancellationToken);

            if (manifestResult.Failed || manifestResult.Data == null)
            {
                return ContentConflictInfo.NoConflict();
            }

            var newManifest = manifestResult.Data;

            // Check if this is an exclusive content type
            if (ExclusiveContentTypes.Contains(newManifest.ContentType))
            {
                // Check for existing content of the same exclusive type
                foreach (var existingId in profile.EnabledContentIds ?? [])
                {
                    try
                    {
                        var existingResult = await manifestPool.GetManifestAsync(
                            Core.Models.Manifest.ManifestId.Create(existingId),
                            cancellationToken);

                        if (existingResult.Success && existingResult.Data != null)
                        {
                            var existingManifest = existingResult.Data;

                            if (existingManifest.ContentType == newManifest.ContentType)
                            {
                                // Same exclusive type - conflict
                                if (newManifest.ContentType == ContentType.GameClient)
                                {
                                    return ContentConflictInfo.GameClientConflict(
                                        existingId,
                                        existingManifest.Name);
                                }

                                return ContentConflictInfo.ExclusiveContentConflict(
                                    existingId,
                                    existingManifest.Name,
                                    existingManifest.ContentType);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to check manifest {ExistingId} for conflicts", existingId);
                    }
                }
            }

            // Check for Community Outpost category-specific conflicts (hotkeys, control bars, cameras)
            // These addons are mutually exclusive within their category
            var newContentCode = GetContentCodeFromManifest(newManifest);
            if (!string.IsNullOrEmpty(newContentCode))
            {
                var conflictingCodes = Core.Models.CommunityOutpost.GenPatcherDependencyBuilder.GetConflictingCodes(newContentCode);
                if (conflictingCodes.Count > 0)
                {
                    // Check if any conflicting content is enabled
                    foreach (var existingId in profile.EnabledContentIds ?? [])
                    {
                        try
                        {
                            var existingResult = await manifestPool.GetManifestAsync(
                                Core.Models.Manifest.ManifestId.Create(existingId),
                                cancellationToken);

                            if (existingResult.Success && existingResult.Data != null)
                            {
                                var existingManifest = existingResult.Data;
                                var existingContentCode = GetContentCodeFromManifest(existingManifest);

                                if (!string.IsNullOrEmpty(existingContentCode) &&
                                    conflictingCodes.Contains(existingContentCode, StringComparer.OrdinalIgnoreCase))
                                {
                                    // Found a conflict - return conflict info
                                    logger.LogInformation(
                                        "Content conflict detected: {NewContent} ({NewCode}) conflicts with {ExistingContent} ({ExistingCode})",
                                        newManifest.Name,
                                        newContentCode,
                                        existingManifest.Name,
                                        existingContentCode);

                                    return ContentConflictInfo.ExclusiveContentConflict(
                                        existingId,
                                        existingManifest.Name,
                                        existingManifest.ContentType);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "Failed to check manifest {ExistingId} for category conflicts", existingId);
                        }
                    }
                }
            }

            return ContentConflictInfo.NoConflict();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking conflicts for {ManifestId}", manifestId);
            return ContentConflictInfo.NoConflict();
        }
    }

    /// <inheritdoc/>
    public Task<ProfileOperationResult<GameProfile>> CreateProfileWithContentAsync(
        string profileName,
        string manifestId,
        CancellationToken cancellationToken = default) =>
        CreateProfileWithContentAsync(profileName, [manifestId], cancellationToken);

    /// <inheritdoc/>
    public async Task<ProfileOperationResult<GameProfile>> CreateProfileWithContentAsync(
        string profileName,
        IReadOnlyList<string> manifestIds,
        CancellationToken cancellationToken = default)
    {
        var requestedIds = (manifestIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requestedIds.Count == 0)
        {
            return ProfileOperationResult<GameProfile>.CreateFailure("No content was selected.");
        }

        var manifestId = requestedIds[0];
        try
        {
            logger.LogInformation("Creating new profile '{ProfileName}' with content {ManifestId}", profileName, manifestId);

            var candidateConflictError = await ValidateCandidateSetPairwiseConflictsAsync(requestedIds, cancellationToken);
            if (candidateConflictError != null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(candidateConflictError);
            }

            // Get the manifest to determine game type
            var manifestResult = await manifestPool.GetManifestAsync(
                Core.Models.Manifest.ManifestId.Create(manifestId),
                cancellationToken);

            if (manifestResult.Failed || manifestResult.Data == null)
            {
                var error = manifestResult.FirstError ?? "Failed to retrieve manifest";
                return ProfileOperationResult<GameProfile>.CreateFailure(error);
            }

            var manifest = manifestResult.Data;

            var resolution = await ResolveProfileContentAsync(requestedIds, requestedIds, cancellationToken);
            if (resolution.Failed || resolution.Data == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    resolution.FirstError ?? "Unable to resolve the required content dependencies.");
            }

            List<string> enabledContentIds = resolution.Data.EnabledContentIds;

            // Find a suitable game installation
            var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
            if (installationsResult.Failed || installationsResult.Data == null || installationsResult.Data.Count == 0)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure("No game installations found. Please configure a game installation first.");
            }

            var requiredGameClient = resolution.Data.RequiredGameClient;
            var requiredGameType = resolution.Data.RequiredGameType ?? manifest.TargetGame;

            // Find an installation that satisfies the reconciled content graph, not merely the
            // type of the item the user selected.
            var installation = installationsResult.Data.FirstOrDefault(i =>
                i.AvailableGameClients.Any(c => c.GameType == requiredGameType));

            if (installation == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure(
                    $"No {requiredGameType} installation is available for the required game client.");
            }

            var gameClient = installation.AvailableGameClients
                .FirstOrDefault(c => c.GameType == requiredGameType);

            if (gameClient == null)
            {
                return ProfileOperationResult<GameProfile>.CreateFailure($"No suitable game client found for installation '{installation.InstallationType}'.");
            }

            var profileGameClient = requiredGameClient != null
                ? CreatePublisherGameClient(requiredGameClient, gameClient, installation.Id)
                : null;

            var createRequest = manifest.ContentType.IsStandalone()
                ? BuildStandaloneCreateProfileRequest(profileName, manifest, manifestId, enabledContentIds)
                : BuildStandardCreateProfileRequest(profileName, manifest, installation, gameClient, requiredGameClient, profileGameClient, enabledContentIds);

            var createResult = await profileManager.CreateProfileAsync(createRequest, cancellationToken);
            if (createResult.Failed)
            {
                var error = createResult.FirstError ?? "Failed to create profile";
                logger.LogError("Failed to create profile '{ProfileName}': {Error}", profileName, error);
                return createResult;
            }

            notificationService.ShowSuccess(
                "Profile Created",
                $"Created profile '{profileName}' with {manifest.Name}");

            logger.LogInformation(
                "Successfully created profile {ProfileId} with content {ManifestId}",
                createResult.Data!.Id,
                manifestId);

            return createResult;
        }
        catch (ManifestNotFoundException ex)
        {
            logger.LogWarning("Content {ManifestId} not found: {Message}", manifestId, ex.Message);
            return ProfileOperationResult<GameProfile>.CreateFailure("Content not found. Please download it again and retry.");
        }
        catch (ManifestValidationException ex)
        {
            logger.LogWarning("Content {ManifestId} validation failed: {Message}", manifestId, ex.Message);
            return ProfileOperationResult<GameProfile>.CreateFailure("Content validation failed. Please re-download and retry.");
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Create profile operation was canceled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create profile '{ProfileName}' with content {ManifestId}", profileName, manifestId);
            return ProfileOperationResult<GameProfile>.CreateFailure("Failed to create profile. Please try again.");
        }
    }

    /// <summary>
    /// Validates a profile's enabled content for conflicts.
    /// Returns a list of conflict warnings to display to the user.
    /// </summary>
    /// <param name="profileId">The profile ID to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of conflict warning messages.</returns>
    public async Task<List<string>> ValidateProfileContentAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();

        try
        {
            // Get the profile
            var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
            if (profileResult.Failed || profileResult.Data == null)
            {
                return warnings;
            }

            var profile = profileResult.Data;
            var enabledIds = profile.EnabledContentIds?.ToList() ?? [];

            // Check each pair of enabled content for conflicts
            for (int i = 0; i < enabledIds.Count; i++)
            {
                for (int j = i + 1; j < enabledIds.Count; j++)
                {
                    try
                    {
                        var manifest1Result = await manifestPool.GetManifestAsync(
                            Core.Models.Manifest.ManifestId.Create(enabledIds[i]),
                            cancellationToken);

                        var manifest2Result = await manifestPool.GetManifestAsync(
                            Core.Models.Manifest.ManifestId.Create(enabledIds[j]),
                            cancellationToken);

                        if (manifest1Result.Success && manifest1Result.Data != null &&
                            manifest2Result.Success && manifest2Result.Data != null)
                        {
                            var manifest1 = manifest1Result.Data;
                            var manifest2 = manifest2Result.Data;

                            // Check exclusive content type conflicts
                            if (ExclusiveContentTypes.Contains(manifest1.ContentType) &&
                                manifest1.ContentType == manifest2.ContentType)
                            {
                                warnings.Add($"⚠ Conflict: '{manifest1.Name}' and '{manifest2.Name}' cannot both be enabled ({manifest1.ContentType})");
                            }

                            // Check Community Outpost category conflicts
                            var code1 = GetContentCodeFromManifest(manifest1);
                            var code2 = GetContentCodeFromManifest(manifest2);

                            if (!string.IsNullOrEmpty(code1) && !string.IsNullOrEmpty(code2))
                            {
                                var conflicting1 = Core.Models.CommunityOutpost.GenPatcherDependencyBuilder.GetConflictingCodes(code1);
                                if (conflicting1.Contains(code2, StringComparer.OrdinalIgnoreCase))
                                {
                                    warnings.Add($"⚠ Conflict: '{manifest1.Name}' and '{manifest2.Name}' cannot both be enabled. Please remove one.");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to check conflict between {Id1} and {Id2}", enabledIds[i], enabledIds[j]);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error validating profile content for {ProfileId}", profileId);
        }

        return warnings;
    }

    /// <summary>
    /// Selects the game client that preserves the profile's launch configuration while satisfying
    /// the selected content's game-client requirement.
    /// </summary>
    /// <param name="requiredGameClient">An explicit game client required by selected content, if any.</param>
    /// <param name="currentGameClient">The profile's current game client.</param>
    /// <param name="installationClient">The generic client supplied by the selected installation.</param>
    /// <param name="requiredGameType">The game type required by the selected content.</param>
    /// <param name="installationId">The selected installation identifier.</param>
    /// <returns>The reconciled game client.</returns>
    internal static GameClient SelectReconciledGameClient(
        Core.Models.Manifest.ContentManifest? requiredGameClient,
        GameClient? currentGameClient,
        GameClient installationClient,
        GameType requiredGameType,
        string installationId)
    {
        if (requiredGameClient != null)
        {
            return CreatePublisherGameClient(requiredGameClient, installationClient, installationId);
        }

        if (currentGameClient?.GameType == requiredGameType &&
            string.Equals(currentGameClient.InstallationId, installationId, StringComparison.OrdinalIgnoreCase))
        {
            return currentGameClient;
        }

        return installationClient;
    }

    private static OperationResult<GameType?> DetermineCompatibleGameTypes(
        IReadOnlyList<ContentDependency> installationDependencies,
        ContentManifest? singleGameClient)
    {
        HashSet<GameType>? compatibleGameTypesIntersection = null;
        foreach (var dep in installationDependencies)
        {
            var depSet = new HashSet<GameType>(dep.CompatibleGameTypes);
            if (compatibleGameTypesIntersection == null)
            {
                compatibleGameTypesIntersection = depSet;
            }
            else
            {
                compatibleGameTypesIntersection.IntersectWith(depSet);
            }
        }

        if (singleGameClient is { TargetGame: not GameType.Unknown })
        {
            if (compatibleGameTypesIntersection == null)
            {
                compatibleGameTypesIntersection = [singleGameClient.TargetGame];
            }
            else
            {
                compatibleGameTypesIntersection.IntersectWith([singleGameClient.TargetGame]);
            }
        }

        if (compatibleGameTypesIntersection is { Count: 0 })
        {
            var declaredGameTypes = installationDependencies
                .SelectMany(d => d.CompatibleGameTypes)
                .Distinct()
                .ToList();
            return OperationResult<GameType?>.CreateFailure(
                $"Selected content requires incompatible game installations: {string.Join(", ", declaredGameTypes)}.");
        }

        var requiredGameType = compatibleGameTypesIntersection is { Count: 1 }
            ? compatibleGameTypesIntersection.First()
            : singleGameClient?.TargetGame;

        return OperationResult<GameType?>.CreateSuccess(requiredGameType);
    }

    private static string? CheckPairConflict(ContentManifest m1, ContentManifest m2)
    {
        if (ExclusiveContentTypes.Contains(m1.ContentType) && m1.ContentType == m2.ContentType)
        {
            return $"Selected items contain conflicting exclusive content of type {m1.ContentType}: '{m1.Name}' and '{m2.Name}' cannot be enabled together.";
        }

        var code1 = GetContentCodeFromManifest(m1);
        var code2 = GetContentCodeFromManifest(m2);
        if (!string.IsNullOrEmpty(code1) && !string.IsNullOrEmpty(code2))
        {
            var conflicts = Core.Models.CommunityOutpost.GenPatcherDependencyBuilder.GetConflictingCodes(code1);
            if (conflicts.Contains(code2, StringComparer.OrdinalIgnoreCase))
            {
                return $"Selected items contain conflicting addons: '{m1.Name}' and '{m2.Name}' cannot be enabled together.";
            }
        }

        return null;
    }

    private static bool TryParseCommunityOutpostContentCode(string manifestId, out string contentCode)
    {
        contentCode = string.Empty;
        var parts = manifestId.Split('.');

        if (parts.Length < 5 ||
            !parts[2].Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var codePart = parts[4];
        contentCode = codePart.Length >= 4 ? codePart[..4] : codePart;
        return !string.IsNullOrEmpty(contentCode);
    }

    /// <summary>
    /// Checks whether two five-segment manifest identifiers address the same publisher content
    /// while allowing a publisher to revise the release-version segment.
    /// </summary>
    private static bool HasSameVersionIndependentIdentity(string declaredManifestId, string acquiredManifestId)
    {
        var declaredParts = declaredManifestId.Split('.');
        var acquiredParts = acquiredManifestId.Split('.');
        return DependencyResolver.HasCompatibleCatalogIdentity(declaredParts, acquiredParts);
    }

    /// <summary>
    /// Extracts the content code from a manifest's metadata tags.
    /// Used for Community Outpost content conflict detection.
    /// </summary>
    /// <param name="manifest">The manifest to extract the content code from.</param>
    /// <returns>The content code, or empty string if not found.</returns>
    private static string GetContentCodeFromManifest(Core.Models.Manifest.ContentManifest manifest)
    {
        // Look for contentCode tag in metadata
        var contentCodeTag = manifest.Metadata?.Tags?
            .FirstOrDefault(t => t.StartsWith("contentCode:", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(contentCodeTag))
        {
            return contentCodeTag["contentCode:".Length..];
        }

        // Try to extract from manifest ID
        // Format: 1.version.communityoutpost.contentType.contentName
        var idParts = manifest.Id.Value?.Split('.') ?? [];
        if (idParts.Length >= 5)
        {
            // Community Outpost uses language suffixes (e.g., hleienglish)
            if (idParts[2].Equals(CommunityOutpostConstants.PublisherType, StringComparison.OrdinalIgnoreCase))
            {
                var codePart = idParts[4];
                return codePart.Length >= 4 ? codePart[..4] : codePart;
            }

            return idParts[4];
        }

        return string.Empty;
    }

    private static GameClient CreatePublisherGameClient(
        Core.Models.Manifest.ContentManifest manifest,
        GameClient installationClient,
        string installationId)
    {
        return new GameClient
        {
            Id = manifest.Id.Value,
            Name = manifest.Name,
            Version = manifest.Version,
            GameType = manifest.TargetGame,
            SourceType = ContentType.GameClient,
            PublisherType = manifest.Publisher?.PublisherType,
            ExecutablePath = installationClient.ExecutablePath,
            WorkingDirectory = installationClient.WorkingDirectory,
            InstallationId = installationId,
            CommandLineArgs = installationClient.CommandLineArgs,
            IsEnabled = true,
        };
    }

    /// <summary>
    /// Gets the theme color for a profile based on the publisher of the content it was created
    /// from. Mirrors the publisher logic in GameClientProfileService.GetThemeColorForGameType.
    /// </summary>
    /// <param name="publisherType">The publisher type of the content being added.</param>
    /// <param name="gameType">The target game type.</param>
    /// <returns>The theme color hex string, or null to use the manifest/default color.</returns>
    private static string? GetThemeColorForPublisher(string? publisherType, GameType gameType)
    {
        if (string.IsNullOrEmpty(publisherType))
        {
            return null;
        }

        if (publisherType == PublisherTypeConstants.TheSuperHackers)
        {
            return gameType == GameType.ZeroHour ? SuperHackersConstants.ZeroHourThemeColor : SuperHackersConstants.GeneralsThemeColor;
        }

        if (publisherType == PublisherTypeConstants.GeneralsOnline)
        {
            return GeneralsOnlineConstants.ThemeColor;
        }

        if (publisherType == CommunityOutpostConstants.PublisherType)
        {
            return CommunityOutpostConstants.ThemeColor;
        }

        return null;
    }

    /// <summary>
    /// Gets the cover image path for a profile based on the publisher of the content it was
    /// created from. Known publishers get their faction cover; everything else randomly picks
    /// one of the generic Generals/Zero Hour covers.
    /// </summary>
    /// <param name="publisherType">The publisher type of the content being added.</param>
    /// <returns>An avares:// URI to the cover image.</returns>
    private static string GetCoverPathForPublisher(string? publisherType)
    {
        var baseUri = $"{UriConstants.AvarUriScheme}GenHub{UriConstants.CoversBasePath}";

        if (!string.IsNullOrEmpty(publisherType))
        {
            if (publisherType == PublisherTypeConstants.TheSuperHackers)
            {
                return $"{baseUri}/china-cover.png";
            }

            if (publisherType == CommunityOutpostConstants.PublisherType)
            {
                return $"{baseUri}/gla-cover.png";
            }

            if (publisherType == PublisherTypeConstants.GeneralsOnline)
            {
                return $"{baseUri}/usa-cover.png";
            }
        }

        // Unknown publishers: pick randomly from the generic covers.
        var genericCovers = new[]
        {
            $"{baseUri}/{UriConstants.GeneralsCoverFilename}",
            $"{baseUri}/generals-cover-2.png",
            $"{baseUri}/{UriConstants.ZeroHourCoverFilename}",
        };
        var index = Random.Shared.Next(genericCovers.Length);
        return genericCovers[index];
    }

    /// <summary>
    /// Gets the default icon path for the given game type.
    /// </summary>
    /// <param name="gameType">The target game type.</param>
    /// <returns>An avares:// URI to the icon image.</returns>
    private static string GetIconPathForGame(GameType gameType)
    {
        var gameIcon = gameType == GameType.Generals
            ? UriConstants.GeneralsIconFilename
            : UriConstants.ZeroHourIconFilename;

        return $"{UriConstants.AvarUriScheme}GenHub{UriConstants.IconsBasePath}/{gameIcon}";
    }

    private async Task<OperationResult<(GameProfile Profile, ContentManifest Manifest, string ContentName)>> LoadProfileAndPrimaryManifestAsync(
        string profileId,
        string primaryManifestId,
        int requestedCount,
        CancellationToken cancellationToken)
    {
        var profileResult = await profileManager.GetProfileAsync(profileId, cancellationToken);
        if (profileResult.Failed || profileResult.Data == null)
        {
            var error = profileResult.FirstError ?? "Profile not found";
            logger.LogWarning("Failed to get profile {ProfileId}: {Error}", profileId, error);
            return OperationResult<(GameProfile, ContentManifest, string)>.CreateFailure(error);
        }

        var manifestResult = await manifestPool.GetManifestAsync(
            Core.Models.Manifest.ManifestId.Create(primaryManifestId),
            cancellationToken);

        if (manifestResult.Failed || manifestResult.Data == null)
        {
            var error = manifestResult.FirstError ?? "Failed to retrieve manifest";
            logger.LogWarning("Failed to get manifest {ManifestId}: {Error}", primaryManifestId, error);
            return OperationResult<(GameProfile, ContentManifest, string)>.CreateFailure(error);
        }

        var manifest = manifestResult.Data;
        var contentName = requestedCount > 1
            ? $"{manifest.Name ?? primaryManifestId} + {requestedCount - 1} more"
            : manifest.Name ?? primaryManifestId;

        return OperationResult<(GameProfile, ContentManifest, string)>.CreateSuccess((profileResult.Data, manifest, contentName));
    }

    private async Task<AddToProfileResult> PerformContentAddAndReconciliationAsync(
        string profileId,
        GameProfile profile,
        List<string> requestedIds,
        string primaryManifestId,
        string contentName,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        GameClient? reconciledGameClient = null;
        string? reconciledInstallationId = null;

        var enabledContentIds = new List<string>(profile.EnabledContentIds ?? []);
        var swapResult = await ResolveContentSwapsAsync(profileId, requestedIds, enabledContentIds, cancellationToken);

        var previousIds = new HashSet<string>(enabledContentIds, StringComparer.OrdinalIgnoreCase);
        var resolution = await ResolveProfileContentAsync(enabledContentIds, requestedIds, cancellationToken);
        if (resolution.Failed || resolution.Data == null)
        {
            return AddToProfileResult.CreateFailure(
                resolution.FirstError ?? "Unable to resolve the required content dependencies.",
                sw.Elapsed);
        }

        enabledContentIds = resolution.Data.EnabledContentIds;
        NotifyNewlyAddedDependencies(enabledContentIds, previousIds, contentName, primaryManifestId, cancellationToken);

        if (resolution.Data.RequiredGameType != null)
        {
            var foundation = await ReconcileGameFoundationAsync(
                enabledContentIds,
                profile.GameClient,
                resolution.Data.RequiredGameClient,
                resolution.Data.RequiredGameType.Value,
                cancellationToken);
            if (foundation.Failed || foundation.Data == null)
            {
                return AddToProfileResult.CreateFailure(
                    foundation.FirstError ?? "Unable to reconcile the profile's game foundation.",
                    sw.Elapsed);
            }

            enabledContentIds = foundation.Data.EnabledContentIds;
            reconciledGameClient = foundation.Data.GameClient;
            reconciledInstallationId = foundation.Data.InstallationId;
        }

        var updateRequest = new UpdateProfileRequest
        {
            EnabledContentIds = enabledContentIds,
            GameClient = reconciledGameClient,
            GameInstallationId = reconciledInstallationId,
        };

        var updateResult = await profileManager.UpdateProfileAsync(profileId, updateRequest, cancellationToken);
        if (updateResult.Failed)
        {
            var error = updateResult.FirstError ?? "Failed to update profile";
            logger.LogError("Failed to update profile {ProfileId}: {Error}", profileId, error);
            return AddToProfileResult.CreateFailure(error, sw.Elapsed);
        }

        if (!string.IsNullOrEmpty(swapResult.SwappedContentId))
        {
            notificationService.ShowInfo(
                "Content Replaced",
                $"Replaced '{swapResult.SwappedContentName ?? swapResult.SwappedContentId}' with '{contentName}'");

            logger.LogInformation(
                "Content swap complete: {OldContent} → {NewContent} in profile {ProfileId}",
                swapResult.SwappedContentId,
                primaryManifestId,
                profileId);

            return AddToProfileResult.CreateSuccessWithSwap(
                primaryManifestId,
                contentName,
                swapResult.SwappedContentId,
                swapResult.SwappedContentName,
                swapResult.SwappedContentType,
                sw.Elapsed);
        }

        logger.LogInformation(
            "Successfully added content {ManifestId} to profile {ProfileId}",
            primaryManifestId,
            profileId);

        return AddToProfileResult.CreateSuccess(primaryManifestId, contentName, sw.Elapsed);
    }

    private async Task<(string? SwappedContentId, string? SwappedContentName, ContentType SwappedContentType)> ResolveContentSwapsAsync(
        string profileId,
        List<string> requestedIds,
        List<string> enabledContentIds,
        CancellationToken cancellationToken)
    {
        string? swappedContentId = null;
        string? swappedContentName = null;
        ContentType swappedContentType = ContentType.UnknownContentType;

        foreach (var reqId in requestedIds)
        {
            var conflictInfo = await CheckContentConflictsAsync(profileId, reqId, cancellationToken);
            if (conflictInfo.HasConflict && conflictInfo.CanAutoResolve && !string.IsNullOrEmpty(conflictInfo.ConflictingContentId))
            {
                enabledContentIds.Remove(conflictInfo.ConflictingContentId);
                swappedContentId = conflictInfo.ConflictingContentId;
                swappedContentName = conflictInfo.ConflictingContentName;
                swappedContentType = conflictInfo.ConflictingContentType;

                logger.LogInformation(
                    "Swapping content: removing {OldContent} to add {NewContent}",
                    swappedContentId,
                    reqId);
            }
        }

        foreach (var requestedId in requestedIds)
        {
            if (!enabledContentIds.Contains(requestedId, StringComparer.OrdinalIgnoreCase))
            {
                enabledContentIds.Add(requestedId);
            }
        }

        return (swappedContentId, swappedContentName, swappedContentType);
    }

    private void NotifyNewlyAddedDependencies(
        List<string> enabledContentIds,
        HashSet<string> previousIds,
        string contentName,
        string primaryManifestId,
        CancellationToken cancellationToken)
    {
        var newlyAdded = enabledContentIds
            .Where(id => !previousIds.Contains(id))
            .ToList();

        if (newlyAdded.Count > 0)
        {
            Task.Run(
                async () =>
                {
                    var dependencyNames = await GetDependencyNamesAsync(newlyAdded, cancellationToken);
                    logger.LogInformation("Resolved {Count} dependencies for {ManifestId}", newlyAdded.Count, primaryManifestId);
                    notificationService.ShowInfo(
                        "Dependencies Added",
                        $"Added required dependencies for '{contentName}': {string.Join(", ", dependencyNames)}");
                },
                cancellationToken);
        }
    }

    private CreateProfileRequest BuildStandaloneCreateProfileRequest(
        string profileName,
        ContentManifest manifest,
        string manifestId,
        List<string> enabledContentIds)
    {
        logger.LogInformation("Creating standalone profile for {ManifestId} - skipping foundation injection", manifestId);

        var filteredIds = enabledContentIds
            .Where(id =>
                !id.Contains(".gameinstallation.", StringComparison.OrdinalIgnoreCase) &&
                !id.Contains(".gameclient.", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!filteredIds.Contains(manifestId, StringComparer.OrdinalIgnoreCase))
        {
            filteredIds.Add(manifestId);
        }

        return new CreateProfileRequest
        {
            Name = profileName,
            EnabledContentIds = filteredIds,
            Description = $"Profile created with {manifest.Name}",
            ThemeColor = manifest.Metadata?.ThemeColor ?? GetThemeColorForPublisher(manifest.Publisher?.PublisherType, manifest.TargetGame),
            IconPath = PublisherInfoConstants.GetPublisherLogo(manifest.Publisher?.PublisherType ?? manifest.Publisher?.Name, $"{manifest.Id} {manifest.Name}")
                       ?? (manifest.Metadata?.IconUrl != null && !manifest.Metadata.IconUrl.Contains("cover", StringComparison.OrdinalIgnoreCase) && !manifest.Metadata.IconUrl.Contains("poster", StringComparison.OrdinalIgnoreCase) ? manifest.Metadata.IconUrl : GetIconPathForGame(manifest.TargetGame)),
            CoverPath = manifest.Metadata?.CoverUrl ?? GetCoverPathForPublisher(manifest.Publisher?.PublisherType),
        };
    }

    private CreateProfileRequest BuildStandardCreateProfileRequest(
        string profileName,
        ContentManifest manifest,
        Core.Models.GameInstallations.GameInstallation installation,
        GameClient gameClient,
        ContentManifest? requiredGameClient,
        GameClient? profileGameClient,
        List<string> enabledContentIds)
    {
        var gameInstallationManifestId = Core.Models.Manifest.ManifestIdGenerator.GenerateGameInstallationId(
            installation,
            gameClient.GameType,
            gameClient.Version);

        if (!enabledContentIds.Contains(gameInstallationManifestId, StringComparer.OrdinalIgnoreCase))
        {
            enabledContentIds.Insert(0, gameInstallationManifestId);
            logger.LogInformation("Added GameInstallation manifest {ManifestId} to enabled content", gameInstallationManifestId);
        }

        if (requiredGameClient == null &&
            !string.IsNullOrEmpty(gameClient.Id) &&
            !enabledContentIds.Contains(gameClient.Id, StringComparer.OrdinalIgnoreCase))
        {
            enabledContentIds.Insert(1, gameClient.Id);
            logger.LogInformation("Added GameClient manifest {ManifestId} to enabled content", gameClient.Id);
        }

        return new CreateProfileRequest
        {
            Name = profileName,
            GameInstallationId = installation.Id,
            GameClientId = profileGameClient?.Id ?? gameClient.Id,
            GameClient = profileGameClient,
            EnabledContentIds = enabledContentIds,
            Description = $"Profile created with {manifest.Name}",
            ThemeColor = manifest.Metadata?.ThemeColor ?? GetThemeColorForPublisher(manifest.Publisher?.PublisherType, manifest.TargetGame),
            IconPath = PublisherInfoConstants.GetPublisherLogo(manifest.Publisher?.PublisherType ?? manifest.Publisher?.Name, $"{manifest.Id} {manifest.Name}")
                       ?? (manifest.Metadata?.IconUrl != null && !manifest.Metadata.IconUrl.Contains("cover", StringComparison.OrdinalIgnoreCase) && !manifest.Metadata.IconUrl.Contains("poster", StringComparison.OrdinalIgnoreCase) ? manifest.Metadata.IconUrl : GetIconPathForGame(manifest.TargetGame)),
            CoverPath = manifest.Metadata?.CoverUrl ?? GetCoverPathForPublisher(manifest.Publisher?.PublisherType),
        };
    }

    private async Task<OperationResult<ProfileContentResolution>> ResolveProfileContentAsync(
        IEnumerable<string> contentIds,
        IReadOnlyList<string> requestedContentIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var acquisition = await AcquireRequiredDependenciesAsync(requestedContentIds, cancellationToken);
            if (acquisition.Failed || acquisition.Data == null)
            {
                return OperationResult<ProfileContentResolution>.CreateFailure(
                    acquisition.FirstError ?? "A required dependency could not be acquired.");
            }

            // Include the canonical manifest IDs acquired above. The generic dependency resolver
            // intentionally skips semantic AutoInstall references, so relying on its output alone
            // would download a dependency without enabling it in the profile.
            var contentIdsWithAcquiredDependencies = contentIds
                .Concat(acquisition.Data)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var completeResolution = await dependencyResolver.ResolveDependenciesWithManifestsAsync(
                contentIdsWithAcquiredDependencies,
                cancellationToken);
            if (completeResolution.Failed)
            {
                return OperationResult<ProfileContentResolution>.CreateFailure(
                    completeResolution.FirstError ?? "Required content dependencies could not be resolved.");
            }

            // Resolve the selected item's closure independently. Existing profile content must
            // not decide the new foundation; only the selected item and its dependencies do.
            var requestedResolution = await dependencyResolver.ResolveDependenciesWithManifestsAsync(
                acquisition.Data,
                cancellationToken);
            if (requestedResolution.Failed)
            {
                return OperationResult<ProfileContentResolution>.CreateFailure(
                    requestedResolution.FirstError ?? "The selected content's dependencies could not be resolved.");
            }

            var requiredGameClients = requestedResolution.ResolvedManifests
                .Where(candidate => candidate.ContentType == ContentType.GameClient)
                .GroupBy(candidate => candidate.Id.Value, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (requiredGameClients.Count > 1)
            {
                return OperationResult<ProfileContentResolution>.CreateFailure(
                    $"Selected content requires incompatible game clients: {string.Join(", ", requiredGameClients.Select(client => client.Name ?? client.Id.Value))}.");
            }

            var installationDependencies = requestedResolution.ResolvedManifests
                .SelectMany(candidate => candidate.Dependencies ?? [])
                .Where(dependency =>
                    dependency.DependencyType == ContentType.GameInstallation &&
                    (dependency.InstallBehavior == DependencyInstallBehavior.RequireExisting ||
                     dependency.InstallBehavior == DependencyInstallBehavior.AutoInstall) &&
                    dependency.CompatibleGameTypes != null &&
                    dependency.CompatibleGameTypes.Count > 0)
                .ToList();

            var gameTypeResult = DetermineCompatibleGameTypes(installationDependencies, requiredGameClients.SingleOrDefault());
            if (gameTypeResult.Failed)
            {
                return OperationResult<ProfileContentResolution>.CreateFailure(
                    gameTypeResult.FirstError ?? "Selected content requires incompatible game installations.");
            }

            var requiredGameType = gameTypeResult.Data;

            var enabledContentIds = completeResolution.ResolvedContentIds
                .Concat(acquisition.Data)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var requestedId in requestedContentIds.Where(id => !enabledContentIds.Contains(id, StringComparer.OrdinalIgnoreCase)))
            {
                enabledContentIds.Add(requestedId);
            }

            return OperationResult<ProfileContentResolution>.CreateSuccess(
                new ProfileContentResolution(
                    enabledContentIds,
                    requiredGameClients.SingleOrDefault(),
                    requiredGameType));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve profile content dependencies for {ManifestId}", string.Join(", ", requestedContentIds));
            return OperationResult<ProfileContentResolution>.CreateFailure(
                "Unable to resolve the required content dependencies.");
        }
    }

    /// <summary>
    /// Acquires every required <see cref="DependencyInstallBehavior.AutoInstall"/> dependency
    /// before resolving the profile closure. The dependency resolver can only include manifests
    /// that are already present in the pool, so acquiring after resolution made transitive content
    /// such as Control Bar Pro Core invisible to the resolver and produced broken launch profiles.
    /// </summary>
    private async Task<OperationResult<List<string>>> AcquireRequiredDependenciesAsync(
        IEnumerable<string> rootManifestIds,
        CancellationToken cancellationToken)
    {
        var pendingIds = new Queue<string>(rootManifestIds);
        var visitedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var acquiredContentIds = new List<string>();

        while (pendingIds.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestId = pendingIds.Dequeue();
            if (!visitedIds.Add(manifestId))
            {
                continue;
            }

            var manifestResult = await manifestPool.GetManifestAsync(
                Core.Models.Manifest.ManifestId.Create(manifestId),
                cancellationToken);
            if (manifestResult.Failed || manifestResult.Data == null)
            {
                return OperationResult<List<string>>.CreateFailure($"Required content '{manifestId}' is not available.");
            }

            acquiredContentIds.Add(manifestResult.Data.Id.Value);

            foreach (var dependency in manifestResult.Data.Dependencies ?? [])
            {
                if (dependency.IsOptional || dependency.InstallBehavior != DependencyInstallBehavior.AutoInstall)
                {
                    continue;
                }

                var dependencyId = dependency.Id.Value;
                var dependencyManifest = await GetOrAcquireDependencyManifestAsync(dependencyId, cancellationToken);
                if (dependencyManifest == null)
                {
                    return OperationResult<List<string>>.CreateFailure(
                        $"'{manifestResult.Data.Name ?? manifestId}' requires '{dependency.Name}', but it could not be downloaded automatically.");
                }

                // Dependencies are frequently declared with a stable logical ID while publishers
                // encode a release version in the actual manifest ID. Continue the closure from
                // the manifest that is really present in the pool, not the stale declaration.
                pendingIds.Enqueue(dependencyManifest.Id.Value);
            }
        }

        return OperationResult<List<string>>.CreateSuccess(acquiredContentIds);
    }

    private async Task<OperationResult<ProfileFoundation>> ReconcileGameFoundationAsync(
        List<string> enabledContentIds,
        GameClient? currentGameClient,
        Core.Models.Manifest.ContentManifest? requiredGameClient,
        GameType requiredGameType,
        CancellationToken cancellationToken)
    {
        var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
        var installations = installationsResult.Success && installationsResult.Data != null
            ? installationsResult.Data
            : [];

        var matchingInstallation = !string.IsNullOrEmpty(currentGameClient?.InstallationId)
            ? installations.FirstOrDefault(c => string.Equals(c.Id, currentGameClient.InstallationId, StringComparison.OrdinalIgnoreCase) &&
                                                c.AvailableGameClients.Any(client => client.GameType == requiredGameType))
            : null;

        var installation = matchingInstallation ?? installations.FirstOrDefault(candidate =>
            candidate.AvailableGameClients.Any(client => client.GameType == requiredGameType));

        var installationClient = installation?.AvailableGameClients
            .FirstOrDefault(client => client.GameType == requiredGameType);

        if (installation == null || installationClient == null)
        {
            return OperationResult<ProfileFoundation>.CreateFailure(
                $"No {requiredGameType} installation is available for '{requiredGameClient?.Name ?? "the required content"}'.");
        }

        var nonFoundationContentIds = new List<string>();
        foreach (var contentId in enabledContentIds)
        {
            if (string.Equals(contentId, currentGameClient?.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var manifestResult = await manifestPool.GetManifestAsync(
                Core.Models.Manifest.ManifestId.Create(contentId),
                cancellationToken);
            if (manifestResult.Success && manifestResult.Data != null &&
                (manifestResult.Data.ContentType == ContentType.GameClient ||
                 manifestResult.Data.ContentType == ContentType.GameInstallation))
            {
                continue;
            }

            nonFoundationContentIds.Add(contentId);
        }

        var installationManifestId = Core.Models.Manifest.ManifestIdGenerator.GenerateGameInstallationId(
            installation,
            requiredGameType,
            installationClient.Version);

        // An add-on can require a game installation without requiring a different client.
        // Keep a compatible existing publisher client in that case; replacing it with the
        // installation's generic client discards its launch configuration and can make an
        // otherwise healthy profile unlaunchable.
        var reconciledGameClient = SelectReconciledGameClient(
            requiredGameClient,
            currentGameClient,
            installationClient,
            requiredGameType,
            installation.Id);
        var reconciledIds = new List<string> { installationManifestId, reconciledGameClient.Id };
        reconciledIds.AddRange(nonFoundationContentIds.Where(id =>
            !string.Equals(id, installationManifestId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(id, reconciledGameClient.Id, StringComparison.OrdinalIgnoreCase)));

        return OperationResult<ProfileFoundation>.CreateSuccess(
            new ProfileFoundation(
                reconciledIds,
                reconciledGameClient,
                installation.Id));
    }

    private async Task<List<string>> GetDependencyNamesAsync(
        IEnumerable<string> dependencyIds,
        CancellationToken cancellationToken)
    {
        var dependencyNames = new List<string>();
        foreach (var dependencyId in dependencyIds)
        {
            var dependencyManifest = await GetOrAcquireDependencyManifestAsync(dependencyId, cancellationToken);
            if (dependencyManifest != null)
            {
                dependencyNames.Add(dependencyManifest.Name ?? "Required dependency");
            }
            else
            {
                logger.LogWarning("Dependency {DependencyId} could not be auto-acquired", dependencyId);
                if (TryParseCommunityOutpostContentCode(dependencyId, out var contentCode))
                {
                    var metadata = Core.Models.CommunityOutpost.GenPatcherContentRegistry.GetMetadata(contentCode);
                    dependencyNames.Add(string.IsNullOrEmpty(metadata.DisplayName) ? "Required dependency" : metadata.DisplayName);
                }
                else
                {
                    dependencyNames.Add("Required dependency");
                }
            }
        }

        return dependencyNames;
    }

    /// <summary>
    /// Gets an acquired dependency or acquires it from its publisher when it is not yet present.
    /// A dependency declaration's version segment is not always the stored artifact's release
    /// version, so this returns the canonical manifest rather than a boolean success flag.
    /// </summary>
    private async Task<Core.Models.Manifest.ContentManifest?> GetOrAcquireDependencyManifestAsync(
        string manifestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await manifestPool.GetManifestAsync(
                Core.Models.Manifest.ManifestId.Create(manifestId),
                cancellationToken);

            if (existing.Success && existing.Data != null)
            {
                return existing.Data;
            }

            var equivalentManifest = await FindEquivalentAcquiredManifestAsync(manifestId, cancellationToken);
            if (equivalentManifest != null)
            {
                logger.LogInformation(
                    "Using acquired manifest {ResolvedManifestId} to satisfy dependency {DeclaredManifestId}",
                    equivalentManifest.Id,
                    manifestId);
                return equivalentManifest;
            }

            if (!TryParseCommunityOutpostContentCode(manifestId, out var contentCode))
            {
                return null;
            }

            var metadata = Core.Models.CommunityOutpost.GenPatcherContentRegistry.GetMetadata(contentCode);
            if (metadata.IsBaseDependency && !string.IsNullOrWhiteSpace(metadata.OutputFilename))
            {
                var allManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
                if (allManifestsResult.Success && allManifestsResult.Data != null)
                {
                    var bundlingManifest = allManifestsResult.Data.FirstOrDefault(manifest =>
                        manifest.Files.Any(file => string.Equals(
                            System.IO.Path.GetFileName(file.RelativePath),
                            metadata.OutputFilename,
                            StringComparison.OrdinalIgnoreCase)));

                    if (bundlingManifest != null)
                    {
                        logger.LogInformation(
                            "Satisfied base dependency {DeclaredManifestId} using acquired manifest {ResolvedManifestId} containing payload {Filename}",
                            manifestId,
                            bundlingManifest.Id,
                            metadata.OutputFilename);
                        return bundlingManifest;
                    }
                }

                // Base deps are hidden from catalog search — acquire them directly.
                var directAcquire = await AcquireCommunityOutpostContentByCodeAsync(
                    contentCode,
                    metadata,
                    cancellationToken);
                if (directAcquire != null)
                {
                    return directAcquire;
                }
            }

            // Provider discoverers match on PublisherType ("communityoutpost"), not
            // PublisherId ("community-outpost"). Using the wrong id yields
            // "No enabled providers available for search".
            var query = new ContentSearchQuery
            {
                ProviderName = CommunityOutpostConstants.PublisherType,
                SearchTerm = contentCode,
                IncludeInstalled = true,
                Take = 50,
            };

            var searchResult = await contentOrchestrator.SearchAsync(query, cancellationToken);
            if (searchResult.Failed || searchResult.Data == null)
            {
                // Catalog search can still miss base deps; fall back to a direct acquire.
                return await AcquireCommunityOutpostContentByCodeAsync(
                    contentCode,
                    metadata,
                    cancellationToken);
            }

            var match = searchResult.Data.FirstOrDefault(r =>
                r.Id?.EndsWith($".{contentCode}", StringComparison.OrdinalIgnoreCase) == true ||
                (r.ResolverMetadata?.TryGetValue("contentCode", out var discoveredCode) == true &&
                 discoveredCode.Equals(contentCode, StringComparison.OrdinalIgnoreCase)));

            if (match == null)
            {
                return await AcquireCommunityOutpostContentByCodeAsync(
                    contentCode,
                    metadata,
                    cancellationToken);
            }

            var acquireResult = await contentOrchestrator.AcquireContentAsync(match, null, cancellationToken);
            return acquireResult.Success ? acquireResult.Data : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to auto-acquire dependency {ManifestId}", manifestId);
            return null;
        }
    }

    /// <summary>
    /// Finds an acquired manifest that represents the same logical dependency when a publisher
    /// changed only its release-version segment. Community Outpost is matched by its authoritative
    /// <c>contentCode</c> metadata so language-suffixed artifacts such as <c>hlenenglish</c> also
    /// reconcile to the declared <c>hlen</c> dependency.
    /// </summary>
    private async Task<Core.Models.Manifest.ContentManifest?> FindEquivalentAcquiredManifestAsync(
        string declaredManifestId,
        CancellationToken cancellationToken)
    {
        var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (manifestsResult.Failed || manifestsResult.Data == null)
        {
            return null;
        }

        if (TryParseCommunityOutpostContentCode(declaredManifestId, out var contentCode))
        {
            return manifestsResult.Data.FirstOrDefault(manifest =>
                string.Equals(
                    manifest.Publisher?.PublisherType,
                    CommunityOutpostConstants.PublisherType,
                    StringComparison.OrdinalIgnoreCase) &&
                GetContentCodeFromManifest(manifest).Equals(contentCode, StringComparison.OrdinalIgnoreCase));
        }

        return manifestsResult.Data.FirstOrDefault(manifest =>
            HasSameVersionIndependentIdentity(declaredManifestId, manifest.Id.Value));
    }

    /// <summary>
    /// Builds a synthetic catalog hit and acquires it for Community Outpost content that is
    /// filtered from browse search (base dependencies such as cbpc / cben / cbbs).
    /// </summary>
    private async Task<Core.Models.Manifest.ContentManifest?> AcquireCommunityOutpostContentByCodeAsync(
        string contentCode,
        GenPatcherContentMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contentCode) ||
            metadata.ContentType == ContentType.UnknownContentType ||
            !GenPatcherContentRegistry.IsKnownCode(contentCode))
        {
            return null;
        }

        var publisher = CommunityOutpostConstants.PublisherType.ToLowerInvariant();
        var contentType = metadata.ContentType.ToString().ToLowerInvariant();
        var downloadUrl = $"{CommunityOutpostCatalogConstants.DefaultFilesBaseUrl.TrimEnd('/')}/{contentCode}.dat";

        var searchResult = new ContentSearchResult
        {
            Id = $"1.0.{publisher}.{contentType}.{contentCode.ToLowerInvariant()}",
            Name = metadata.DisplayName,
            Description = metadata.Description ?? string.Empty,
            Version = metadata.Version ?? "1.0",
            ContentType = metadata.ContentType,
            TargetGame = metadata.TargetGame,
            ProviderName = CommunityOutpostConstants.PublisherType,
            AuthorName = CommunityOutpostConstants.PublisherName,
            SourceUrl = downloadUrl,
            RequiresResolution = true,
            ResolverId = CommunityOutpostConstants.PublisherId,
            IconUrl = CommunityOutpostConstants.LogoSource,
        };

        searchResult.ResolverMetadata[CommunityOutpostCatalogConstants.ContentCodeKey] = contentCode;
        searchResult.ResolverMetadata[CommunityOutpostCatalogConstants.CategoryKey] = metadata.Category.ToString();

        logger.LogInformation(
            "Directly acquiring Community Outpost dependency {ContentCode} ({Name}) from {Url}",
            contentCode,
            metadata.DisplayName,
            downloadUrl);

        var acquireResult = await contentOrchestrator.AcquireContentAsync(searchResult, null, cancellationToken);
        return acquireResult.Success ? acquireResult.Data : null;
    }

    private async Task<string?> ValidateCandidateSetPairwiseConflictsAsync(
        IReadOnlyList<string> candidateIds,
        CancellationToken cancellationToken)
    {
        if (candidateIds.Count < 2)
        {
            return null;
        }

        var candidateManifests = new List<ContentManifest>();
        foreach (var id in candidateIds)
        {
            var res = await manifestPool.GetManifestAsync(Core.Models.Manifest.ManifestId.Create(id), cancellationToken);
            if (res.Success && res.Data != null)
            {
                candidateManifests.Add(res.Data);
            }
        }

        for (int i = 0; i < candidateManifests.Count; i++)
        {
            for (int j = i + 1; j < candidateManifests.Count; j++)
            {
                var conflict = CheckPairConflict(candidateManifests[i], candidateManifests[j]);
                if (conflict != null)
                {
                    return conflict;
                }
            }
        }

        return null;
    }
}
