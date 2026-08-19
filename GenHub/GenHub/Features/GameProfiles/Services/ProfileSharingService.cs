using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
            var encodedPayload = ProfileSharingCompressionHelper.CompressAndEncode(json);

            if (encodedPayload.Length > ProfileSharingConstants.MaxInlinePayloadLength)
            {
                logger.LogWarning("Exported profile {ProfileId} payload ({Length} chars) exceeds inline limit.", profileId, encodedPayload.Length);
            }

            string shareUri = $"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}{encodedPayload}";
            return OperationResult<string>.CreateSuccess(shareUri);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error generating share URI for profile {ProfileId}.", profileId);
            return OperationResult<string>.CreateFailure($"Failed to generate share URI: {ex.Message}");
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
    public async Task<OperationResult<SharedProfileInspectionResult>> InspectSharedProfileAsync(
        string shareUriOrJsonOrPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(shareUriOrJsonOrPath))
            {
                return OperationResult<SharedProfileInspectionResult>.CreateFailure("Shared profile payload or path cannot be empty.");
            }

            var rawJsonResult = await ResolvePayloadJsonAsync(shareUriOrJsonOrPath, cancellationToken);
            if (!rawJsonResult.Success || string.IsNullOrEmpty(rawJsonResult.Data))
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

            if (package.SchemaVersion != ProfileSharingConstants.DefaultSchemaVersion)
            {
                return OperationResult<SharedProfileInspectionResult>.CreateFailure($"Unsupported package schema version {package.SchemaVersion}. Expected version {ProfileSharingConstants.DefaultSchemaVersion}.");
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
                if (!ManifestId.TryCreate(reqManifest.ManifestId, out _))
                {
                    return OperationResult<SharedProfileInspectionResult>.CreateFailure($"Invalid manifest identifier '{reqManifest.ManifestId}'. Manifest IDs must follow the 5-segment schema format.");
                }

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
                    totalMissingDownloadBytes += reqManifest.DownloadSize;
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
                    Files = reqManifest.Files,
                });
            }

            // Game installation compatibility matching
            var compatibleInstallations = new List<GameInstallation>();
            string? matchedInstallationId = null;

            var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
            if (installationsResult.Success && installationsResult.Data != null)
            {
                foreach (var inst in installationsResult.Data)
                {
                    bool isCompatible = package.Profile.GameType switch
                    {
                        GameType.Generals => inst.HasGenerals,
                        GameType.ZeroHour => inst.HasZeroHour,
                        _ => inst.HasZeroHour || inst.HasGenerals,
                    };

                    if (isCompatible)
                    {
                        compatibleInstallations.Add(inst);
                    }
                }

                matchedInstallationId = compatibleInstallations.FirstOrDefault()?.Id;
            }

            // Name conflict resolution
            string suggestedName = package.Profile.Name;
            bool hasNameConflict = false;

            var allProfilesResult = await profileRepository.LoadAllProfilesAsync(cancellationToken);
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

            var result = new SharedProfileInspectionResult
            {
                ProfileMetadata = package.Profile,
                Manifests = inspectedManifests,
                TotalDownloadBytesRequired = totalMissingDownloadBytes,
                CachedManifestCount = cachedCount,
                MissingManifestCount = missingCount,
                HasValidGameInstallation = compatibleInstallations.Count > 0,
                MatchedGameInstallationId = matchedInstallationId,
                CompatibleInstallations = compatibleInstallations,
                HasNameConflict = hasNameConflict,
                SuggestedProfileName = suggestedName,
                SecurityWarnings = securityWarnings,
                Package = package,
            };

            return OperationResult<SharedProfileInspectionResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during profile inspection.");
            return OperationResult<SharedProfileInspectionResult>.CreateFailure($"Failed to inspect shared profile: {ex.Message}");
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
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.ProfileName) || request.ProfileName.Length > ProfileSharingConstants.MaxProfileNameLength)
            {
                return OperationResult<GameProfile>.CreateFailure($"Profile name must be between 1 and {ProfileSharingConstants.MaxProfileNameLength} characters.");
            }

            var package = request.Package;
            if (package?.Profile == null)
            {
                return OperationResult<GameProfile>.CreateFailure("Invalid package in import request.");
            }

            if (package.SchemaVersion != ProfileSharingConstants.DefaultSchemaVersion)
            {
                return OperationResult<GameProfile>.CreateFailure($"Unsupported package schema version {package.SchemaVersion}. Expected version {ProfileSharingConstants.DefaultSchemaVersion}.");
            }

            // Validate installation and game type compatibility
            GameInstallation? selectedInstallation = null;
            if (!string.IsNullOrEmpty(request.GameInstallationId))
            {
                var instResult = await installationService.GetInstallationAsync(request.GameInstallationId, cancellationToken);
                if (instResult.Success && instResult.Data != null)
                {
                    selectedInstallation = instResult.Data;
                }
            }

            if (selectedInstallation != null && package.Profile.GameClientManifestId != null)
            {
                if (selectedInstallation.AvailableGameClients.FirstOrDefault(c => c.Id == package.Profile.GameClientManifestId) is { } matchedClient)
                {
                    if (matchedClient.GameType != package.Profile.GameType)
                    {
                        return OperationResult<GameProfile>.CreateFailure($"Client '{matchedClient.Name}' game type ({matchedClient.GameType}) does not match shared profile game type ({package.Profile.GameType}).");
                    }
                }
            }

            // Acquire missing dependencies
            var requiredManifestIds = new List<string>();

            for (int i = 0; i < package.RequiredManifests.Count; i++)
            {
                var dep = package.RequiredManifests[i];
                if (!ManifestId.TryCreate(dep.ManifestId, out _))
                {
                    return OperationResult<GameProfile>.CreateFailure($"Invalid dependency manifest ID '{dep.ManifestId}'.");
                }

                requiredManifestIds.Add(dep.ManifestId);

                var isCachedResult = await manifestPool.IsManifestAcquiredAsync(dep.ManifestId, cancellationToken);
                if (!isCachedResult.Success || !isCachedResult.Data)
                {
                    logger.LogInformation("Acquiring missing dependency for profile import: {ManifestId}", dep.ManifestId);
                    progress?.Report(new ContentAcquisitionProgress
                    {
                        Phase = ContentAcquisitionPhase.Downloading,
                        ProgressPercentage = package.RequiredManifests.Count > 0 ? ((double)i / package.RequiredManifests.Count) * 100.0 : 0.0,
                        CurrentOperation = $"Acquiring {dep.DisplayName}...",
                        CurrentFile = dep.DisplayName,
                        FilesProcessed = i,
                        TotalFiles = package.RequiredManifests.Count,
                    });

                    var acquireResult = await AcquireMissingManifestAsync(dep, progress, cancellationToken);
                    if (!acquireResult.Success)
                    {
                        return OperationResult<GameProfile>.CreateFailure($"Failed to acquire manifest {dep.DisplayName} ({dep.ManifestId}): {acquireResult.FirstError}");
                    }
                }
            }

            // Build GameClient definition
            GameClient? gameClient = null;
            if (selectedInstallation != null)
            {
                gameClient = selectedInstallation.AvailableGameClients.FirstOrDefault(c => c.Id == package.Profile.GameClientManifestId)
                    ?? selectedInstallation.AvailableGameClients.FirstOrDefault(c => c.GameType == package.Profile.GameType)
                    ?? selectedInstallation.AvailableGameClients.FirstOrDefault();
            }

            if (gameClient == null && package.Profile.GameClientManifestId != null)
            {
                gameClient = new GameClient
                {
                    Id = package.Profile.GameClientManifestId,
                    Name = package.Profile.Name,
                    Version = package.Profile.GameVersion,
                    GameType = package.Profile.GameType,
                    ExecutablePath = string.Empty,
                };
            }

            // Build new GameProfile
            var sanitizedArgs = ProfileSharingCompressionHelper.SanitizeCommandLineArguments(
                package.Profile.CommandLineArguments,
                out _);

            var newProfile = new GameProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = request.ProfileName,
                Description = package.Profile.Description,
                ThemeColor = package.Profile.ThemeColor ?? "#1976D2",
                IconPath = package.Profile.IconPath,
                CoverPath = package.Profile.CoverPath,
                GameInstallationId = request.GameInstallationId,
                GameClient = gameClient,
                WorkspaceStrategy = package.Profile.WorkspaceStrategy,
                CommandLineArguments = sanitizedArgs,
                UseSteamLaunch = package.Profile.UseSteamLaunch,
                EnabledContentIds = requiredManifestIds,
            };

            if (request.IncludeGameSettings && package.Profile.GameSettingsOverrides != null)
            {
                ApplySettingsOverridesToProfile(newProfile, package.Profile.GameSettingsOverrides);
            }

            var saveResult = await profileRepository.SaveProfileAsync(newProfile, cancellationToken);
            if (!saveResult.Success || saveResult.Data == null)
            {
                return OperationResult<GameProfile>.CreateFailure(saveResult.Errors);
            }

            logger.LogInformation("Successfully imported profile: {ProfileName} ({ProfileId})", newProfile.Name, newProfile.Id);
            WeakReferenceMessenger.Default.Send(new ProfileCreatedMessage(saveResult.Data));
            return OperationResult<GameProfile>.CreateSuccess(saveResult.Data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during profile import.");
            return OperationResult<GameProfile>.CreateFailure($"Failed to import profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public string GenerateDiscordMarkdown(GameProfile profile, string shareUri)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(shareUri);

        string gameTypeName = profile.GameClient?.GameType.ToString() ?? "Game";
        string version = string.IsNullOrWhiteSpace(profile.GameClient?.Version) ? profile.Version : profile.GameClient.Version;
        string headerInfo = $"{gameTypeName} {version}".Trim();
        string desc = string.IsNullOrWhiteSpace(profile.Description) ? "Custom game configuration for GenHub" : profile.Description;

        return string.Format(
            ProfileSharingConstants.DiscordMarkdownTemplate,
            profile.Name,
            headerInfo,
            desc,
            shareUri);
    }

    private static async Task<bool> IsSafeRemoteUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        try
        {
            var hostEntry = await Dns.GetHostEntryAsync(uri.DnsSafeHost, cancellationToken);
            foreach (var ip in hostEntry.AddressList)
            {
                if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
                {
                    return false;
                }

                byte[] bytes = ip.GetAddressBytes();
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    if (bytes[0] == 10) return false;
                    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                    if (bytes[0] == 192 && bytes[1] == 168) return false;
                    if (bytes[0] == 169 && bytes[1] == 254) return false;
                    if (bytes[0] == 127) return false;
                    if (bytes[0] == 0) return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, object?> ExtractSettingsOverridesFromProfile(GameProfile profile)
    {
        var dict = new Dictionary<string, object?>();

        // Video settings
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

    private async Task<OperationResult<SharedGameProfilePackage>> BuildPackageFromProfileIdAsync(string profileId, CancellationToken cancellationToken)
    {
        var profileResult = await profileRepository.LoadProfileAsync(profileId, cancellationToken);
        if (!profileResult.Success || profileResult.Data == null)
        {
            return OperationResult<SharedGameProfilePackage>.CreateFailure($"Profile not found: {profileId}");
        }

        var profile = profileResult.Data;
        var manifests = new List<SharedManifestDependency>();

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

                    if (encoded.Length > ProfileSharingConstants.MaxInlinePayloadLength)
                    {
                        return OperationResult<string>.CreateFailure($"Inline payload length ({encoded.Length}) exceeds maximum permitted size.");
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

                    if (!Uri.TryCreate(url, UriKind.Absolute, out var profileUri) || !await IsSafeRemoteUriAsync(profileUri, cancellationToken))
                    {
                        return OperationResult<string>.CreateFailure($"Remote URL '{url}' is blocked by security policies.");
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

            var fileInfo = new FileInfo(input);
            if (fileInfo.Length > ProfileSharingConstants.MaxProfileFileBytes)
            {
                return OperationResult<string>.CreateFailure($"Profile file size exceeds maximum limit ({ProfileSharingConstants.MaxProfileFileBytes} bytes).");
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
            if (!ManifestId.TryCreate(dependency.ManifestId, out var validatedManifestId))
            {
                return OperationResult<bool>.CreateFailure($"Invalid manifest ID '{dependency.ManifestId}'.");
            }

            // 1. Check if manifest has download files
            if (dependency.Files?.Count > 0)
            {
                var stagingBase = Path.Combine(Path.GetTempPath(), "GenHub", "SharedImportStaging");
                var stagingDir = Path.Combine(stagingBase, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingDir);

                try
                {
                    var contentManifest = new ContentManifest
                    {
                        Id = validatedManifestId,
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

                    var canonicalStagingPrefix = Path.GetFullPath(stagingDir) + Path.DirectorySeparatorChar;

                    foreach (var file in dependency.Files)
                    {
                        if (string.IsNullOrEmpty(file.DownloadUrl))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(file.RelativePath) || Path.IsPathRooted(file.RelativePath) || file.RelativePath.Contains(".."))
                        {
                            return OperationResult<bool>.CreateFailure($"Invalid relative path in manifest: {file.RelativePath}");
                        }

                        var destination = Path.GetFullPath(Path.Combine(stagingDir, file.RelativePath));
                        if (!destination.StartsWith(canonicalStagingPrefix, StringComparison.Ordinal))
                        {
                            return OperationResult<bool>.CreateFailure($"File path escapes staging directory: {file.RelativePath}");
                        }

                        var destinationDir = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrEmpty(destinationDir))
                        {
                            Directory.CreateDirectory(destinationDir);
                        }

                        if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var fileDownloadUri) || !await IsSafeRemoteUriAsync(fileDownloadUri, cancellationToken))
                        {
                            return OperationResult<bool>.CreateFailure($"Unsafe download URL blocked: {file.DownloadUrl}");
                        }

                        using var response = await httpClient.GetAsync(fileDownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();

                        using var sha256 = SHA256.Create();
                        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        using var fileStream = File.Create(destination);

                        byte[] buffer = new byte[16384];
                        int bytesRead;
                        long totalDownloaded = 0;

                        while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                        {
                            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                            totalDownloaded += bytesRead;

                            if (file.Size > 0 && totalDownloaded > (file.Size + (1024 * 1024)))
                            {
                                return OperationResult<bool>.CreateFailure($"Download size for {file.RelativePath} exceeded expected size limit.");
                            }
                        }

                        sha256.TransformFinalBlock([], 0, 0);
                        var computedHash = Convert.ToHexString(sha256.Hash ?? []).ToLowerInvariant();

                        if (!string.IsNullOrWhiteSpace(file.Hash) && !string.Equals(computedHash, file.Hash.Replace("-", string.Empty).ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                        {
                            return OperationResult<bool>.CreateFailure($"SHA-256 hash mismatch for {file.RelativePath}.");
                        }
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
                        if (Directory.Exists(stagingDir) && Path.GetFullPath(stagingDir).StartsWith(Path.GetFullPath(stagingBase), StringComparison.Ordinal))
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

            // 2. Fallback: query orchestrator search for manifest by EXACT ID match only
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
                var match = searchResult.Data.FirstOrDefault(r => r.Id.Equals(dependency.ManifestId, StringComparison.OrdinalIgnoreCase));
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
            var fallbackManifest = new ContentManifest
            {
                Id = validatedManifestId,
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
