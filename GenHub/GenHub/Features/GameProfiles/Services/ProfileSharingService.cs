using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service that implements profile package export, URL generation, pre-import inspection, and acquisition.
/// </summary>
public class ProfileSharingService(
    IGameProfileRepository profileRepository,
    IContentManifestPool manifestPool,
    IGameInstallationService installationService,
    IContentOrchestrator contentOrchestrator,
    PublisherManifestFactoryResolver publisherManifestFactoryResolver,
    HttpClient httpClient,
    ILogger<ProfileSharingService> logger) : IProfileSharingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <inheritdoc/>
    public async Task<OperationResult<string>> ExportProfileToUriAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return OperationResult<string>.CreateFailure("Profile identifier cannot be empty.");
            }

            var packageResult = await BuildPackageFromProfileIdAsync(profileId, cancellationToken);
            if (!packageResult.Success || packageResult.Data == null)
            {
                return OperationResult<string>.CreateFailure(packageResult.Errors);
            }

            var json = JsonSerializer.Serialize(packageResult.Data, JsonOptions);
            var encoded = ProfileSharingCompressionHelper.CompressAndEncode(json);

            if (encoded.Length > ProfileSharingConstants.MaxInlinePayloadLength)
            {
                logger.LogWarning("Profile sharing payload length ({Length} chars) exceeds inline maximum ({Max} chars).", encoded.Length, ProfileSharingConstants.MaxInlinePayloadLength);
                return OperationResult<string>.CreateFailure($"Shared profile payload exceeds the inline URI size limit ({ProfileSharingConstants.MaxInlinePayloadLength / 1024} KB). Please export as a {ProfileSharingConstants.ProfileFileExtension} file instead.");
            }

            string uri = $"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}{encoded}";
            logger.LogInformation("Exported profile {ProfileId} to URI (Payload size: {Length} chars).", profileId, encoded.Length);
            return OperationResult<string>.CreateSuccess(uri);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error exporting profile {ProfileId} to URI.", profileId);
            return OperationResult<string>.CreateFailure($"Failed to export profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> ExportProfileToFileAsync(string profileId, string destinationPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return OperationResult<string>.CreateFailure("Profile identifier cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return OperationResult<string>.CreateFailure("Destination file path cannot be empty.");
            }

            var packageResult = await BuildPackageFromProfileIdAsync(profileId, cancellationToken);
            if (!packageResult.Success || packageResult.Data == null)
            {
                return OperationResult<string>.CreateFailure(packageResult.Errors);
            }

            var json = JsonSerializer.Serialize(packageResult.Data, JsonOptions);
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(destinationPath, json, cancellationToken);
            logger.LogInformation("Exported profile {ProfileId} to file: {DestinationPath}", profileId, destinationPath);
            return OperationResult<string>.CreateSuccess(destinationPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error exporting profile {ProfileId} to file {DestinationPath}.", profileId, destinationPath);
            return OperationResult<string>.CreateFailure($"Failed to export profile to file: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> ExportProfileToJsonAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return OperationResult<string>.CreateFailure("Profile identifier cannot be empty.");
            }

            var packageResult = await BuildPackageFromProfileIdAsync(profileId, cancellationToken);
            if (!packageResult.Success || packageResult.Data == null)
            {
                return OperationResult<string>.CreateFailure(packageResult.Errors);
            }

            var json = JsonSerializer.Serialize(packageResult.Data, JsonOptions);
            return OperationResult<string>.CreateSuccess(json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error exporting profile {ProfileId} to JSON.", profileId);
            return OperationResult<string>.CreateFailure($"Failed to export profile to JSON: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<SharedProfileInspectionResult>> InspectSharedProfileAsync(string shareUriOrJsonOrPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(shareUriOrJsonOrPath))
            {
                return OperationResult<SharedProfileInspectionResult>.CreateFailure("Share payload or path cannot be empty.");
            }

            var rawJsonResult = await ResolvePayloadJsonAsync(shareUriOrJsonOrPath, cancellationToken);
            if (!rawJsonResult.Success || string.IsNullOrWhiteSpace(rawJsonResult.Data))
            {
                return OperationResult<SharedProfileInspectionResult>.CreateFailure(rawJsonResult.Errors);
            }

            SharedGameProfilePackage? package;
            try
            {
                package = JsonSerializer.Deserialize<SharedGameProfilePackage>(rawJsonResult.Data, JsonOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize shared profile package JSON.");
                return OperationResult<SharedProfileInspectionResult>.CreateFailure($"Invalid shared profile package format: {ex.Message}");
            }

            if (package?.Profile == null)
            {
                return OperationResult<SharedProfileInspectionResult>.CreateFailure("Package does not contain valid profile metadata.");
            }

            // Sanitize launch arguments
            _ = ProfileSharingCompressionHelper.SanitizeCommandLineArguments(package.Profile.CommandLineArguments, out var securityWarnings);

            // Itemize and diff manifests against local CAS/ManifestPool
            var inspectedManifests = new List<SharedManifestDependency>();
            long totalMissingDownloadBytes = 0;
            int cachedCount = 0;
            int missingCount = 0;

            foreach (var reqManifest in package.RequiredManifests)
            {
                bool isCached = false;
                var acquiredResult = await manifestPool.IsManifestAcquiredAsync(reqManifest.ManifestId, cancellationToken);
                if (acquiredResult.Success && acquiredResult.Data)
                {
                    isCached = true;
                    cachedCount++;
                }
                else
                {
                    missingCount++;
                    long size = reqManifest.DownloadSize > 0
                        ? reqManifest.DownloadSize
                        : (reqManifest.Files?.Sum(f => f.Size) ?? 0);
                    totalMissingDownloadBytes += size;
                }

                inspectedManifests.Add(new SharedManifestDependency
                {
                    ManifestId = reqManifest.ManifestId,
                    DisplayName = reqManifest.DisplayName,
                    Version = reqManifest.Version,
                    ContentType = reqManifest.ContentType,
                    Publisher = reqManifest.Publisher,
                    PublisherType = reqManifest.PublisherType,
                    ManifestUrl = reqManifest.ManifestUrl,
                    DownloadSize = reqManifest.DownloadSize,
                    IsCachedLocally = isCached,
                    Hash = reqManifest.Hash,
                    Files = reqManifest.Files ?? [],
                });
            }

            // Check game installations on recipient's machine
            var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
            var compatibleInstallations = new List<GameInstallation>();
            string? matchedInstallationId = null;

            if (installationsResult.Success && installationsResult.Data != null)
            {
                compatibleInstallations = installationsResult.Data
                    .Where(i => package.Profile.GameType == GameType.Generals ? i.HasGenerals : i.HasZeroHour)
                    .ToList();

                matchedInstallationId = compatibleInstallations.FirstOrDefault()?.Id;
            }

            bool hasValidGameInstallation = compatibleInstallations.Count > 0;

            // Check name conflicts
            var allProfilesResult = await profileRepository.LoadAllProfilesAsync(cancellationToken);
            bool hasNameConflict = false;
            string suggestedName = package.Profile.Name;

            if (allProfilesResult.Success && allProfilesResult.Data != null)
            {
                var existingNames = allProfilesResult.Data.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (existingNames.Contains(package.Profile.Name))
                {
                    hasNameConflict = true;
                    int counter = 1;
                    suggestedName = $"{package.Profile.Name}{ProfileSharingConstants.NameConflictSuffix}";
                    while (existingNames.Contains(suggestedName))
                    {
                        suggestedName = $"{package.Profile.Name}{ProfileSharingConstants.NameConflictSuffix} ({++counter})";
                    }
                }
            }

            var inspectionResult = new SharedProfileInspectionResult
            {
                ProfileMetadata = package.Profile,
                Manifests = inspectedManifests,
                HasValidGameInstallation = hasValidGameInstallation,
                MatchedGameInstallationId = matchedInstallationId,
                CompatibleInstallations = compatibleInstallations,
                TotalDownloadBytesRequired = totalMissingDownloadBytes,
                CachedManifestCount = cachedCount,
                MissingManifestCount = missingCount,
                HasNameConflict = hasNameConflict,
                SuggestedProfileName = suggestedName,
                SecurityWarnings = securityWarnings,
                Package = package,
            };

            return OperationResult<SharedProfileInspectionResult>.CreateSuccess(inspectionResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error inspecting shared profile.");
            return OperationResult<SharedProfileInspectionResult>.CreateFailure($"Failed to inspect profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<GameProfile>> ImportSharedProfileAsync(
        SharedProfileImportRequest request,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (request == null)
            {
                return OperationResult<GameProfile>.CreateFailure("Import request cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(request.ProfileName))
            {
                return OperationResult<GameProfile>.CreateFailure("Profile name cannot be empty.");
            }

            var package = request.Package;
            if (package?.Profile == null)
            {
                return OperationResult<GameProfile>.CreateFailure("Invalid package in import request.");
            }

            // Acquire missing dependencies
            var requiredManifestIds = new List<string>();

            for (int i = 0; i < package.RequiredManifests.Count; i++)
            {
                var dep = package.RequiredManifests[i];
                requiredManifestIds.Add(dep.ManifestId);

                var isCachedResult = await manifestPool.IsManifestAcquiredAsync(dep.ManifestId, cancellationToken);
                if (isCachedResult.Success && isCachedResult.Data)
                {
                    logger.LogDebug("Manifest {ManifestId} is already cached locally, skipping download.", dep.ManifestId);
                    continue;
                }

                logger.LogInformation("Acquiring missing manifest {ManifestId} for imported profile.", dep.ManifestId);
                progress?.Report(new ContentAcquisitionProgress
                {
                    Phase = ContentAcquisitionPhase.Downloading,
                    ProgressPercentage = (int)((double)i / package.RequiredManifests.Count * 100),
                    CurrentOperation = $"Acquiring {dep.DisplayName}...",
                });

                var acquireResult = await AcquireMissingManifestAsync(dep, progress, cancellationToken);
                if (!acquireResult.Success)
                {
                    logger.LogError("Failed to acquire manifest {ManifestId}: {Error}", dep.ManifestId, acquireResult.FirstError);
                    return OperationResult<GameProfile>.CreateFailure($"Failed to acquire '{dep.DisplayName}': {acquireResult.FirstError}");
                }
            }

            // Resolve target game installation
            GameInstallation? targetInstallation = null;
            if (!string.IsNullOrEmpty(request.GameInstallationId))
            {
                var instResult = await installationService.GetInstallationAsync(request.GameInstallationId, cancellationToken);
                if (instResult.Success)
                {
                    targetInstallation = instResult.Data;
                }
            }

            if (targetInstallation == null)
            {
                var allInstallations = await installationService.GetAllInstallationsAsync(cancellationToken);
                targetInstallation = allInstallations.Data?.FirstOrDefault(i => package.Profile.GameType == GameType.Generals ? i.HasGenerals : i.HasZeroHour);
            }

            if (targetInstallation == null)
            {
                return OperationResult<GameProfile>.CreateFailure($"No compatible game installation found for {package.Profile.GameType}.");
            }

            // Determine GameClient
            GameClient? gameClient = null;
            if (!string.IsNullOrEmpty(package.Profile.GameClientManifestId))
            {
                var clientManifestResult = await manifestPool.GetManifestAsync(package.Profile.GameClientManifestId, cancellationToken);
                if (clientManifestResult.Success && clientManifestResult.Data != null)
                {
                    gameClient = new GameClient
                    {
                        Id = clientManifestResult.Data.Id.ToString(),
                        Name = clientManifestResult.Data.Name,
                        Version = clientManifestResult.Data.Version,
                        GameType = clientManifestResult.Data.TargetGame,
                        InstallationId = targetInstallation.Id,
                    };
                }
            }

            gameClient ??= targetInstallation.AvailableGameClients.FirstOrDefault(c => c.GameType == package.Profile.GameType)
                ?? targetInstallation.AvailableGameClients.FirstOrDefault();

            // Construct new GameProfile
            var sanitizedArguments = ProfileSharingCompressionHelper.SanitizeCommandLineArguments(package.Profile.CommandLineArguments, out _);

            var newProfile = new GameProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = request.ProfileName.Trim(),
                Description = package.Profile.Description ?? string.Empty,
                GameInstallationId = targetInstallation.Id,
                GameClient = gameClient,
                WorkspaceStrategy = request.WorkspaceStrategy ?? package.Profile.WorkspaceStrategy,
                EnabledContentIds = requiredManifestIds,
                ThemeColor = package.Profile.ThemeColor,
                IconPath = package.Profile.IconPath,
                CoverPath = package.Profile.CoverPath,
                CommandLineArguments = sanitizedArguments,
                UseSteamLaunch = package.Profile.UseSteamLaunch,
                CreatedAt = DateTime.UtcNow,
            };

            // Apply game settings overrides if requested
            if (request.IncludeGameSettings && package.Profile.GameSettingsOverrides != null)
            {
                ApplySettingsOverridesToProfile(newProfile, package.Profile.GameSettingsOverrides);
            }

            var saveResult = await profileRepository.SaveProfileAsync(newProfile, cancellationToken);
            if (!saveResult.Success)
            {
                return OperationResult<GameProfile>.CreateFailure(saveResult.Errors);
            }

            logger.LogInformation("Successfully imported and created shared game profile '{ProfileName}' (ID: {ProfileId}).", newProfile.Name, newProfile.Id);
            WeakReferenceMessenger.Default.Send(new ProfileCreatedMessage(newProfile));

            return OperationResult<GameProfile>.CreateSuccess(newProfile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error importing shared profile.");
            return OperationResult<GameProfile>.CreateFailure($"Failed to import shared profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public string GenerateDiscordMarkdown(GameProfile profile, string shareUri)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(shareUri);

        string gameVersion = !string.IsNullOrEmpty(profile.GameClient?.Version)
            ? profile.GameClient.Version
            : profile.Version;

        string titleInfo = $"{profile.GameClient?.GameType ?? GameType.ZeroHour} {gameVersion}".Trim();
        string description = string.IsNullOrWhiteSpace(profile.Description)
            ? "Shared game configuration for GenHub."
            : profile.Description.Trim();

        return string.Format(
            ProfileSharingConstants.DiscordMarkdownTemplate,
            profile.Name,
            titleInfo,
            description,
            shareUri);
    }

    private async Task<OperationResult<SharedGameProfilePackage>> BuildPackageFromProfileIdAsync(string profileId, CancellationToken cancellationToken)
    {
        var loadResult = await profileRepository.LoadProfileAsync(profileId, cancellationToken);
        if (!loadResult.Success || loadResult.Data == null)
        {
            return OperationResult<SharedGameProfilePackage>.CreateFailure($"Profile not found: {profileId}");
        }

        var profile = loadResult.Data;
        var manifests = new List<SharedManifestDependency>();

        // Gather all enabled content manifests
        foreach (var contentId in profile.EnabledContentIds ?? [])
        {
            var manifestResult = await manifestPool.GetManifestAsync(contentId, cancellationToken);
            if (manifestResult != null && manifestResult.Success && manifestResult.Data != null)
            {
                var manifest = manifestResult.Data;
                manifests.Add(new SharedManifestDependency
                {
                    ManifestId = manifest.Id.ToString(),
                    DisplayName = manifest.Name,
                    Version = manifest.Version,
                    ContentType = manifest.ContentType,
                    Publisher = manifest.Publisher?.Name,
                    PublisherType = manifest.Publisher?.PublisherType,
                    ManifestUrl = manifest.Publisher?.ContentIndexUrl,
                    DownloadSize = manifest.Files?.Sum(f => f.Size) ?? 0,
                    IsCachedLocally = true,
                    Files = manifest.Files ?? [],
                });
            }
            else
            {
                // Add reference item even if manifest pool did not have full object
                manifests.Add(new SharedManifestDependency
                {
                    ManifestId = contentId,
                    DisplayName = contentId,
                    Version = "1.0",
                    ContentType = ContentType.Mod,
                    IsCachedLocally = true,
                });
            }
        }

        var settingsOverrides = ExtractSettingsOverridesFromProfile(profile);

        var package = new SharedGameProfilePackage
        {
            SchemaVersion = ProfileSharingConstants.DefaultSchemaVersion,
            GeneratorVersion = AppConstants.AppVersion,
            ExportedAt = DateTime.UtcNow,
            Profile = new SharedProfileMetadata
            {
                Name = profile.Name,
                Description = profile.Description ?? string.Empty,
                ThemeColor = profile.ThemeColor,
                IconPath = profile.IconPath,
                CoverPath = profile.CoverPath,
                GameType = profile.GameClient?.GameType ?? GameType.ZeroHour,
                GameVersion = profile.Version,
                GameClientManifestId = profile.GameClient?.Id,
                UseSteamLaunch = profile.UseSteamLaunch,
                WorkspaceStrategy = profile.WorkspaceStrategy,
                CommandLineArguments = profile.CommandLineArguments ?? string.Empty,
                GameSettingsOverrides = settingsOverrides,
            },
            RequiredManifests = manifests,
        };

        return OperationResult<SharedGameProfilePackage>.CreateSuccess(package);
    }

    private async Task<OperationResult<string>> ResolvePayloadJsonAsync(string shareUriOrJsonOrPath, CancellationToken cancellationToken)
    {
        string input = shareUriOrJsonOrPath.Trim();

        // Check if URI scheme format
        if (input.StartsWith(CommandLineConstants.UriScheme, StringComparison.OrdinalIgnoreCase))
        {
            if (input.StartsWith(CommandLineConstants.ProfileImportUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int dataParamIdx = input.IndexOf(CommandLineConstants.DataQueryParam, StringComparison.OrdinalIgnoreCase);
                if (dataParamIdx != -1)
                {
                    string encoded = input[(dataParamIdx + CommandLineConstants.DataQueryParam.Length)..];
                    int nextParamIdx = encoded.IndexOf('&');
                    if (nextParamIdx != -1)
                    {
                        encoded = encoded[..nextParamIdx];
                    }

                    string decompressed = await ProfileSharingCompressionHelper.DecodeAndDecompressAsync(encoded, cancellationToken);
                    return OperationResult<string>.CreateSuccess(decompressed);
                }

                int urlParamIdx = input.IndexOf(CommandLineConstants.UrlQueryParam, StringComparison.OrdinalIgnoreCase);
                if (urlParamIdx != -1)
                {
                    string url = Uri.UnescapeDataString(input[(urlParamIdx + CommandLineConstants.UrlQueryParam.Length)..]);
                    int nextParamIdx = url.IndexOf('&');
                    if (nextParamIdx != -1)
                    {
                        url = url[..nextParamIdx];
                    }

                    var response = await httpClient.GetStringAsync(url, cancellationToken);
                    return OperationResult<string>.CreateSuccess(response);
                }
            }

            return OperationResult<string>.CreateFailure($"Unsupported or malformed genhub:// sharing URI: {input}");
        }

        // Check if local file path
        if (File.Exists(input) || input.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(input))
            {
                return OperationResult<string>.CreateFailure($"Specified profile file does not exist: {input}");
            }

            var fileContent = await File.ReadAllTextAsync(input, cancellationToken);
            if (fileContent.TrimStart().StartsWith('{'))
            {
                return OperationResult<string>.CreateSuccess(fileContent);
            }

            var decompressed = await ProfileSharingCompressionHelper.DecodeAndDecompressAsync(fileContent.Trim(), cancellationToken);
            return OperationResult<string>.CreateSuccess(decompressed);
        }

        // Check if raw JSON string
        if (input.StartsWith('{'))
        {
            return OperationResult<string>.CreateSuccess(input);
        }

        // Try direct Base64Url decompress
        try
        {
            var decompressed = await ProfileSharingCompressionHelper.DecodeAndDecompressAsync(input, cancellationToken);
            return OperationResult<string>.CreateSuccess(decompressed);
        }
        catch (Exception ex)
        {
            return OperationResult<string>.CreateFailure($"Unable to parse shared profile payload: {ex.Message}");
        }
    }

    private async Task<OperationResult<bool>> AcquireMissingManifestAsync(
        SharedManifestDependency dependency,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Check if manifest has download files
            if (dependency.Files?.Count > 0)
            {
                var stagingDir = Path.Combine(Path.GetTempPath(), "GenHub", "SharedImportStaging", dependency.ManifestId);
                Directory.CreateDirectory(stagingDir);

                try
                {
                    if (!ManifestId.TryCreate(dependency.ManifestId, out var manifestId))
                    {
                        manifestId = ManifestId.Create($"1.0.community.mod.{Guid.NewGuid():N}");
                    }

                    var contentManifest = new ContentManifest
                    {
                        Id = manifestId,
                        Name = dependency.DisplayName,
                        Version = dependency.Version,
                        ContentType = dependency.ContentType,
                        Publisher = new PublisherInfo
                        {
                            Name = dependency.Publisher ?? "Community",
                            PublisherType = dependency.PublisherType ?? PublisherTypeConstants.Unknown,
                        },
                        Files = [.. dependency.Files],
                    };

                    foreach (var file in dependency.Files)
                    {
                        if (string.IsNullOrEmpty(file.DownloadUrl))
                        {
                            continue;
                        }

                        var destination = Path.Combine(stagingDir, file.RelativePath);
                        var destinationDir = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrEmpty(destinationDir))
                        {
                            Directory.CreateDirectory(destinationDir);
                        }

                        var bytes = await httpClient.GetByteArrayAsync(file.DownloadUrl, cancellationToken);
                        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
                    }

                    // Check if a specialized factory applies
                    var factory = publisherManifestFactoryResolver.ResolveFactory(contentManifest);
                    if (factory != null)
                    {
                        var createdManifests = await factory.CreateManifestsFromExtractedContentAsync(contentManifest, stagingDir, cancellationToken);
                        foreach (var m in createdManifests)
                        {
                            await manifestPool.AddManifestAsync(m, stagingDir, cancellationToken: cancellationToken);
                        }
                    }
                    else
                    {
                        await manifestPool.AddManifestAsync(contentManifest, stagingDir, cancellationToken: cancellationToken);
                    }

                    return OperationResult<bool>.CreateSuccess(true);
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(stagingDir))
                        {
                            Directory.Delete(stagingDir, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to clean up staging directory {StagingDir}", stagingDir);
                    }
                }
            }

            // 2. Fallback: query orchestrator search for manifest by ID
            var searchResult = await contentOrchestrator.SearchAsync(
                new ContentSearchQuery
                {
                    SearchTerm = dependency.DisplayName,
                    ContentType = dependency.ContentType,
                    Take = 10,
                },
                cancellationToken);

            if (searchResult.Success && searchResult.Data != null)
            {
                var match = searchResult.Data.FirstOrDefault(r => r.Id.Equals(dependency.ManifestId, StringComparison.OrdinalIgnoreCase))
                    ?? searchResult.Data.FirstOrDefault();

                if (match != null)
                {
                    var acquireRes = await contentOrchestrator.AcquireContentAsync(match, progress, cancellationToken);
                    if (acquireRes.Success)
                    {
                        return OperationResult<bool>.CreateSuccess(true);
                    }
                }
            }

            // If unable to acquire but manifest definition is known, synthesize a manifest entry
            if (!ManifestId.TryCreate(dependency.ManifestId, out var fallbackId))
            {
                fallbackId = ManifestId.Create($"1.0.community.mod.{Guid.NewGuid():N}");
            }

            var fallbackManifest = new ContentManifest
            {
                Id = fallbackId,
                Name = dependency.DisplayName,
                Version = dependency.Version,
                ContentType = dependency.ContentType,
                Publisher = new PublisherInfo
                {
                    Name = dependency.Publisher ?? "Community",
                    PublisherType = dependency.PublisherType ?? PublisherTypeConstants.Unknown,
                },
            };

            await manifestPool.AddManifestAsync(fallbackManifest, cancellationToken);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error acquiring missing manifest {ManifestId}", dependency.ManifestId);
            return OperationResult<bool>.CreateFailure($"Failed to acquire manifest: {ex.Message}");
        }
    }

    private Dictionary<string, object?> ExtractSettingsOverridesFromProfile(GameProfile profile)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (profile.VideoResolutionWidth.HasValue) dict[nameof(profile.VideoResolutionWidth)] = profile.VideoResolutionWidth.Value;
        if (profile.VideoResolutionHeight.HasValue) dict[nameof(profile.VideoResolutionHeight)] = profile.VideoResolutionHeight.Value;
        if (profile.VideoWindowed.HasValue) dict[nameof(profile.VideoWindowed)] = profile.VideoWindowed.Value;
        if (profile.VideoTextureQuality.HasValue) dict[nameof(profile.VideoTextureQuality)] = profile.VideoTextureQuality.Value.ToString();
        if (profile.EnableVideoShadows.HasValue) dict[nameof(profile.EnableVideoShadows)] = profile.EnableVideoShadows.Value;
        if (profile.AudioSoundVolume.HasValue) dict[nameof(profile.AudioSoundVolume)] = profile.AudioSoundVolume.Value;
        if (profile.AudioMusicVolume.HasValue) dict[nameof(profile.AudioMusicVolume)] = profile.AudioMusicVolume.Value;
        if (profile.AudioSpeechVolume.HasValue) dict[nameof(profile.AudioSpeechVolume)] = profile.AudioSpeechVolume.Value;

        // TSH settings
        if (profile.TshArchiveReplays.HasValue) dict[nameof(profile.TshArchiveReplays)] = profile.TshArchiveReplays.Value;
        if (profile.TshRenderFpsFontSize.HasValue) dict[nameof(profile.TshRenderFpsFontSize)] = profile.TshRenderFpsFontSize.Value;
        if (profile.TshNetworkLatencyFontSize.HasValue) dict[nameof(profile.TshNetworkLatencyFontSize)] = profile.TshNetworkLatencyFontSize.Value;
        if (profile.TshSystemTimeFontSize.HasValue) dict[nameof(profile.TshSystemTimeFontSize)] = profile.TshSystemTimeFontSize.Value;

        // GO settings
        if (profile.GoShowFps.HasValue) dict[nameof(profile.GoShowFps)] = profile.GoShowFps.Value;
        if (profile.GoShowPing.HasValue) dict[nameof(profile.GoShowPing)] = profile.GoShowPing.Value;
        if (profile.GoShowPlayerRanks.HasValue) dict[nameof(profile.GoShowPlayerRanks)] = profile.GoShowPlayerRanks.Value;
        if (profile.GoRenderFpsLimit.HasValue) dict[nameof(profile.GoRenderFpsLimit)] = profile.GoRenderFpsLimit.Value;

        return dict;
    }

    private void ApplySettingsOverridesToProfile(GameProfile profile, Dictionary<string, object?> overrides)
    {
        ApplyVideoSettingsOverrides(profile, overrides);
        ApplyAudioSettingsOverrides(profile, overrides);
        ApplyTshSettingsOverrides(profile, overrides);
        ApplyGoSettingsOverrides(profile, overrides);
    }

    private void ApplyVideoSettingsOverrides(GameProfile profile, Dictionary<string, object?> overrides)
    {
        if (overrides.TryGetValue(nameof(profile.VideoResolutionWidth), out var widthObj) && widthObj is JsonElement widthElem && widthElem.TryGetInt32(out var width)) profile.VideoResolutionWidth = width;
        if (overrides.TryGetValue(nameof(profile.VideoResolutionHeight), out var heightObj) && heightObj is JsonElement heightElem && heightElem.TryGetInt32(out var height)) profile.VideoResolutionHeight = height;
        if (overrides.TryGetValue(nameof(profile.VideoWindowed), out var winObj) && winObj is JsonElement winElem) profile.VideoWindowed = winElem.GetBoolean();
        if (overrides.TryGetValue(nameof(profile.EnableVideoShadows), out var shadowsObj) && shadowsObj is JsonElement shadowsElem) profile.EnableVideoShadows = shadowsElem.GetBoolean();
    }

    private void ApplyAudioSettingsOverrides(GameProfile profile, Dictionary<string, object?> overrides)
    {
        if (overrides.TryGetValue(nameof(profile.AudioSoundVolume), out var soundObj) && soundObj is JsonElement soundElem && soundElem.TryGetInt32(out var sound)) profile.AudioSoundVolume = sound;
        if (overrides.TryGetValue(nameof(profile.AudioMusicVolume), out var musicObj) && musicObj is JsonElement musicElem && musicElem.TryGetInt32(out var music)) profile.AudioMusicVolume = music;
        if (overrides.TryGetValue(nameof(profile.AudioSpeechVolume), out var speechObj) && speechObj is JsonElement speechElem && speechElem.TryGetInt32(out var speech)) profile.AudioSpeechVolume = speech;
    }

    private void ApplyTshSettingsOverrides(GameProfile profile, Dictionary<string, object?> overrides)
    {
        if (overrides.TryGetValue(nameof(profile.TshArchiveReplays), out var tshReplayObj) && tshReplayObj is JsonElement tshReplayElem) profile.TshArchiveReplays = tshReplayElem.GetBoolean();
        if (overrides.TryGetValue(nameof(profile.TshRenderFpsFontSize), out var tshFpsObj) && tshFpsObj is JsonElement tshFpsElem && tshFpsElem.TryGetInt32(out var fpsSize)) profile.TshRenderFpsFontSize = fpsSize;
        if (overrides.TryGetValue(nameof(profile.TshNetworkLatencyFontSize), out var tshLatObj) && tshLatObj is JsonElement tshLatElem && tshLatElem.TryGetInt32(out var latSize)) profile.TshNetworkLatencyFontSize = latSize;
    }

    private void ApplyGoSettingsOverrides(GameProfile profile, Dictionary<string, object?> overrides)
    {
        if (overrides.TryGetValue(nameof(profile.GoShowFps), out var goFpsObj) && goFpsObj is JsonElement goFpsElem) profile.GoShowFps = goFpsElem.GetBoolean();
        if (overrides.TryGetValue(nameof(profile.GoShowPing), out var goPingObj) && goPingObj is JsonElement goPingElem) profile.GoShowPing = goPingElem.GetBoolean();
        if (overrides.TryGetValue(nameof(profile.GoShowPlayerRanks), out var goRanksObj) && goRanksObj is JsonElement goRanksElem) profile.GoShowPlayerRanks = goRanksElem.GetBoolean();
        if (overrides.TryGetValue(nameof(profile.GoRenderFpsLimit), out var goFpsLimitObj) && goFpsLimitObj is JsonElement goFpsLimitElem && goFpsLimitElem.TryGetInt32(out var fpsLimit)) profile.GoRenderFpsLimit = fpsLimit;
    }
}
