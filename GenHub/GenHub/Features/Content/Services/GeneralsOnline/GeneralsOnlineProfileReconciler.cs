using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Service for reconciling profiles when GeneralsOnline updates are detected.
/// When an update is found, this service updates all profiles using GeneralsOnline,
/// removes old manifests and CAS content, and prepares profiles for the new version.
/// </summary>
[method: SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required dependencies for Generals Online reconciliation.")]
public partial class GeneralsOnlineProfileReconciler(
    ILogger<GeneralsOnlineProfileReconciler> logger,
    IGeneralsOnlineUpdateService updateService,
    IContentManifestPool manifestPool,
    IContentOrchestrator contentOrchestrator,
    IContentReconciliationService reconciliationService,
    INotificationService notificationService,
    IDialogService dialogService,
    IUserSettingsService userSettingsService,
    IGameProfileManager profileManager,
    IContentVersionComparer versionComparer,
    IGameInstallationService? installationService = null)
    : IGeneralsOnlineProfileReconciler, IPublisherReconciler
{
    private readonly SemaphoreSlim _reconcileLock = new(1, 1);

    /// <inheritdoc/>
    public string PublisherType => GeneralsOnlineConstants.PublisherType;

    /// <inheritdoc/>
    public async Task<OperationResult<PublisherReconciliationResult>> CheckAndReconcileIfNeededAsync(
        string triggeringProfileId,
        CancellationToken cancellationToken = default)
    {
        await _reconcileLock.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation(
                "[GO Reconciler] Checking for GeneralsOnline updates (triggered by profile: {ProfileId})",
                triggeringProfileId);

            var checkResult = await CheckUpdateAvailabilityAndStrategyAsync(triggeringProfileId, cancellationToken);
            if (!checkResult.Success)
            {
                return OperationResult<PublisherReconciliationResult>.CreateFailure(checkResult.FirstError ?? "Failed to check update availability");
            }

            var (proceed, updateResult, strategy, _, shouldDeleteOldVersions) = checkResult.Data;
            if (!proceed || updateResult == null)
            {
                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.None);
            }

            var progressNotificationId = Guid.NewGuid();
            var progressNotification = new NotificationMessage(
                NotificationType.Info,
                "GeneralsOnline Update",
                $"Installing GeneralsOnline {updateResult.LatestVersion}. Please wait...",
                autoDismissMilliseconds: null,
                isPersistent: true)
            {
                Id = progressNotificationId,
            };
            notificationService.Show(progressNotification);

            try
            {
                var oldManifests = await FindGeneralsOnlineManifestsAsync(cancellationToken);
                if (oldManifests.Count == 0)
                {
                    logger.LogWarning("[GO Reconciler] No existing GeneralsOnline manifests found in pool");
                }

                logger.LogInformation(
                    "[GO Reconciler] Found {Count} existing GeneralsOnline manifests to replace",
                    oldManifests.Count);

                var acquireResult = await AcquireLatestVersionAsync(oldManifests, progressNotificationId, cancellationToken);
                if (!acquireResult.Success)
                {
                    notificationService.ShowError(
                        "GeneralsOnline Update Failed",
                        $"Failed to download update: {acquireResult.FirstError}",
                        NotificationDurations.Critical);

                    return OperationResult<PublisherReconciliationResult>.CreateFailure(
                        $"Failed to acquire new GeneralsOnline version: {acquireResult.FirstError}");
                }

                var newManifests = acquireResult.Data!;
                logger.LogInformation("[GO Reconciler] Successfully acquired {Count} new manifests", newManifests.Count);

                notificationService.Update(
                    progressNotificationId,
                    "Applying update to profiles...",
                    "GeneralsOnline Update");

                var updateResultData = await ApplyUpdateStrategyAsync(strategy, oldManifests, newManifests, updateResult.LatestVersion ?? "Unknown", triggeringProfileId, cancellationToken);
                if (!updateResultData.Success)
                {
                    return OperationResult<PublisherReconciliationResult>.CreateFailure(updateResultData.FirstError ?? "Failed to apply update strategy");
                }

                var (profilesUpdated, anyFailure, manifestMapping, targetProfileId) = updateResultData.Data;

                var enforceResult = await EnforceMapPackDependencyAsync(newManifests, cancellationToken);
                if (!enforceResult.Success)
                {
                    logger.LogWarning("[GO Reconciler] Map pack dependency enforcement had warnings: {Error}", enforceResult.FirstError);
                    anyFailure = true;
                }

                bool deleteOldVersions = (strategy != UpdateStrategy.CreateNewProfile) && shouldDeleteOldVersions;
                await HandleOldManifestsAndCleanupAsync(deleteOldVersions, anyFailure, manifestMapping, oldManifests, cancellationToken);

                if (anyFailure)
                {
                    notificationService.ShowWarning(
                        "Generals Online Updated (Partial)",
                        $"Updated to {updateResult.LatestVersion}, but some profiles or components had issues.",
                        NotificationDurations.VeryLong);
                }
                else
                {
                    notificationService.ShowSuccess(
                        "Generals Online Updated",
                        $"Successfully updated {profilesUpdated} profile(s) to Generals Online {updateResult.LatestVersion}.",
                        NotificationDurations.Long);
                }

                return OperationResult<PublisherReconciliationResult>.CreateSuccess(PublisherReconciliationResult.Success(
                    strategy,
                    targetProfileId ?? triggeringProfileId,
                    profilesUpdated));
            }
            finally
            {
                notificationService.Dismiss(progressNotificationId);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GO Reconciler] Failed to reconcile GeneralsOnline update");
            return OperationResult<PublisherReconciliationResult>.CreateFailure($"GeneralsOnline update reconciliation failed: {ex.Message}");
        }
        finally
        {
            _reconcileLock.Release();
        }
    }

    /// <summary>
    /// Formats an updated profile name by stripping existing version suffixes and appending the new version.
    /// </summary>
    /// <param name="originalName">The existing profile name.</param>
    /// <param name="newVersion">The new version string.</param>
    /// <param name="existingNames">Set of already existing profile names to prevent collisions.</param>
    /// <param name="allowSameAsOriginal">Whether the updated name is allowed to match the original profile name without appending a collision suffix.</param>
    /// <returns>A formatted, unique profile name.</returns>
    internal static string FormatUpdatedProfileName(
        string originalName,
        string newVersion,
        HashSet<string> existingNames,
        bool allowSameAsOriginal = true)
    {
        var baseName = originalName.Trim();
        while (true)
        {
            var stripped = VersionSuffixRegex().Replace(baseName, string.Empty).Trim();
            if (stripped.Length == 0 || stripped == baseName)
            {
                break;
            }

            baseName = stripped;
        }

        var candidate = $"{baseName} v{newVersion}";
        if (allowSameAsOriginal && string.Equals(candidate, originalName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return originalName;
        }

        var finalName = candidate;
        int suffix = 2;
        while (existingNames.Contains(finalName) &&
               (!allowSameAsOriginal || !string.Equals(finalName, originalName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            finalName = $"{candidate} ({suffix++})";
        }

        return finalName;
    }

    /// <summary>
    /// Resolves updated enabled content IDs mapping old manifests to new ones and preserving non-GeneralsOnline content.
    /// </summary>
    /// <param name="profile">The game profile being updated.</param>
    /// <param name="manifestMapping">Mapping from old manifest IDs to new manifest IDs.</param>
    /// <param name="newManifests">The new manifests being applied in the update.</param>
    /// <returns>A list of updated enabled content IDs.</returns>
    internal static List<string> ResolveUpdatedEnabledContent(
        Core.Models.GameProfile.GameProfile profile,
        Dictionary<string, string> manifestMapping,
        List<ContentManifest> newManifests)
    {
        var newEnabledContent = new List<string>();
        if (profile.EnabledContentIds != null)
        {
            foreach (var id in profile.EnabledContentIds)
            {
                if (manifestMapping.TryGetValue(id, out var newId))
                {
                    newEnabledContent.Add(newId);
                }
                else if (!id.Contains($".{GeneralsOnlineConstants.PublisherType}.", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep non-GO content
                    newEnabledContent.Add(id);
                }
            }
        }

        // Ensure all new GO manifests (e.g. MapPack) are included
        newEnabledContent.AddRange(
            newManifests
                .Select(m => m.Id.Value)
                .Where(id => !newEnabledContent.Contains(id, StringComparer.OrdinalIgnoreCase)));

        return newEnabledContent.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [GeneratedRegex(@"\s*(?:\([vV]?[\w\.\-]+(?:\s*QFE\d+)?\)|\b[vV]\d+[\w\.\-]*)\s*$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex VersionSuffixRegex();

    /// <summary>
    /// Checks if two manifests refer to the same variant (30hz, 60hz, quickmatch-maps, or gamedata).
    /// </summary>
    private static bool MatchesByVariant(ContentManifest oldManifest, ContentManifest newManifest)
    {
        var oldVariant = ExtractVariant(oldManifest);
        var newVariant = ExtractVariant(newManifest);
        return string.Equals(oldVariant, newVariant, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the variant suffix from a manifest ID.
    /// </summary>
    private static string? ExtractVariant(string manifestId)
    {
        var parts = manifestId.Split('.');
        if (parts.Length == 0) return null;

        var lastPart = parts[^1];

        if (lastPart.Equals(GeneralsOnlineConstants.Variant60HzSuffix, StringComparison.OrdinalIgnoreCase) ||
            lastPart.Equals(GeneralsOnlineConstants.QuickMatchMapPackSuffix, StringComparison.OrdinalIgnoreCase) ||
            lastPart.Equals(GeneralsOnlineConstants.GameDataPatchSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return lastPart.ToLowerInvariant();
        }

        // Check if any segment is gamedata
        if (parts.Any(p => p.Equals(GeneralsOnlineConstants.GameDataPatchSuffix, StringComparison.OrdinalIgnoreCase)))
        {
            return GeneralsOnlineConstants.GameDataPatchSuffix;
        }

        // Check for legacy ID formats
        if (lastPart.Equals("generalsonlinezh-60", StringComparison.OrdinalIgnoreCase) ||
            lastPart.Equals("generalsonlinezh", StringComparison.OrdinalIgnoreCase))
        {
            return GeneralsOnlineConstants.Variant60HzSuffix;
        }

        // Check for map pack variations
        if (parts.Any(p => p.Contains("quickmatchmaps", StringComparison.OrdinalIgnoreCase) ||
                           p.Contains("generalsonlinemaps", StringComparison.OrdinalIgnoreCase) ||
                           p.Contains("mappack", StringComparison.OrdinalIgnoreCase)))
        {
            return GeneralsOnlineConstants.QuickMatchMapPackSuffix;
        }

        // If it's a gameclient ID with zerohour
        if (parts.Any(p => p.Equals("gameclient", StringComparison.OrdinalIgnoreCase)) &&
            parts.Any(p => p.Equals("zerohour", StringComparison.OrdinalIgnoreCase)))
        {
            return GeneralsOnlineConstants.Variant60HzSuffix;
        }

        // Fallback for legacy ID formats or unrecognized patterns.
        // We take the last dot-separated segment as the variant.
        return parts.Length > 1 ? lastPart.ToLowerInvariant() : null;
    }

    /// <summary>
    /// Extracts the variant suffix from a manifest, checking tags first for explicit variant detection.
    /// </summary>
    /// <param name="manifest">The manifest to extract variant from.</param>
    /// <returns>The variant suffix, or null if not detected.</returns>
    private static string? ExtractVariant(ContentManifest manifest)
    {
        // First, check for explicit variant tags in metadata (preferred method for new manifests)
        if (manifest.Metadata?.Tags != null)
        {
            foreach (var tag in manifest.Metadata.Tags)
            {
                if (tag.Equals(GeneralsOnlineVariantTags.Tag60Hz, StringComparison.OrdinalIgnoreCase))
                {
                    return GeneralsOnlineConstants.Variant60HzSuffix;
                }

                if (tag.Equals(GeneralsOnlineVariantTags.TagQuickMatchMaps, StringComparison.OrdinalIgnoreCase))
                {
                    return GeneralsOnlineConstants.QuickMatchMapPackSuffix;
                }

                if (tag.Equals(GeneralsOnlineVariantTags.TagGameData, StringComparison.OrdinalIgnoreCase))
                {
                    return GeneralsOnlineConstants.GameDataPatchSuffix;
                }
            }
        }

        // Fallback to explicit metadata if available (Check TargetGame for default variant association)
        if (manifest.TargetGame == GameType.ZeroHour && manifest.ContentType == ContentType.GameClient)
        {
            return GeneralsOnlineConstants.DefaultVariantSuffix;
        }

        // Fallback to ID-based detection for legacy manifests
        return ExtractVariant(manifest.Id.Value);
    }

    /// <summary>
    /// Groups a collection of manifests by their variant suffix.
    /// </summary>
    /// <param name="manifests">The manifests to group.</param>
    /// <returns>A dictionary mapping variant suffix to list of manifests.</returns>
    private static Dictionary<string, List<ContentManifest>> GroupManifestsByVariant(IEnumerable<ContentManifest> manifests)
    {
        var byVariant = new Dictionary<string, List<ContentManifest>>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests)
        {
            var variant = ExtractVariant(manifest);
            if (variant != null)
            {
                if (!byVariant.TryGetValue(variant, out var list))
                {
                    list = [];
                    byVariant[variant] = list;
                }

                list.Add(manifest);
            }
        }

        return byVariant;
    }

    /// <summary>
    /// Finds the best matching new manifest candidate for an old manifest by content type and latest version.
    /// </summary>
    /// <param name="candidates">The candidate new manifests.</param>
    /// <param name="oldManifest">The old manifest being replaced.</param>
    /// <param name="versionComparer">Optional comparer for version sorting.</param>
    /// <returns>The matching candidate manifest, or null if none found.</returns>
    private static ContentManifest? FindMatchingCandidate(
        List<ContentManifest> candidates,
        ContentManifest oldManifest,
        IComparer<string>? versionComparer)
    {
        return candidates
            .Where(c => c.ContentType == oldManifest.ContentType ||
                        (oldManifest.ContentType == ContentType.Mod && c.ContentType == ContentType.GameClient))
            .OrderByDescending(c => c.Version, versionComparer ?? Comparer<string>.Default)
            .FirstOrDefault();
    }

    /// <summary>
    /// Builds an updated GameClient model based on the new client manifest and the existing profile's client settings.
    /// </summary>
    private static Core.Models.GameClients.GameClient BuildUpdatedGameClient(
        Core.Models.GameProfile.GameProfile profile,
        ContentManifest? newClientManifest,
        string newVersion)
    {
        var workingDir = !string.IsNullOrEmpty(profile.GameClient?.WorkingDirectory)
            ? profile.GameClient.WorkingDirectory
            : string.Empty;

        var executableFile = newClientManifest?.Files?.FirstOrDefault(f =>
            f.RelativePath?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);
        var relativeExe = executableFile?.RelativePath ?? GeneralsOnlineConstants.DefaultExecutableFileName;
        var exePath = !string.IsNullOrEmpty(workingDir)
            ? Path.Combine(workingDir, relativeExe)
            : (profile.GameClient?.ExecutablePath ?? relativeExe);

        return new Core.Models.GameClients.GameClient
        {
            Id = newClientManifest?.Id.Value ?? profile.GameClient?.Id ?? string.Empty,
            Name = newClientManifest?.Name ?? profile.GameClient?.Name ?? GameClientConstants.GeneralsOnline60HzDisplayName,
            Version = newClientManifest?.Version ?? newVersion,
            GameType = newClientManifest?.TargetGame ?? profile.GameClient?.GameType ?? GameType.ZeroHour,
            SourceType = newClientManifest?.ContentType ?? profile.GameClient?.SourceType ?? ContentType.GameClient,
            PublisherType = newClientManifest?.Publisher?.PublisherType ?? profile.GameClient?.PublisherType ?? GeneralsOnlineConstants.PublisherType,
            InstallationId = profile.GameInstallationId ?? profile.GameClient?.InstallationId ?? string.Empty,
            ExecutablePath = exePath,
            WorkingDirectory = workingDir,
            CommandLineArgs = profile.GameClient?.CommandLineArgs ?? string.Empty,
        };
    }

    /// <summary>
    /// Builds a CreateProfileRequest cloning the existing profile's settings with the updated client and content.
    /// </summary>
    private static Core.Models.GameProfile.CreateProfileRequest BuildCloneProfileRequest(
        Core.Models.GameProfile.GameProfile profile,
        string targetProfileName,
        Core.Models.GameClients.GameClient updatedGameClient,
        List<string> newEnabledContent,
        ContentManifest? newClientManifest)
    {
        var iconPath = !string.IsNullOrEmpty(profile.IconPath) &&
                       !profile.IconPath.Contains(UriConstants.GenHubIconMarker) &&
                       !profile.IconPath.Contains(UriConstants.ZeroHourIconMarker)
            ? profile.IconPath
            : (newClientManifest?.Metadata?.IconUrl ?? GeneralsOnlineConstants.LogoSource);

        var coverPath = !string.IsNullOrEmpty(profile.CoverPath) &&
                        !profile.CoverPath.Contains(UriConstants.ZeroHourCoverMarker)
            ? profile.CoverPath
            : (newClientManifest?.Metadata?.CoverUrl ?? GeneralsOnlineConstants.CoverSource);

        var themeColor = !string.IsNullOrEmpty(profile.ThemeColor)
            ? profile.ThemeColor
            : (newClientManifest?.Metadata?.ThemeColor ?? GeneralsOnlineConstants.ThemeColor);

        var description = !string.IsNullOrEmpty(profile.Description)
            ? profile.Description
            : (newClientManifest?.Metadata?.Description ?? GeneralsOnlineConstants.ShortDescription);

        return new Core.Models.GameProfile.CreateProfileRequest
        {
            Name = targetProfileName,
            Description = description,
            GameInstallationId = profile.GameInstallationId,
            GameClientId = updatedGameClient.Id,
            GameClient = updatedGameClient,
            WorkspaceStrategy = profile.WorkspaceStrategy,
            EnabledContentIds = newEnabledContent,
            ThemeColor = themeColor,
            IconPath = iconPath,
            CoverPath = coverPath,
            CommandLineArguments = profile.CommandLineArguments,
            GameSpyIPAddress = profile.GameSpyIPAddress,
            UseSteamLaunch = profile.UseSteamLaunch,

            // Video Settings
            VideoResolutionWidth = profile.VideoResolutionWidth,
            VideoResolutionHeight = profile.VideoResolutionHeight,
            VideoWindowed = profile.VideoWindowed,
            VideoTextureQuality = profile.VideoTextureQuality,
            EnableVideoShadows = profile.EnableVideoShadows,
            VideoParticleEffects = profile.VideoParticleEffects,
            VideoExtraAnimations = profile.VideoExtraAnimations,
            VideoBuildingAnimations = profile.VideoBuildingAnimations,
            VideoGamma = profile.VideoGamma,
            VideoAlternateMouseSetup = profile.VideoAlternateMouseSetup,
            VideoHeatEffects = profile.VideoHeatEffects,
            VideoStaticGameLOD = profile.VideoStaticGameLOD,
            VideoIdealStaticGameLOD = profile.VideoIdealStaticGameLOD,
            VideoUseDoubleClickAttackMove = profile.VideoUseDoubleClickAttackMove,
            VideoScrollFactor = profile.VideoScrollFactor,
            VideoRetaliation = profile.VideoRetaliation,
            VideoDynamicLOD = profile.VideoDynamicLOD,
            VideoMaxParticleCount = profile.VideoMaxParticleCount,
            VideoAntiAliasing = profile.VideoAntiAliasing,
            VideoSkipEALogo = profile.VideoSkipEALogo,

            // Audio Settings
            AudioSoundVolume = profile.AudioSoundVolume,
            AudioThreeDSoundVolume = profile.AudioThreeDSoundVolume,
            AudioSpeechVolume = profile.AudioSpeechVolume,
            AudioMusicVolume = profile.AudioMusicVolume,
            AudioNumSounds = profile.AudioNumSounds,
            AudioEnabled = profile.AudioEnabled,

            // GeneralsOnline Settings
            GoShowFps = profile.GoShowFps,
            GoShowPing = profile.GoShowPing,
            GoAutoLogin = profile.GoAutoLogin,
            GoRememberUsername = profile.GoRememberUsername,
            GoEnableNotifications = profile.GoEnableNotifications,
            GoChatFontSize = profile.GoChatFontSize,
            GoEnableSoundNotifications = profile.GoEnableSoundNotifications,
            GoShowPlayerRanks = profile.GoShowPlayerRanks,
            GoCameraMaxHeightOnlyWhenLobbyHost = profile.GoCameraMaxHeightOnlyWhenLobbyHost,
            GoCameraMinHeight = profile.GoCameraMinHeight,
            GoCameraMoveSpeedRatio = profile.GoCameraMoveSpeedRatio,
            GoChatDurationSecondsUntilFadeOut = profile.GoChatDurationSecondsUntilFadeOut,
            GoDebugVerboseLogging = profile.GoDebugVerboseLogging,
            GoRenderFpsLimit = profile.GoRenderFpsLimit,
            GoRenderLimitFramerate = profile.GoRenderLimitFramerate,
            GoRenderStatsOverlay = profile.GoRenderStatsOverlay,
        };
    }

    private static bool HasRelevantGeneralsOnlineProfiles(IEnumerable<Core.Models.GameProfile.GameProfile>? profiles) =>
        profiles?.Any(IsProfileRelevantToGeneralsOnline) == true;

    private static bool IsProfileRelevantToGeneralsOnline(Core.Models.GameProfile.GameProfile profile)
    {
        if (profile.GameClient != null &&
            (string.Equals(profile.GameClient.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase) ||
             profile.GameClient.Id.Contains($".{GeneralsOnlineConstants.PublisherType}.", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return profile.EnabledContentIds is { } enabled &&
               enabled.Any(id => id.Contains($".{GeneralsOnlineConstants.PublisherType}.", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds a mapping from old manifest IDs to new manifest IDs based on variant matching.
    /// Handles 30hz, 60hz, quickmatch-maps, and gamedata variants.
    /// </summary>
    private Dictionary<string, string> BuildManifestMapping(
        List<ContentManifest> oldManifests,
        List<ContentManifest> newManifests,
        IComparer<string>? versionComparer = null)
    {
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Group new manifests by variant for fast lookup
        var newByVariant = GroupManifestsByVariant(newManifests);

        foreach (var oldManifest in oldManifests)
        {
            var oldVariant = ExtractVariant(oldManifest);
            if (oldVariant == null)
            {
                logger.LogWarning("[GO Reconciler] Could not determine variant for old manifest {Id}", oldManifest.Id);
                continue;
            }

            // Find candidates in the same variant
            if (newByVariant.TryGetValue(oldVariant, out var candidates))
            {
                var match = FindMatchingCandidate(candidates, oldManifest, versionComparer);
                if (match != null)
                {
                    mapping[oldManifest.Id.Value] = match.Id.Value;
                    logger.LogDebug("[GO Reconciler] Mapped {OldId} -> {NewId} (variant: {Variant})", oldManifest.Id, match.Id, oldVariant);
                    continue;
                }
            }

            // Fallback: match by content type regardless of variant (for backwards compatibility)
            var fallbackMatch = newManifests
                .Where(n => n.ContentType == oldManifest.ContentType ||
                            (oldManifest.ContentType == ContentType.Mod && n.ContentType == ContentType.GameClient))
                .OrderByDescending(n => n.Version, versionComparer ?? Comparer<string>.Default)
                .FirstOrDefault();

            if (fallbackMatch != null)
            {
                mapping[oldManifest.Id.Value] = fallbackMatch.Id.Value;
                logger.LogDebug("[GO Reconciler] Fallback mapped {OldId} -> {NewId}", oldManifest.Id, fallbackMatch.Id);
            }
            else
            {
                logger.LogWarning("[GO Reconciler] No replacement found for old manifest {Id}", oldManifest.Id);
            }
        }

        return mapping;
    }

    /// <summary>
    /// Checks update availability and determines the update strategy.
    /// Returns (proceed: false, ...) if update is not needed or cancelled.
    /// </summary>
    private async Task<OperationResult<(bool Proceed, ContentUpdateCheckResult? UpdateResult, UpdateStrategy Strategy, bool AutoUpdate, bool ShouldDeleteOldVersions)>>
        CheckUpdateAvailabilityAndStrategyAsync(
            string triggeringProfileId,
            CancellationToken cancellationToken)
    {
        var updateResult = await updateService.CheckForUpdatesAsync(cancellationToken);
        if (!updateResult.Success)
        {
            logger.LogWarning("[GO Reconciler] Update check failed: {Error}", updateResult.FirstError);
            return OperationResult<(bool, ContentUpdateCheckResult?, UpdateStrategy, bool, bool)>.CreateFailure(
                $"Failed to check for GeneralsOnline updates: {updateResult.FirstError}");
        }

        if (!updateResult.IsUpdateAvailable)
        {
            logger.LogInformation("[GO Reconciler] No update available. Current version: {Version}", updateResult.CurrentVersion);
            return OperationResult<(bool, ContentUpdateCheckResult?, UpdateStrategy, bool, bool)>.CreateSuccess(
                (false, null, UpdateStrategy.ReplaceCurrent, false, true));
        }

        logger.LogInformation(
            "[GO Reconciler] Update available! Current: {CurrentVersion}, Latest: {LatestVersion}",
            updateResult.CurrentVersion,
            updateResult.LatestVersion);

        // Check if the triggering profile already uses the latest version
        if (await IsProfileAlreadyOnVersionAsync(triggeringProfileId, updateResult.LatestVersion ?? string.Empty, cancellationToken))
        {
            return OperationResult<(bool, ContentUpdateCheckResult?, UpdateStrategy, bool, bool)>.CreateSuccess(
                (false, null, UpdateStrategy.ReplaceCurrent, false, true));
        }

        var settings = userSettingsService.Get();
        if (settings.IsVersionSkipped(GeneralsOnlineConstants.PublisherType, updateResult.LatestVersion ?? string.Empty))
        {
            logger.LogInformation("[GO Reconciler] User opted to skip version {Version}. Skipping.", updateResult.LatestVersion);
            return OperationResult<(bool, ContentUpdateCheckResult?, UpdateStrategy, bool, bool)>.CreateSuccess(
                (false, null, UpdateStrategy.ReplaceCurrent, false, true));
        }

        var subscription = settings.GetSubscription(GeneralsOnlineConstants.PublisherType);
        var strategy = subscription?.PreferredUpdateStrategy ?? settings.PreferredUpdateStrategy ?? UpdateStrategy.ReplaceCurrent;
        var autoUpdate = subscription?.AutoUpdateEnabled == true;
        var shouldDeleteOldVersions = subscription?.DeleteOldVersions ?? true;

        if (!autoUpdate)
        {
            var promptResult = await PromptUserForUpdateStrategyAsync(
                updateResult.LatestVersion ?? "Unknown",
                strategy,
                shouldDeleteOldVersions);

            if (!promptResult.Proceed)
            {
                return OperationResult<(bool, ContentUpdateCheckResult?, UpdateStrategy, bool, bool)>.CreateSuccess(
                    (false, null, strategy, false, shouldDeleteOldVersions));
            }

            strategy = promptResult.Strategy;
            shouldDeleteOldVersions = promptResult.ShouldDeleteOldVersions;
        }

        return OperationResult<(bool, ContentUpdateCheckResult?, UpdateStrategy, bool, bool)>.CreateSuccess(
            (true, updateResult, strategy, autoUpdate, shouldDeleteOldVersions));
    }

    /// <summary>
    /// Checks if a profile is already using the specified version of the GeneralsOnline client.
    /// </summary>
    private async Task<bool> IsProfileAlreadyOnVersionAsync(
        string triggeringProfileId,
        string latestVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(triggeringProfileId))
        {
            return false;
        }

        try
        {
            var profileResult = await profileManager.GetProfileAsync(triggeringProfileId, cancellationToken);
            if (!profileResult.Success || profileResult.Data == null)
            {
                return false;
            }

            var clientVersion = profileResult.Data.GameClient?.Version;
            if (string.Equals(clientVersion, latestVersion, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "[GO Reconciler] Profile {ProfileId} already uses latest version {Version} (client version: {ClientVersion}). Skipping update.",
                    triggeringProfileId,
                    latestVersion,
                    clientVersion);
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[GO Reconciler] Could not retrieve triggering profile {ProfileId} to inspect client version", triggeringProfileId);
        }

        return false;
    }

    /// <summary>
    /// Prompts the user with update strategy dialog and saves preferences if requested.
    /// </summary>
    /// <param name="latestVersion">The latest version string.</param>
    /// <param name="fallbackStrategy">The default update strategy to use if not specified.</param>
    /// <param name="fallbackDeleteOldVersions">The default setting for deleting old versions.</param>
    /// <returns>A tuple indicating whether to proceed, the chosen strategy, and whether to delete old versions.</returns>
    private async Task<(bool Proceed, UpdateStrategy Strategy, bool ShouldDeleteOldVersions)> PromptUserForUpdateStrategyAsync(
        string latestVersion,
        UpdateStrategy fallbackStrategy,
        bool fallbackDeleteOldVersions)
    {
        var dialogResult = await dialogService.ShowUpdateOptionDialogAsync(
            "Generals Online Update Available",
            $"A new version of **Generals Online** is available ({latestVersion}).\n\nHow do you want to apply this update?",
            fallbackDeleteOldVersions);

        if (dialogResult == null)
        {
            return (false, fallbackStrategy, fallbackDeleteOldVersions);
        }

        if (dialogResult.Action == "Skip")
        {
            logger.LogInformation("[GO Reconciler] User skipped version {Version}.", latestVersion);
            if (dialogResult.IsDoNotAskAgain)
            {
                await userSettingsService.TryUpdateAndSaveAsync(s =>
                {
                    s.SkipVersion(GeneralsOnlineConstants.PublisherType, latestVersion);
                    return true;
                });
            }

            return (false, fallbackStrategy, fallbackDeleteOldVersions);
        }

        var strategy = dialogResult.Strategy;
        var shouldDeleteOldVersions = dialogResult.DeleteOldVersions;

        if (dialogResult.IsDoNotAskAgain)
        {
            logger.LogInformation("[GO Reconciler] Saving user preference for GeneralsOnline updates");
            await userSettingsService.TryUpdateAndSaveAsync(s =>
            {
                var sub = s.GetOrCreateSubscription(GeneralsOnlineConstants.PublisherType, isSubscribed: true);
                sub.AutoUpdateEnabled = true;
                sub.PreferredUpdateStrategy = strategy;
                sub.DeleteOldVersions = shouldDeleteOldVersions;
                return true;
            });
        }

        return (true, strategy, shouldDeleteOldVersions);
    }

    private async Task<OperationResult<(int ProfilesUpdated, bool AnyFailure, Dictionary<string, string>? ManifestMapping, string? TargetProfileId)>>
        ApplyUpdateStrategyAsync(
            UpdateStrategy strategy,
            List<ContentManifest> oldManifests,
            List<ContentManifest> newManifests,
            string newVersion,
            string? triggeringProfileId,
            CancellationToken cancellationToken)
    {
        if (strategy == UpdateStrategy.CreateNewProfile)
        {
            var createResult = await CreateNewProfilesForUpdateAsync(oldManifests, newManifests, newVersion, triggeringProfileId, cancellationToken);
            if (createResult.Success)
            {
                return OperationResult<(int, bool, Dictionary<string, string>?, string?)>.CreateSuccess((createResult.Data.CreatedCount, false, null, createResult.Data.TargetProfileId));
            }

            notificationService.ShowWarning("GeneralsOnline Update Partial", $"Failed to create some new profiles: {createResult.FirstError}", NotificationDurations.VeryLong);
            return OperationResult<(int, bool, Dictionary<string, string>?, string?)>.CreateSuccess((0, true, null, null));
        }

        var manifestMapping = BuildManifestMapping(oldManifests, newManifests, versionComparer.GetScheme(GeneralsOnlineConstants.PublisherType));
        var bulkUpdateResult = await reconciliationService.OrchestrateBulkUpdateAsync(manifestMapping, removeOld: false, cancellationToken);

        if (!bulkUpdateResult.Success)
        {
            notificationService.ShowWarning("GeneralsOnline Update Partial", $"Some profiles could not be updated: {bulkUpdateResult.FirstError}", NotificationDurations.VeryLong);
            return OperationResult<(int, bool, Dictionary<string, string>?, string?)>.CreateFailure($"Bulk update failed: {bulkUpdateResult.FirstError}");
        }

        int profilesUpdated = bulkUpdateResult.Data?.ProfilesUpdated ?? 0;
        string? targetProfileId = triggeringProfileId;

        // If no profiles were updated because none existed, create a fresh profile
        if (profilesUpdated == 0)
        {
            var allProfiles = await profileManager.GetAllProfilesAsync(cancellationToken);
            if (!HasRelevantGeneralsOnlineProfiles(allProfiles.Data))
            {
                logger.LogInformation("[GO Reconciler] No relevant profiles found during replace update. Creating fresh profile.");
                var freshResult = await CreateFreshGeneralsOnlineProfileAsync(newManifests, newVersion, cancellationToken);
                if (freshResult.Success)
                {
                    profilesUpdated = freshResult.Data.CreatedCount;
                    targetProfileId = freshResult.Data.TargetProfileId;
                }
                else
                {
                    return OperationResult<(int, bool, Dictionary<string, string>?, string?)>.CreateFailure(freshResult.FirstError ?? "Failed to create fresh GeneralsOnline profile");
                }
            }
        }

        bool anyFailure = (bulkUpdateResult.Data?.FailedProfilesCount ?? 0) > 0;
        if (anyFailure)
        {
            notificationService.ShowWarning("Generals Online Update Partial", $"{bulkUpdateResult.Data?.FailedProfilesCount} profiles could not be updated.", NotificationDurations.VeryLong);
        }

        return OperationResult<(int, bool, Dictionary<string, string>?, string?)>.CreateSuccess((profilesUpdated, anyFailure, manifestMapping, targetProfileId));
    }

    private async Task HandleOldManifestsAndCleanupAsync(
        bool shouldDeleteOldVersions,
        bool anyFailure,
        Dictionary<string, string>? manifestMapping,
        List<ContentManifest> oldManifests,
        CancellationToken cancellationToken)
    {
        if (shouldDeleteOldVersions && !anyFailure && manifestMapping != null)
        {
            logger.LogInformation("[GO Reconciler] Deleting old manifests that have mapped successors");
            var oldManifestIds = oldManifests
                .Where(m => manifestMapping.ContainsKey(m.Id.Value))
                .Select(m => m.Id)
                .ToList();

            if (oldManifestIds.Count > 0)
            {
                var removalResult = await reconciliationService.OrchestrateBulkRemovalAsync(oldManifestIds, cancellationToken);
                if (!removalResult.Success)
                {
                    logger.LogWarning("[GO Reconciler] Failed to remove old manifests: {Error}", removalResult.FirstError);
                }
            }

            await reconciliationService.ScheduleGarbageCollectionAsync(false, cancellationToken);
        }
        else if (shouldDeleteOldVersions && anyFailure)
        {
            logger.LogWarning("[GO Reconciler] Skipping old manifest deletion and scheduled GC due to previous failures to preserve content integrity.");
        }
    }

    /// <summary>
    /// Finds all GeneralsOnline manifests currently in the manifest pool.
    /// </summary>
    private async Task<List<ContentManifest>> FindGeneralsOnlineManifestsAsync(
        CancellationToken cancellationToken)
    {
        var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!manifestsResult.Success || manifestsResult.Data == null)
        {
            return [];
        }

        return [.. manifestsResult.Data
            .Where(m =>
                !m.Id.Value.Contains(".local.", StringComparison.OrdinalIgnoreCase) && // Exclude local content
                (string.Equals(m.Publisher?.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase) ||
                  m.Id.Value.Contains($".{GeneralsOnlineConstants.PublisherType}.", StringComparison.OrdinalIgnoreCase) ||
                  (m.Name is { } name && name.Contains(GeneralsOnlineConstants.ClientName, StringComparison.OrdinalIgnoreCase))))];
    }

    private IProgress<ContentAcquisitionProgress>? CreateAcquisitionProgress(
        Guid? progressNotificationId,
        string itemName,
        int currentItemIndex,
        int totalItems)
    {
        if (!progressNotificationId.HasValue)
        {
            return null;
        }

        var notificationId = progressNotificationId.Value;
        var lastNotificationTimestamp = Stopwatch.GetTimestamp();

        return new Progress<ContentAcquisitionProgress>(p =>
        {
            var elapsedMs = Stopwatch.GetElapsedTime(lastNotificationTimestamp).TotalMilliseconds;
            if (elapsedMs < ManifestConstants.NotificationUpdateThrottleMs && p.ProgressPercentage < 100)
            {
                return;
            }

            lastNotificationTimestamp = Stopwatch.GetTimestamp();
            var status = p.FormatProgressStatus();
            var message = totalItems > 1
                ? $"[{currentItemIndex}/{totalItems}] {itemName}: {status}"
                : $"{itemName}: {status}";

            notificationService.Update(
                notificationId,
                message,
                "GeneralsOnline Update");
        });
    }

    private async Task<OperationResult<bool>> AcquireItemsAsync(
        IReadOnlyList<ContentSearchResult> items,
        Guid? progressNotificationId,
        CancellationToken cancellationToken)
    {
        int totalItems = items.Count;
        int currentItemIndex = 0;

        foreach (var result in items)
        {
            currentItemIndex++;
            var progress = CreateAcquisitionProgress(
                progressNotificationId,
                result.Name,
                currentItemIndex,
                totalItems);

            var acquireOp = await contentOrchestrator.AcquireContentAsync(result, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!acquireOp.Success)
            {
                logger.LogError(
                    "[GO:Reconciler] Failed to acquire content {ContentId}: {Error}",
                    result.Id,
                    acquireOp.FirstError);

                return OperationResult<bool>.CreateFailure(
                    $"Failed to acquire content {result.Id}: {acquireOp.FirstError}");
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <summary>
    /// Acquires the latest GeneralsOnline version by searching and downloading.
    /// </summary>
    private async Task<OperationResult<List<ContentManifest>>> AcquireLatestVersionAsync(
        List<ContentManifest> oldManifests,
        Guid? progressNotificationId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Search for Game Client
            var clientQuery = new ContentSearchQuery
            {
                ProviderName = GeneralsOnlineConstants.PublisherType,
                ContentType = ContentType.GameClient,
                TargetGame = GameType.ZeroHour,
            };

            var clientResult = await contentOrchestrator.SearchAsync(clientQuery, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Search for Map Packs (required dependency)
            var mapPackQuery = new ContentSearchQuery
            {
                ProviderName = GeneralsOnlineConstants.PublisherType,
                ContentType = ContentType.MapPack,
                TargetGame = GameType.ZeroHour,
            };

            var mapPackResult = await contentOrchestrator.SearchAsync(mapPackQuery, cancellationToken);

            var allResults = new List<ContentSearchResult>();

            if (clientResult.Success && clientResult.Data != null)
            {
                allResults.AddRange(clientResult.Data);
            }

            if (mapPackResult.Success && mapPackResult.Data != null)
            {
                allResults.AddRange(mapPackResult.Data);
            }

            // Layers beneath the orchestrator still report cancellation as a failed result, so a
            // failure raised while shutting down must not be surfaced as a real acquisition error.
            cancellationToken.ThrowIfCancellationRequested();

            if (allResults.Count == 0)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    "No GeneralsOnline content found from provider");
            }

            var items = allResults;
            var acquireResult = await AcquireItemsAsync(items, progressNotificationId, cancellationToken);
            if (!acquireResult.Success)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(acquireResult.FirstError ?? "Failed to acquire content");
            }

            var allGoManifests = await FindGeneralsOnlineManifestsAsync(cancellationToken);
            var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newManifests = allGoManifests.Where(m => !oldIds.Contains(m.Id.Value)).ToList();

            if (newManifests.Count == 0)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    "Acquisition completed but no new GeneralsOnline manifests were found in pool");
            }

            return OperationResult<List<ContentManifest>>.CreateSuccess(newManifests);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GO Reconciler] Failed to acquire latest version");
            return OperationResult<List<ContentManifest>>.CreateFailure(
                $"Failed to acquire latest version: {ex.Message}");
        }
    }

    /// <summary>
    /// Enforces that profiles using the new GeneralsOnline client also have the new MapPack.
    /// </summary>
    private async Task<OperationResult> EnforceMapPackDependencyAsync(
        List<ContentManifest> newManifests,
        CancellationToken cancellationToken)
    {
        // 1. Identify the new MapPack ID and GameClient IDs
        var newMapPack = newManifests.FirstOrDefault(m => m.ContentType == ContentType.MapPack);
        var newGameClient = newManifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient);
        var newVersion = newGameClient?.Version ?? string.Empty;
        var newGameClientIds = newManifests
            .Where(m => m.ContentType == ContentType.GameClient)
            .Select(m => m.Id.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (newMapPack == null || newGameClientIds.Count == 0)
        {
            logger.LogInformation("[GO Reconciler] No MapPack (found: {HasMapPack}) or GameClient ({ClientCount}) found for dependency enforcement.", newMapPack != null, newGameClientIds.Count);
            return OperationResult.CreateSuccess();
        }

        var newMapPackId = newMapPack.Id.Value;

        // 2. Get all profiles
        var allProfilesResult = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!allProfilesResult.Success || allProfilesResult.Data == null)
        {
            logger.LogWarning("[GO Reconciler] Failed to retrieve profiles for dependency enforcement.");
            return OperationResult.CreateFailure("Failed to retrieve profiles for dependency enforcement");
        }

        // 3. Iterate profiles and patch if needed
        var errors = new List<string>();
        var existingProfileNames = allProfilesResult.Data.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in allProfilesResult.Data)
        {
            // Only update profiles that use one of the new GameClients (preserving old profiles when CreateNewProfile was selected)
            if (profile.GameClient == null || !newGameClientIds.Contains(profile.GameClient.Id))
            {
                continue;
            }

            // Check if profile already has the new MapPack
            bool hasMapPack = profile.EnabledContentIds is { } contentIds &&
                              contentIds.Contains(newMapPackId, StringComparer.OrdinalIgnoreCase);

            var newEnabledContent = profile.EnabledContentIds != null
                ? [.. profile.EnabledContentIds]
                : new List<string>();

            bool needsUpdate = false;
            if (!hasMapPack)
            {
                logger.LogInformation("[GO Reconciler] Adding required MapPack {MapPackId} to profile {ProfileName}", newMapPackId, profile.Name);
                newEnabledContent.Add(newMapPackId);
                needsUpdate = true;
            }

            var updateRequest = new Core.Models.GameProfile.UpdateProfileRequest();
            if (needsUpdate)
            {
                updateRequest.EnabledContentIds = newEnabledContent;
            }

            // If profile name had an old version, update it to the new version
            var updatedName = FormatUpdatedProfileName(profile.Name, newVersion, existingProfileNames, allowSameAsOriginal: true);
            if (!string.Equals(updatedName, profile.Name, StringComparison.Ordinal))
            {
                updateRequest.Name = updatedName;
                existingProfileNames.Add(updatedName);
                needsUpdate = true;
            }

            // Ensure branding paths are populated if missing or defaulted
            if (string.IsNullOrEmpty(profile.IconPath) || profile.IconPath.Contains(UriConstants.GenHubIconMarker) || profile.IconPath.Contains(UriConstants.ZeroHourIconMarker))
            {
                updateRequest.IconPath = GeneralsOnlineConstants.LogoSource;
                needsUpdate = true;
            }

            if (string.IsNullOrEmpty(profile.CoverPath) || profile.CoverPath.Contains(UriConstants.ZeroHourCoverMarker))
            {
                updateRequest.CoverPath = GeneralsOnlineConstants.CoverSource;
                needsUpdate = true;
            }

            if (string.IsNullOrEmpty(profile.ThemeColor))
            {
                updateRequest.ThemeColor = GeneralsOnlineConstants.ThemeColor;
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                var updateResult = await profileManager.UpdateProfileAsync(profile.Id, updateRequest, cancellationToken);
                if (!updateResult.Success)
                {
                    var errorMsg = $"Failed to update profile '{profile.Name}': {updateResult.FirstError}";
                    errors.Add(errorMsg);
                    logger.LogError("[GO Reconciler] {Error}", errorMsg);
                }
            }
        }

        if (errors.Count > 0)
        {
            return OperationResult.CreateFailure($"Failed to update {errors.Count} profiles: {string.Join("; ", errors.Take(3))}{(errors.Count > 3 ? "..." : string.Empty)}");
        }

        return OperationResult.CreateSuccess();
    }

    /// <summary>
    /// Creates a fresh GeneralsOnline profile when no existing relevant profiles are present.
    /// </summary>
    private async Task<OperationResult<(int CreatedCount, string? TargetProfileId)>> CreateFreshGeneralsOnlineProfileAsync(
        List<ContentManifest> newManifests,
        string newVersion,
        CancellationToken cancellationToken)
    {
        var newClientManifest = newManifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient);
        GameInstallation? installation = null;

        if (installationService != null)
        {
            var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
            if (installationsResult.Success && installationsResult.Data != null)
            {
                installation = installationsResult.Data.FirstOrDefault(i => i.HasZeroHour);
            }
        }

        var installationPath = installation?.ZeroHourPath ?? string.Empty;
        var executableFile = newClientManifest?.Files?.FirstOrDefault(f =>
            f.RelativePath?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);
        var relativeExe = executableFile?.RelativePath ?? GeneralsOnlineConstants.DefaultExecutableFileName;
        var exePath = !string.IsNullOrEmpty(installationPath)
            ? Path.Combine(installationPath, relativeExe)
            : relativeExe;

        var gameClient = new Core.Models.GameClients.GameClient
        {
            Id = newClientManifest?.Id.Value ?? string.Empty,
            Name = newClientManifest?.Name ?? GameClientConstants.GeneralsOnline60HzDisplayName,
            Version = newClientManifest?.Version ?? newVersion,
            GameType = GameType.ZeroHour,
            SourceType = ContentType.GameClient,
            PublisherType = GeneralsOnlineConstants.PublisherType,
            InstallationId = installation?.Id ?? string.Empty,
            ExecutablePath = exePath,
            WorkingDirectory = installationPath,
        };

        var enabledContentIds = new List<string>();
        var allPoolManifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
        var installManifest = allPoolManifestsResult.Data?.FirstOrDefault(m =>
            m.ContentType == ContentType.GameInstallation && m.TargetGame == GameType.ZeroHour);
        if (installManifest != null)
        {
            enabledContentIds.Add(installManifest.Id.Value);
        }

        enabledContentIds.AddRange(
            newManifests
                .Select(m => m.Id.Value)
                .Where(id => !enabledContentIds.Contains(id, StringComparer.OrdinalIgnoreCase)));

        var baseName = !string.IsNullOrWhiteSpace(newClientManifest?.Name) ? newClientManifest.Name : GeneralsOnlineConstants.ClientName;
        var profileName = $"{baseName} v{newVersion}";

        var allProfiles = await profileManager.GetAllProfilesAsync(cancellationToken);
        var existingProfileNames = allProfiles.Data?.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var finalProfileName = profileName;
        int suffix = 2;
        while (existingProfileNames.Contains(finalProfileName))
        {
            finalProfileName = $"{profileName} ({suffix++})";
        }

        var iconPath = newClientManifest?.Metadata?.IconUrl ?? GeneralsOnlineConstants.LogoSource;
        var coverPath = newClientManifest?.Metadata?.CoverUrl ?? GeneralsOnlineConstants.CoverSource;
        var themeColor = newClientManifest?.Metadata?.ThemeColor ?? GeneralsOnlineConstants.ThemeColor;

        var createRequest = new Core.Models.GameProfile.CreateProfileRequest
        {
            Name = finalProfileName,
            Description = newClientManifest?.Metadata?.Description ?? GeneralsOnlineConstants.ShortDescription,
            GameInstallationId = installation?.Id,
            GameClientId = gameClient.Id,
            GameClient = gameClient,
            WorkspaceStrategy = WorkspaceStrategy.SymlinkOnly,
            EnabledContentIds = enabledContentIds,
            ThemeColor = themeColor,
            IconPath = iconPath,
            CoverPath = coverPath,
            UseSteamLaunch = installation?.InstallationType == GameInstallationType.Steam,
        };

        var createResult = await profileManager.CreateProfileAsync(createRequest, cancellationToken);
        if (createResult != null && createResult.Success)
        {
            logger.LogInformation("[GO Reconciler] Successfully created fresh profile '{Name}' for update", createRequest.Name);
            return OperationResult<(int CreatedCount, string? TargetProfileId)>.CreateSuccess((1, createResult.Data?.Id));
        }

        logger.LogError("[GO Reconciler] Failed to create fresh profile for update: {Error}", createResult?.FirstError);
        return OperationResult<(int CreatedCount, string? TargetProfileId)>.CreateFailure(createResult?.FirstError ?? "Failed to create fresh GeneralsOnline profile");
    }

    /// <summary>
    /// Creates new profiles for the update instead of replacing existing ones.
    /// </summary>
    private async Task<OperationResult<(int CreatedCount, string? TargetProfileId)>> CreateNewProfilesForUpdateAsync(
        List<ContentManifest> oldManifests,
        List<ContentManifest> newManifests,
        string newVersion,
        string? triggeringProfileId,
        CancellationToken cancellationToken)
    {
        var oldIds = oldManifests.Select(m => m.Id.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestMapping = BuildManifestMapping(oldManifests, newManifests, versionComparer.GetScheme(GeneralsOnlineConstants.PublisherType));
        int createdCount = 0;
        string? targetProfileId = null;

        var allProfiles = await profileManager.GetAllProfilesAsync(cancellationToken);
        if (!allProfiles.Success || allProfiles.Data == null)
        {
            return OperationResult<(int CreatedCount, string? TargetProfileId)>.CreateFailure("Failed to retrieve profiles for creating new profiles");
        }

        var existingProfileNames = allProfiles.Data.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var relevantProfiles = allProfiles.Data.Where(p =>
            (p.GameClient != null && oldIds.Contains(p.GameClient.Id)) ||
            (p.EnabledContentIds is { } enabled && enabled.Any(oldIds.Contains)) ||
            (p.GameClient != null && string.Equals(p.GameClient.PublisherType, GeneralsOnlineConstants.PublisherType, StringComparison.OrdinalIgnoreCase)) ||
            (p.GameClient != null && p.GameClient.Id.Contains($".{GeneralsOnlineConstants.PublisherType}.", StringComparison.OrdinalIgnoreCase))).ToList();

        if (relevantProfiles.Count == 0)
        {
            logger.LogInformation("[GO Reconciler] No existing relevant profiles found. Creating a fresh profile.");
            return await CreateFreshGeneralsOnlineProfileAsync(newManifests, newVersion, cancellationToken);
        }

        var newClientManifest = newManifests.FirstOrDefault(m => m.ContentType == ContentType.GameClient);

        foreach (var profile in relevantProfiles)
        {
            var targetProfileName = FormatUpdatedProfileName(profile.Name, newVersion, existingProfileNames, allowSameAsOriginal: false);

            try
            {
                var updatedGameClient = BuildUpdatedGameClient(profile, newClientManifest, newVersion);
                var newEnabledContent = ResolveUpdatedEnabledContent(profile, manifestMapping, newManifests);
                var cloneRequest = BuildCloneProfileRequest(profile, targetProfileName, updatedGameClient, newEnabledContent, newClientManifest);

                var createResult = await profileManager.CreateProfileAsync(cloneRequest, cancellationToken);
                if (createResult != null && createResult.Success)
                {
                    createdCount++;
                    existingProfileNames.Add(targetProfileName);
                    if (!string.IsNullOrEmpty(triggeringProfileId) &&
                        string.Equals(profile.Id, triggeringProfileId, StringComparison.OrdinalIgnoreCase) &&
                        createResult.Data != null)
                    {
                        targetProfileId = createResult.Data.Id;
                    }

                    logger.LogInformation("[GO Reconciler] Created new profile '{Name}' for update", cloneRequest.Name);
                }
                else
                {
                    logger.LogError("[GO Reconciler] Failed to create new profile for update: {Error}", createResult?.FirstError);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[GO Reconciler] Error creating profile for update");
            }
        }

        if (createdCount == 0)
        {
            logger.LogInformation("[GO Reconciler] No profiles were cloned. Creating fresh profile as fallback.");
            return await CreateFreshGeneralsOnlineProfileAsync(newManifests, newVersion, cancellationToken);
        }

        return OperationResult<(int CreatedCount, string? TargetProfileId)>.CreateSuccess((createdCount, targetProfileId));
    }
}
