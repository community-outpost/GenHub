using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Services;
using GenHub.Core.Interfaces.Storage;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Service that implements profile package export, URL generation, pre-import inspection, and acquisition.
/// </summary>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ProfileSharingService is the comprehensive facade coordinating storage, profiles, manifest pools, installation, upload, and verification services injected via DI.")]
public class ProfileSharingService(
    IGameProfileRepository profileRepository,
    IContentManifestPool manifestPool,
    IGameInstallationService installationService,
    IContentOrchestrator contentOrchestrator,
    PublisherManifestFactoryResolver publisherManifestFactoryResolver,
    ILogger<ProfileSharingService> logger,
    ICasService? casService = null,
    IUploadThingService? uploadThingService = null,
    IUploadHistoryService? uploadHistoryService = null) : IProfileSharingService, IDisposable
{
    private sealed record ManifestInspectionSummary(
        List<SharedManifestDependency> Manifests,
        int CachedCount,
        int MissingCount,
        long TotalMissingDownloadBytes);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // Hosts validated by the SSRF guard, mapped to the public IP addresses observed at validation time.
    // Static because connections are pinned through a shared handler; entries are refreshed on every validation.
    private static readonly ConcurrentDictionary<string, HashSet<IPAddress>> ValidatedHostAddresses = new(StringComparer.OrdinalIgnoreCase);

    // HTTP client whose connections are pinned to previously validated addresses, defeating DNS rebinding.
    private readonly HttpClient safeHttpClient = CreateSafeHttpClient();

    /// <inheritdoc/>
    public async Task<OperationResult<string>> ExportProfileToUriAsync(string profileId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return OperationResult<string>.CreateFailure(ProfileSharingConstants.EmptyProfileIdErrorMessage);
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
                logger?.LogWarning("Exported profile {ProfileId} payload ({Length} chars) exceeds inline limit.", profileId, encodedPayload.Length);
                return OperationResult<string>.CreateFailure(
                    $"Profile payload ({encodedPayload.Length} characters) exceeds the maximum inline sharing limit of {ProfileSharingConstants.MaxInlinePayloadLength} characters. Please export as a .ghprofile file instead.");
            }

            string shareUri = $"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}{encodedPayload}";
            return OperationResult<string>.CreateSuccess(shareUri);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error generating share URI for profile {ProfileId}.", profileId);
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
                return OperationResult<string>.CreateFailure(ProfileSharingConstants.EmptyProfileIdErrorMessage);
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
            logger?.LogInformation("Exported profile {ProfileId} to file: {DestinationPath}", profileId, destinationPath);
            return OperationResult<string>.CreateSuccess(destinationPath);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error exporting profile {ProfileId} to file {DestinationPath}.", profileId, destinationPath);
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
                return OperationResult<string>.CreateFailure(ProfileSharingConstants.EmptyProfileIdErrorMessage);
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
            logger?.LogError(ex, "Unexpected error exporting profile {ProfileId} to JSON.", profileId);
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

            var packageResult = DeserializeSharedPackage(rawJsonResult.Data);
            if (!packageResult.Success || packageResult.Data == null)
            {
                return OperationResult<SharedProfileInspectionResult>.CreateFailure(packageResult.Errors);
            }

            var package = packageResult.Data;
            _ = ProfileSharingCompressionHelper.SanitizeCommandLineArguments(package.Profile.CommandLineArguments, out var securityWarnings);

            var manifestDiffResult = await DiffManifestsAgainstPoolAsync(package, cancellationToken);
            if (!manifestDiffResult.Success || manifestDiffResult.Data == null)
            {
                return OperationResult<SharedProfileInspectionResult>.CreateFailure(manifestDiffResult.Errors);
            }

            var manifestSummary = manifestDiffResult.Data;
            var (compatibleInstallations, matchedInstallationId) = await FindCompatibleInstallationsAsync(package.Profile.GameType, cancellationToken);
            var (suggestedName, hasNameConflict) = await DetermineSuggestedProfileNameAsync(package.Profile.Name, cancellationToken);

            var result = new SharedProfileInspectionResult
            {
                ProfileMetadata = package.Profile,
                Manifests = manifestSummary.Manifests,
                TotalDownloadBytesRequired = manifestSummary.TotalMissingDownloadBytes,
                CachedManifestCount = manifestSummary.CachedCount,
                MissingManifestCount = manifestSummary.MissingCount,
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
            logger?.LogError(ex, "Unexpected error during profile inspection.");
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
            var validationResult = ValidateImportRequest(request);
            if (!validationResult.Success)
            {
                return OperationResult<GameProfile>.CreateFailure(validationResult.Errors);
            }

            var package = request.Package;
            var selectedInstallation = await ResolveSelectedInstallationAsync(request.GameInstallationId, cancellationToken);
            if (selectedInstallation == null)
            {
                return OperationResult<GameProfile>.CreateFailure(
                    "No compatible game installation is available. Install a matching game before importing this profile.");
            }

            var compatibilityResult = ValidateClientCompatibility(selectedInstallation, package);
            if (!compatibilityResult.Success)
            {
                return OperationResult<GameProfile>.CreateFailure(compatibilityResult.Errors);
            }

            var dependenciesResult = await AcquireAllDependenciesAsync(package, progress, cancellationToken);
            if (!dependenciesResult.Success || dependenciesResult.Data == null)
            {
                return OperationResult<GameProfile>.CreateFailure(dependenciesResult.Errors);
            }

            var gameClient = ResolveGameClient(selectedInstallation, package);
            var newProfile = BuildImportedProfile(request, selectedInstallation, gameClient, dependenciesResult.Data);

            var saveResult = await profileRepository.SaveProfileAsync(newProfile, cancellationToken);
            if (!saveResult.Success || saveResult.Data == null)
            {
                return OperationResult<GameProfile>.CreateFailure(saveResult.Errors);
            }

            logger?.LogInformation("Successfully imported profile: {ProfileName} ({ProfileId})", newProfile.Name, newProfile.Id);
            WeakReferenceMessenger.Default.Send(new ProfileCreatedMessage(saveResult.Data));
            return OperationResult<GameProfile>.CreateSuccess(saveResult.Data);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Unexpected error during profile import.");
            return OperationResult<GameProfile>.CreateFailure($"Failed to import profile: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose; false if from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            safeHttpClient.Dispose();
        }
    }

    private static HttpClient CreateSafeHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = (context, token) =>
                ConnectToValidatedAddressAsync(ValidatedHostAddresses, context, token),
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.DefaultUserAgent);
        return client;
    }

    private static bool IsPublicIpAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !ip.IsIPv6LinkLocal &&
                   !ip.IsIPv6SiteLocal &&
                   !ip.IsIPv6Multicast &&
                   !ip.IsIPv6UniqueLocal;
        }

        if (ip.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] bytes = ip.GetAddressBytes();
        return !IsPrivateOrReservedIpv4(bytes);
    }

    private static bool IsPrivateOrReservedIpv4(byte[] bytes)
    {
        return bytes switch
        {
            [0, ..] => true,
            [10, ..] => true,
            [100, >= 64 and <= 127, ..] => true,
            [127, ..] => true,
            [169, 254, ..] => true,
            [172, >= 16 and <= 31, ..] => true,
            [192, 168, ..] => true,
            [192, 0, 0, ..] => true,
            [192, 0, 2, ..] => true,
            [198, 18 or 19, ..] => true,
            [198, 51, 100, ..] => true,
            [203, 0, 113, ..] => true,
            [>= 224, ..] => true,
            _ => false,
        };
    }

    private static async ValueTask<Stream> ConnectToValidatedAddressAsync(
        ConcurrentDictionary<string, HashSet<IPAddress>> validatedHosts,
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        string host = context.DnsEndPoint.Host;

        // Reject hosts that were never cleared by the SSRF guard, and pin the connection
        // to an address observed during validation so a rebinding DNS answer cannot reroute it.
        if (!validatedHosts.TryGetValue(host, out var allowedAddresses))
        {
            throw new IOException($"Host '{host}' was not validated before connecting.");
        }

        var hostEntry = await Dns.GetHostEntryAsync(host, cancellationToken);
        var candidate = hostEntry.AddressList.FirstOrDefault(allowedAddresses.Contains);
        if (candidate is null)
        {
            throw new IOException($"DNS resolution for '{host}' returned no previously validated addresses.");
        }

        var socket = new Socket(candidate.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        try
        {
            await socket.ConnectAsync(candidate, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<HttpResponseMessage> SendWithManualRedirectsAsync(
        HttpClient client,
        Uri initialUri,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        const int maxRedirects = 5;
        var currentUri = initialUri;

        for (int i = 0; i <= maxRedirects; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            var response = await client.SendAsync(request, completionOption, cancellationToken);

            if (response.StatusCode is HttpStatusCode.MovedPermanently or
                HttpStatusCode.Found or
                HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or
                (HttpStatusCode)308)
            {
                var location = response.Headers.Location;
                response.Dispose();

                if (location == null)
                {
                    throw new HttpRequestException("Redirect response missing Location header.");
                }

                var nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                if (!await IsSafeRemoteUriAsync(nextUri, cancellationToken))
                {
                    throw new HttpRequestException($"Redirect target URL '{nextUri}' is blocked by security policies.");
                }

                currentUri = nextUri;
                continue;
            }

            return response;
        }

        throw new HttpRequestException($"Too many redirects (exceeded limit of {maxRedirects}).");
    }

    private static Dictionary<string, object?> ExtractSettingsOverridesFromProfile(GameProfile profile)
    {
        var dict = new Dictionary<string, object?>();
        ExtractVideoSettingsOverrides(profile, dict);
        ExtractTshSettingsOverrides(profile, dict);
        ExtractGoSettingsOverrides(profile, dict);
        return dict;
    }

    private static void ExtractVideoSettingsOverrides(GameProfile profile, Dictionary<string, object?> dict)
    {
        if (profile.VideoResolutionWidth.HasValue) dict[nameof(profile.VideoResolutionWidth)] = profile.VideoResolutionWidth.Value;
        if (profile.VideoResolutionHeight.HasValue) dict[nameof(profile.VideoResolutionHeight)] = profile.VideoResolutionHeight.Value;
        if (profile.VideoWindowed.HasValue) dict[nameof(profile.VideoWindowed)] = profile.VideoWindowed.Value;
        if (profile.VideoTextureQuality.HasValue) dict[nameof(profile.VideoTextureQuality)] = profile.VideoTextureQuality.Value.ToString();
        if (profile.EnableVideoShadows.HasValue) dict[nameof(profile.EnableVideoShadows)] = profile.EnableVideoShadows.Value;
        if (profile.AudioSoundVolume.HasValue) dict[nameof(profile.AudioSoundVolume)] = profile.AudioSoundVolume.Value;
        if (profile.AudioMusicVolume.HasValue) dict[nameof(profile.AudioMusicVolume)] = profile.AudioMusicVolume.Value;
        if (profile.AudioSpeechVolume.HasValue) dict[nameof(profile.AudioSpeechVolume)] = profile.AudioSpeechVolume.Value;
    }

    private static void ExtractTshSettingsOverrides(GameProfile profile, Dictionary<string, object?> dict)
    {
        if (profile.TshArchiveReplays.HasValue) dict[nameof(profile.TshArchiveReplays)] = profile.TshArchiveReplays.Value;
        if (profile.TshRenderFpsFontSize.HasValue) dict[nameof(profile.TshRenderFpsFontSize)] = profile.TshRenderFpsFontSize.Value;
        if (profile.TshNetworkLatencyFontSize.HasValue) dict[nameof(profile.TshNetworkLatencyFontSize)] = profile.TshNetworkLatencyFontSize.Value;
        if (profile.TshSystemTimeFontSize.HasValue) dict[nameof(profile.TshSystemTimeFontSize)] = profile.TshSystemTimeFontSize.Value;
    }

    private static void ExtractGoSettingsOverrides(GameProfile profile, Dictionary<string, object?> dict)
    {
        if (profile.GoShowFps.HasValue) dict[nameof(profile.GoShowFps)] = profile.GoShowFps.Value;
        if (profile.GoShowPing.HasValue) dict[nameof(profile.GoShowPing)] = profile.GoShowPing.Value;
        if (profile.GoShowPlayerRanks.HasValue) dict[nameof(profile.GoShowPlayerRanks)] = profile.GoShowPlayerRanks.Value;
        if (profile.GoRenderFpsLimit.HasValue) dict[nameof(profile.GoRenderFpsLimit)] = profile.GoRenderFpsLimit.Value;
    }

    private static OperationResult<bool> ValidateImportRequest(SharedProfileImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ProfileName) || request.ProfileName.Length > ProfileSharingConstants.MaxProfileNameLength)
        {
            return OperationResult<bool>.CreateFailure($"Profile name must be between 1 and {ProfileSharingConstants.MaxProfileNameLength} characters.");
        }

        if (request.Package?.Profile == null || request.Package.RequiredManifests == null)
        {
            return OperationResult<bool>.CreateFailure("Package must include profile metadata and required manifests list.");
        }

        if (request.Package.SchemaVersion != ProfileSharingConstants.DefaultSchemaVersion)
        {
            return OperationResult<bool>.CreateFailure($"Unsupported package schema version {request.Package.SchemaVersion}. Expected version {ProfileSharingConstants.DefaultSchemaVersion}.");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static OperationResult<bool> ValidateClientCompatibility(GameInstallation? installation, SharedGameProfilePackage package)
    {
        if (installation == null || package.Profile.GameClientManifestId == null)
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        var matchedClient = installation.AvailableGameClients.FirstOrDefault(c => c.Id == package.Profile.GameClientManifestId);
        if (matchedClient is { } client && client.GameType != package.Profile.GameType)
        {
            return OperationResult<bool>.CreateFailure($"Client '{client.Name}' game type ({client.GameType}) does not match shared profile game type ({package.Profile.GameType}).");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static GameClient? ResolveGameClient(GameInstallation? installation, SharedGameProfilePackage package)
    {
        if (installation != null)
        {
            var matched = installation.AvailableGameClients.FirstOrDefault(c => c.Id == package.Profile.GameClientManifestId)
                ?? installation.AvailableGameClients.FirstOrDefault(c => c.GameType == package.Profile.GameType);

            if (matched != null)
            {
                return matched;
            }
        }

        if (package.Profile.GameClientManifestId != null)
        {
            return new GameClient
            {
                Id = package.Profile.GameClientManifestId,
                Name = package.Profile.Name,
                Version = package.Profile.GameVersion,
                GameType = package.Profile.GameType,
                ExecutablePath = string.Empty,
            };
        }

        return null;
    }

    /// <summary>
    /// Removes machine-specific artwork paths before a profile is packaged for sharing.
    /// Local absolute paths, UNC paths, and traversal segments are stripped to
    /// avoid leaking exporter filesystem details to recipients. Built-in application
    /// assets (avares://, /Assets/, Assets/) are preserved.
    /// </summary>
    private static string? SanitizeShareableArtworkPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();

        // Built-in assets and Avalonia resources are safe and portable across all GenHub installations
        if (trimmed.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        bool isWindowsDrive = (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':') ||
            (trimmed.Length >= 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':' && (trimmed[2] == '/' || trimmed[2] == '\\'));
        bool isRooted = Path.IsPathRooted(trimmed) ||
            isWindowsDrive ||
            trimmed.StartsWith('/') ||
            trimmed.StartsWith('\\');

        bool isShareable = !isRooted &&
            !trimmed.Contains("://", StringComparison.Ordinal) &&
            !trimmed.Contains("..", StringComparison.Ordinal);

        return isShareable ? trimmed : null;
    }

    private static async Task<bool> IsSafeRemoteUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.DnsSafeHost))
        {
            return false;
        }

        try
        {
            var hostEntry = await Dns.GetHostEntryAsync(uri.DnsSafeHost, cancellationToken);
            var publicAddresses = new HashSet<IPAddress>();

            foreach (var ip in hostEntry.AddressList)
            {
                if (!IsPublicIpAddress(ip))
                {
                    return false;
                }

                publicAddresses.Add(ip);
            }

            if (publicAddresses.Count == 0)
            {
                return false;
            }

            ValidatedHostAddresses[uri.DnsSafeHost] = publicAddresses;
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<OperationResult<string>> ResolvePayloadFromLocalFileAsync(string input, CancellationToken cancellationToken)
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

        try
        {
            var decompressed = await ProfileSharingCompressionHelper.DecodeAndDecompressAsync(fileContent.Trim(), cancellationToken);
            return OperationResult<string>.CreateSuccess(decompressed);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        {
            return OperationResult<string>.CreateFailure($"Unable to parse shared profile file '{input}': {ex.Message}");
        }
    }

    private static async Task<OperationResult<string>> ResolvePayloadFromRawOrCompressedAsync(string input, CancellationToken cancellationToken)
    {
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

    private static void ApplySettingsOverridesToProfile(GameProfile profile, Dictionary<string, object?> overrides)
    {
        ApplyVideoSettingsOverrides(profile, overrides);
        ApplyAudioSettingsOverrides(profile, overrides);
        ApplyTshSettingsOverrides(profile, overrides);
        ApplyGoSettingsOverrides(profile, overrides);
    }

    private static void ApplyVideoSettingsOverrides(GameProfile profile, Dictionary<string, object?> overrides)
    {
        if (TryReadInt32(overrides, nameof(profile.VideoResolutionWidth), out var width)) profile.VideoResolutionWidth = width;
        if (TryReadInt32(overrides, nameof(profile.VideoResolutionHeight), out var height)) profile.VideoResolutionHeight = height;
        if (TryReadBoolean(overrides, nameof(profile.VideoWindowed), out var windowed)) profile.VideoWindowed = windowed;
        if (TryReadBoolean(overrides, nameof(profile.EnableVideoShadows), out var shadows)) profile.EnableVideoShadows = shadows;
        if (TryReadTextureQuality(overrides, nameof(profile.VideoTextureQuality), out var textureQuality)) profile.VideoTextureQuality = textureQuality;
    }

    private static void ApplyAudioSettingsOverrides(GameProfile profile, Dictionary<string, object?> overrides)
    {
        if (TryReadInt32(overrides, nameof(profile.AudioSoundVolume), out var sound)) profile.AudioSoundVolume = sound;
        if (TryReadInt32(overrides, nameof(profile.AudioMusicVolume), out var music)) profile.AudioMusicVolume = music;
        if (TryReadInt32(overrides, nameof(profile.AudioSpeechVolume), out var speech)) profile.AudioSpeechVolume = speech;
    }

    private static void ApplyTshSettingsOverrides(GameProfile profile, Dictionary<string, object?> overrides)
    {
        if (TryReadBoolean(overrides, nameof(profile.TshArchiveReplays), out var tshArchiveReplays)) profile.TshArchiveReplays = tshArchiveReplays;
        if (TryReadInt32(overrides, nameof(profile.TshRenderFpsFontSize), out var fpsSize)) profile.TshRenderFpsFontSize = fpsSize;
        if (TryReadInt32(overrides, nameof(profile.TshNetworkLatencyFontSize), out var latSize)) profile.TshNetworkLatencyFontSize = latSize;
        if (TryReadInt32(overrides, nameof(profile.TshSystemTimeFontSize), out var timeSize)) profile.TshSystemTimeFontSize = timeSize;
    }

    private static void ApplyGoSettingsOverrides(GameProfile profile, Dictionary<string, object?> overrides)
    {
        if (TryReadBoolean(overrides, nameof(profile.GoShowFps), out var showFps)) profile.GoShowFps = showFps;
        if (TryReadBoolean(overrides, nameof(profile.GoShowPing), out var showPing)) profile.GoShowPing = showPing;
        if (TryReadBoolean(overrides, nameof(profile.GoShowPlayerRanks), out var showPlayerRanks)) profile.GoShowPlayerRanks = showPlayerRanks;
        if (TryReadInt32(overrides, nameof(profile.GoRenderFpsLimit), out var fpsLimit)) profile.GoRenderFpsLimit = fpsLimit;
    }

    private static bool TryReadInt32(Dictionary<string, object?> overrides, string key, out int value)
    {
        value = 0;
        if (!overrides.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case int i:
                value = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                value = (int)l;
                return true;
            case short s:
                value = s;
                return true;
            case byte b:
                value = b;
                return true;
            case JsonElement elem when elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out var parsed):
                value = parsed;
                return true;
            case string str when int.TryParse(str, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadTextureQuality(Dictionary<string, object?> overrides, string key, out TextureQuality quality)
    {
        quality = TextureQuality.High;
        if (!overrides.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case TextureQuality tq:
                quality = tq;
                return true;
            case string str when Enum.TryParse<TextureQuality>(str, ignoreCase: true, out var parsed):
                quality = parsed;
                return true;
            case JsonElement elem when elem.ValueKind == JsonValueKind.String && Enum.TryParse<TextureQuality>(elem.GetString(), ignoreCase: true, out var parsed):
                quality = parsed;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadBoolean(Dictionary<string, object?> overrides, string key, out bool value)
    {
        value = false;
        if (!overrides.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case bool b:
                value = b;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static Dictionary<string, string> ParseQueryParameters(string queryString)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return parameters;
        }

        var pairs = queryString.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            int eqIdx = pair.IndexOf('=');
            if (eqIdx >= 0)
            {
                string key = Uri.UnescapeDataString(pair[..eqIdx]);
                string val = Uri.UnescapeDataString(pair[(eqIdx + 1)..]);
                parameters[key] = val;
            }
            else
            {
                parameters[Uri.UnescapeDataString(pair)] = string.Empty;
            }
        }

        return parameters;
    }

    private static async Task<OperationResult<string>> ResolveInlinePayloadAsync(string encoded, CancellationToken cancellationToken)
    {
        if (encoded.Length > ProfileSharingConstants.MaxInlinePayloadLength)
        {
            return OperationResult<string>.CreateFailure($"Inline payload length ({encoded.Length}) exceeds maximum permitted size.");
        }

        try
        {
            string decompressed = await ProfileSharingCompressionHelper.DecodeAndDecompressAsync(encoded, cancellationToken);
            return OperationResult<string>.CreateSuccess(decompressed);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        {
            return OperationResult<string>.CreateFailure($"Unable to parse inline shared profile payload: {ex.Message}");
        }
    }

    private static bool IsCustomLocalManifest(ContentManifest manifest)
    {
        if (manifest.ContentType == ContentType.GameInstallation ||
            manifest.Id.ToString().Contains(ManifestConstants.GameInstallationSegment, StringComparison.OrdinalIgnoreCase) ||
            manifest.Id.ToString().Contains(ManifestConstants.GameClientSegment, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (manifest.Publisher?.PublisherType == PublisherTypeConstants.Local ||
            string.Equals(manifest.Publisher?.PublisherType, PublisherTypeConstants.Local, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(manifest.Publisher?.Name, PublisherTypeConstants.Local, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return manifest.Id.ToString().Contains(ManifestConstants.LocalSegment, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<OperationResult<bool>> ExtractAndVerifyPackageFilesAsync(
        string tempZipPath,
        string stagingDir,
        SharedManifestDependency dependency,
        CancellationToken cancellationToken)
    {
        string canonicalStagingDir = GetCanonicalStagingDirectory(stagingDir);

        var extractResult = await ExtractArchiveEntriesAsync(tempZipPath, stagingDir, canonicalStagingDir, cancellationToken);
        if (!extractResult.Success)
        {
            return extractResult;
        }

        return await VerifyExtractedFilesAsync(dependency, stagingDir, canonicalStagingDir, cancellationToken);
    }

    private static string GetCanonicalStagingDirectory(string stagingDir)
    {
        string canonicalStagingDir = Path.GetFullPath(stagingDir);
        return canonicalStagingDir.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalStagingDir
            : canonicalStagingDir + Path.DirectorySeparatorChar;
    }

    [SuppressMessage("Major Code Smell", "S6966:Await async method instead of sync counterpart", Justification = "ZipFile.OpenRead and ZipArchiveEntry.Open have no asynchronous OpenReadAsync/OpenAsync methods in .NET 8 BCL.")]
    private static async Task<OperationResult<bool>> ExtractArchiveEntriesAsync(
        string tempZipPath,
        string stagingDir,
        string canonicalStagingDir,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(tempZipPath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var normalizedRelativePath = entry.FullName.Replace('\\', '/');
                var destinationPath = Path.GetFullPath(Path.Combine(stagingDir, normalizedRelativePath));
                if (!destinationPath.StartsWith(canonicalStagingDir, StringComparison.Ordinal))
                {
                    return OperationResult<bool>.CreateFailure($"Package contains entry attempting directory traversal: {entry.FullName}");
                }

                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await using (var entryStream = entry.Open())
                await using (var outputStream = File.Create(destinationPath))
                {
                    await entryStream.CopyToAsync(outputStream, cancellationToken);
                }
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (InvalidDataException ex)
        {
            return OperationResult<bool>.CreateFailure($"Archive extraction failed: {ex.Message}");
        }
        catch (IOException ex)
        {
            return OperationResult<bool>.CreateFailure($"Archive I/O error: {ex.Message}");
        }
    }

    private static async Task<OperationResult<bool>> VerifyExtractedFilesAsync(
        SharedManifestDependency dependency,
        string stagingDir,
        string canonicalStagingDir,
        CancellationToken cancellationToken)
    {
        if (dependency.Files is not { Count: > 0 })
        {
            return OperationResult<bool>.CreateFailure($"Shared package '{dependency.DisplayName}' ({dependency.ManifestId}) does not define any files.");
        }

        foreach (var file in dependency.Files)
        {
            var validatePathResult = ValidateExtractedFilePath(file.RelativePath, stagingDir, canonicalStagingDir);
            if (!validatePathResult.Success)
            {
                return OperationResult<bool>.CreateFailure(validatePathResult.Errors);
            }

            var filePath = validatePathResult.Data;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return OperationResult<bool>.CreateFailure($"Package contains invalid path for: {file.RelativePath}");
            }

            if (!string.IsNullOrWhiteSpace(file.Hash))
            {
                var hashCheckResult = await ValidateFileChecksumAsync(filePath, file.RelativePath, file.Hash, cancellationToken);
                if (!hashCheckResult.Success)
                {
                    return hashCheckResult;
                }
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static OperationResult<string> ValidateExtractedFilePath(
        string? relativePath,
        string stagingDir,
        string canonicalStagingDir)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return OperationResult<string>.CreateFailure("Package contains a file with an empty or missing relative path.");
        }

        var normalizedPath = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalizedPath) ||
            normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains(ProfileSharingConstants.ParentDirectorySegment))
        {
            return OperationResult<string>.CreateFailure($"Package contains invalid relative path: {relativePath}");
        }

        var filePath = Path.GetFullPath(Path.Combine(stagingDir, normalizedPath));
        if (!filePath.StartsWith(canonicalStagingDir, StringComparison.Ordinal))
        {
            return OperationResult<string>.CreateFailure($"Package path escapes staging directory: {relativePath}");
        }

        if (!File.Exists(filePath))
        {
            return OperationResult<string>.CreateFailure($"Package is missing expected file: {relativePath}");
        }

        return OperationResult<string>.CreateSuccess(filePath);
    }

    private static async Task<OperationResult<bool>> ValidateFileChecksumAsync(
        string filePath,
        string relativePath,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        using var sha = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha.ComputeHashAsync(stream, cancellationToken);
        var actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var normalizedExpectedHash = expectedHash.Replace("-", string.Empty).ToLowerInvariant();

        if (!string.Equals(actualHash, normalizedExpectedHash, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<bool>.CreateFailure($"SHA-256 hash mismatch for {relativePath}.");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<GameInstallation?> ResolveSelectedInstallationAsync(string? installationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(installationId))
        {
            return null;
        }

        var instResult = await installationService.GetInstallationAsync(installationId, cancellationToken);
        return instResult.Success ? instResult.Data : null;
    }

    private async Task<OperationResult<List<string>>> AcquireAllDependenciesAsync(
        SharedGameProfilePackage package,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var requiredManifestIds = new List<string>();

        for (int i = 0; i < package.RequiredManifests.Count; i++)
        {
            var dep = package.RequiredManifests[i];
            if (dep.ContentType == ContentType.GameInstallation ||
                dep.ManifestId.Contains(ManifestConstants.GameInstallationSegment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ManifestId.TryCreate(dep.ManifestId, out _))
            {
                return OperationResult<List<string>>.CreateFailure($"Invalid dependency manifest ID '{dep.ManifestId}'.");
            }

            requiredManifestIds.Add(dep.ManifestId);

            var isCachedResult = await manifestPool.IsManifestAcquiredAsync(dep.ManifestId, cancellationToken);
            if (!isCachedResult.Success || !isCachedResult.Data)
            {
                logger?.LogInformation("Acquiring missing dependency for profile import: {ManifestId}", dep.ManifestId);
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
                    return OperationResult<List<string>>.CreateFailure($"Failed to acquire manifest {dep.DisplayName} ({dep.ManifestId}): {acquireResult.FirstError}");
                }
            }
        }

        return OperationResult<List<string>>.CreateSuccess(requiredManifestIds);
    }

    private GameProfile BuildImportedProfile(
        SharedProfileImportRequest request,
        GameInstallation? selectedInstallation,
        GameClient? gameClient,
        List<string> requiredManifestIds)
    {
        var package = request.Package;
        var sanitizedArgs = ProfileSharingCompressionHelper.SanitizeCommandLineArguments(
            package.Profile.CommandLineArguments,
            out _);

        var enabledIds = new List<string>(requiredManifestIds);

        // If a local game installation is selected, resolve and attach the matching local GameInstallation manifest ID
        if (selectedInstallation != null)
        {
            var baseGameClient = selectedInstallation.AvailableGameClients
                .FirstOrDefault(c => c.GameType == package.Profile.GameType && !c.IsPublisherClient)
                ?? selectedInstallation.AvailableGameClients.FirstOrDefault(c => c.GameType == package.Profile.GameType);

            if (baseGameClient != null)
            {
                var version = GameVersionHelper.NormalizeVersion(baseGameClient.Version);
                var localInstallManifestId = ManifestIdGenerator.GenerateGameInstallationId(
                    selectedInstallation,
                    package.Profile.GameType,
                    version);

                if (!enabledIds.Contains(localInstallManifestId))
                {
                    enabledIds.Add(localInstallManifestId);
                }
            }
        }

        var newProfile = new GameProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.ProfileName,
            Description = package.Profile.Description,
            ThemeColor = package.Profile.ThemeColor ?? "#1976D2",
            IconPath = SanitizeShareableArtworkPath(package.Profile.IconPath),
            CoverPath = SanitizeShareableArtworkPath(package.Profile.CoverPath),
            GameInstallationId = request.GameInstallationId,
            GameClient = gameClient,
            WorkspaceStrategy = request.WorkspaceStrategy ?? package.Profile.WorkspaceStrategy,
            CommandLineArguments = sanitizedArgs,
            UseSteamLaunch = selectedInstallation != null
                ? selectedInstallation.InstallationType == GameInstallationType.Steam
                : package.Profile.UseSteamLaunch ?? false,
            EnabledContentIds = enabledIds,
        };

        if (request.IncludeGameSettings && package.Profile.GameSettingsOverrides != null)
        {
            ApplySettingsOverridesToProfile(newProfile, package.Profile.GameSettingsOverrides);
        }

        return newProfile;
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
            if (manifestResult is { Success: true, Data: not null })
            {
                var manifest = manifestResult.Data;

                // Exclude local GameInstallation manifests as base game installations are locally scanned
                if (manifest.ContentType == ContentType.GameInstallation)
                {
                    continue;
                }

                var dependency = await BuildManifestDependencyAsync(manifest, cancellationToken);
                manifests.Add(dependency);
            }
            else
            {
                // Exclude any gameinstallation IDs
                if (contentId.Contains(ManifestConstants.GameInstallationSegment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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
                IconPath = SanitizeShareableArtworkPath(profile.IconPath),
                CoverPath = SanitizeShareableArtworkPath(profile.CoverPath),
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

    private async Task<SharedManifestDependency> BuildManifestDependencyAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken)
    {
        var dependencyFiles = (manifest.Files ?? []).Select(f => new ManifestFile
        {
            RelativePath = f.RelativePath,
            Hash = f.Hash,
            Size = f.Size,
            SourceType = f.SourceType != ContentSourceType.Unknown ? f.SourceType : ContentSourceType.ContentAddressable,
            InstallTarget = f.InstallTarget,
            IsRequired = f.IsRequired,
            IsExecutable = f.IsExecutable,
            Permissions = f.Permissions,
            DownloadUrl = f.DownloadUrl,
            SourcePath = f.SourcePath,
            PatchSourceFile = f.PatchSourceFile,
            PackageInfo = f.PackageInfo,
        }).ToList();

        string? packageUrl = null;
        string? packageHash = null;

        bool isLocal = IsCustomLocalManifest(manifest);

        if (isLocal && uploadThingService != null && casService != null && dependencyFiles.Count > 0)
        {
            var (uploadedUrl, uploadHash) = await PackageAndUploadLocalManifestAsync(manifest, cancellationToken);
            if (!string.IsNullOrWhiteSpace(uploadedUrl))
            {
                packageUrl = uploadedUrl;
                packageHash = uploadHash;
                dependencyFiles = dependencyFiles.Select(f => new ManifestFile
                {
                    RelativePath = f.RelativePath,
                    Hash = f.Hash,
                    Size = f.Size,
                    SourceType = f.SourceType != ContentSourceType.Unknown ? f.SourceType : ContentSourceType.ContentAddressable,
                    InstallTarget = f.InstallTarget,
                    IsRequired = f.IsRequired,
                    DownloadUrl = uploadedUrl,
                    IsExecutable = f.IsExecutable,
                    Permissions = f.Permissions,
                    SourcePath = f.SourcePath,
                    PatchSourceFile = f.PatchSourceFile,
                    PackageInfo = f.PackageInfo,
                }).ToList();
            }
        }

        return new SharedManifestDependency
        {
            ManifestId = manifest.Id.ToString(),
            DisplayName = manifest.Name,
            Version = manifest.Version,
            ContentType = manifest.ContentType,
            TargetGame = manifest.TargetGame,
            Publisher = manifest.Publisher?.Name,
            PublisherType = manifest.Publisher?.PublisherType,
            DownloadSize = dependencyFiles.Sum(f => f.Size),
            IsCachedLocally = true,
            Hash = packageHash ?? dependencyFiles.FirstOrDefault()?.Hash,
            PackageUrl = packageUrl,
            PackageHash = packageHash,
            Files = dependencyFiles,
        };
    }

    private async Task<(string? Url, string? Hash)> PackageAndUploadLocalManifestAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken)
    {
        if (uploadThingService == null || casService == null || manifest.Files is not { Count: > 0 })
        {
            return (null, null);
        }

        var stagingBase = Path.Combine(Path.GetTempPath(), AppConstants.AppName, "CloudUploadStaging");
        var tempZipPath = Path.Combine(stagingBase, $"{Guid.NewGuid():N}.zip");

        try
        {
            Directory.CreateDirectory(stagingBase);
            var zipHash = await CreateLocalManifestArchiveAsync(tempZipPath, manifest, cancellationToken);
            if (string.IsNullOrEmpty(zipHash))
            {
                return (null, null);
            }

            return await UploadLocalManifestPackageAsync(manifest, tempZipPath, zipHash, cancellationToken);
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<ProfileSharingService>.Instance).LogError(ex, "Unexpected error uploading local manifest {ManifestId}", manifest.Id);
            return (null, null);
        }
        finally
        {
            CleanupStagingFile(tempZipPath);
        }
    }

    private async Task<string?> CreateLocalManifestArchiveAsync(
        string tempZipPath,
        ContentManifest manifest,
        CancellationToken cancellationToken)
    {
        if (casService == null || manifest.Files == null)
        {
            return null;
        }

        var sortedFiles = manifest.Files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
        var fixedTimestamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using (var zipFile = File.Create(tempZipPath))
        using (var archive = new ZipArchive(zipFile, ZipArchiveMode.Create))
        {
            foreach (var file in sortedFiles)
            {
                var contentPathResult = await casService.GetContentPathAsync(file.Hash, manifest.ContentType, cancellationToken);
                if (contentPathResult.Success && File.Exists(contentPathResult.Data))
                {
                    var entryName = file.RelativePath.Replace('\\', '/');
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                    entry.LastWriteTime = fixedTimestamp;

                    await using var sourceStream = File.OpenRead(contentPathResult.Data);
                    await using var entryStream = entry.Open();
                    await sourceStream.CopyToAsync(entryStream, cancellationToken);
                }
                else
                {
                    (logger ?? NullLogger<ProfileSharingService>.Instance).LogWarning(
                        "File {RelativePath} ({Hash}) not found in CAS for local manifest {ManifestId}.",
                        file.RelativePath,
                        file.Hash,
                        manifest.Id);
                }
            }
        }

        var zipInfo = new FileInfo(tempZipPath);
        if (zipInfo.Length == 0)
        {
            return null;
        }

        using var sha = SHA256.Create();
        await using var readStream = File.OpenRead(tempZipPath);
        var hashBytes = await sha.ComputeHashAsync(readStream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private async Task<(string? Url, string? Hash)> UploadLocalManifestPackageAsync(
        ContentManifest manifest,
        string tempZipPath,
        string zipHash,
        CancellationToken cancellationToken)
    {
        if (uploadHistoryService != null)
        {
            var existing = await uploadHistoryService.FindExistingUploadAsync(zipHash);
            if (existing != null && !string.IsNullOrWhiteSpace(existing.Url))
            {
                (logger ?? NullLogger<ProfileSharingService>.Instance).LogInformation(
                    "Reusing existing cloud upload for local manifest {ManifestId}: {Url}",
                    manifest.Id,
                    existing.Url);
                return (existing.Url, zipHash);
            }
        }

        var zipInfo = new FileInfo(tempZipPath);
        if (zipInfo.Length > ProfileSharingConstants.MaxCloudUploadSizeBytes)
        {
            (logger ?? NullLogger<ProfileSharingService>.Instance).LogWarning(
                "Local manifest {ManifestId} size ({Size} bytes) exceeds cloud upload quota of {Max} bytes.",
                manifest.Id,
                zipInfo.Length,
                ProfileSharingConstants.MaxCloudUploadSizeBytes);
            return (null, zipHash);
        }

        if (uploadThingService == null)
        {
            return (null, zipHash);
        }

        var uploadResult = await uploadThingService.UploadFileAsync(tempZipPath, null, cancellationToken);
        if (uploadResult.Success && uploadResult.Data != null)
        {
            var data = uploadResult.Data;
            uploadHistoryService?.RecordUpload(
                zipInfo.Length,
                data.PublicUrl,
                $"{manifest.Name}.zip",
                data.FileKey,
                data.DeleteToken,
                zipHash,
                ProfileSharingConstants.UploadCategoryProfiles);

            (logger ?? NullLogger<ProfileSharingService>.Instance).LogInformation(
                "Uploaded local manifest {ManifestId} package to cloud: {Url}",
                manifest.Id,
                data.PublicUrl);

            return (data.PublicUrl, zipHash);
        }

        (logger ?? NullLogger<ProfileSharingService>.Instance).LogWarning(
            "Failed to upload local manifest {ManifestId} package: {Error}",
            manifest.Id,
            uploadResult.FirstError);
        return (null, zipHash);
    }

    private async Task<OperationResult<string>> ResolvePayloadJsonAsync(string shareUriOrJsonOrPath, CancellationToken cancellationToken)
    {
        string input = shareUriOrJsonOrPath.Trim();

        if (input.StartsWith(CommandLineConstants.UriScheme, StringComparison.OrdinalIgnoreCase))
        {
            return await ResolvePayloadFromUriAsync(input, cancellationToken);
        }

        if (File.Exists(input) || input.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return await ResolvePayloadFromLocalFileAsync(input, cancellationToken);
        }

        if (input.StartsWith('{'))
        {
            return OperationResult<string>.CreateSuccess(input);
        }

        return await ResolvePayloadFromRawOrCompressedAsync(input, cancellationToken);
    }

    private async Task<OperationResult<string>> ResolvePayloadFromUriAsync(string input, CancellationToken cancellationToken)
    {
        if (input.StartsWith(CommandLineConstants.ProfileImportUriPrefix, StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith(CommandLineConstants.ProfileViewUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int queryIndex = input.IndexOf('?');
            if (queryIndex != -1 && queryIndex < input.Length - 1)
            {
                var queryString = input[(queryIndex + 1)..];
                var queryParams = ParseQueryParameters(queryString);

                if (queryParams.TryGetValue("url", out var remoteUrl) && !string.IsNullOrWhiteSpace(remoteUrl))
                {
                    return await ResolveRemotePayloadAsync(remoteUrl, cancellationToken);
                }

                if (queryParams.TryGetValue("data", out var inlineData) && !string.IsNullOrWhiteSpace(inlineData))
                {
                    return await ResolveInlinePayloadAsync(inlineData, cancellationToken);
                }
            }
        }

        return OperationResult<string>.CreateFailure($"Unsupported or malformed genhub:// sharing URI: {input}");
    }

    private async Task<OperationResult<string>> ResolveRemotePayloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var profileUri) || !await IsSafeRemoteUriAsync(profileUri, cancellationToken))
        {
            return OperationResult<string>.CreateFailure($"Remote URL '{url}' is blocked by security policies.");
        }

        return await FetchRemotePayloadWithLimitAsync(profileUri, cancellationToken);
    }

    private async Task<OperationResult<string>> FetchRemotePayloadWithLimitAsync(Uri profileUri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendWithManualRedirectsAsync(safeHttpClient, profileUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffered = new MemoryStream();

            byte[] buffer = new byte[8192];
            int bytesRead = 0;
            long totalBytes = 0;

            while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > ProfileSharingConstants.MaxDecompressedPayloadBytes)
                {
                    return OperationResult<string>.CreateFailure(
                        $"Remote profile payload exceeds maximum allowed size ({ProfileSharingConstants.MaxDecompressedPayloadBytes} bytes).");
                }

                await buffered.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            return OperationResult<string>.CreateSuccess(Encoding.UTF8.GetString(buffered.ToArray()));
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning(ex, "Failed to fetch remote profile payload from {Uri}", profileUri);
            return OperationResult<string>.CreateFailure($"Failed to fetch remote profile payload: {ex.Message}");
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

            // 1. Check if manifest is already fully acquired in the manifest pool
            var acquiredResult = await manifestPool.IsManifestAcquiredAsync(validatedManifestId, cancellationToken);
            if (acquiredResult.Success && acquiredResult.Data)
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            // 2. Check if all required files already exist in local CAS storage (zero-download path)
            var casCheckResult = await TryRegisterFromExistingCasBlobsAsync(validatedManifestId, dependency, cancellationToken);
            if (casCheckResult.Success && casCheckResult.Data)
            {
                logger?.LogInformation("Manifest {ManifestId} was reconstructed directly from existing CAS pool objects without downloading.", dependency.ManifestId);
                return OperationResult<bool>.CreateSuccess(true);
            }

            // 3. Check if this is a shared package (e.g. UploadThing or direct zip package)
            string? packageUrl = !string.IsNullOrWhiteSpace(dependency.PackageUrl)
                ? dependency.PackageUrl
                : dependency.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.DownloadUrl))?.DownloadUrl;

            bool isZipArchive = !string.IsNullOrWhiteSpace(packageUrl) &&
                (!string.IsNullOrWhiteSpace(dependency.PackageUrl) ||
                 packageUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                 packageUrl.Contains(ApiConstants.UploadThingUrlFragment, StringComparison.OrdinalIgnoreCase) ||
                 packageUrl.Contains(ApiConstants.UploadThingUfsUrlFragment, StringComparison.OrdinalIgnoreCase) ||
                 packageUrl.Contains(ApiConstants.UploadThingUfsShortUrlFragment, StringComparison.OrdinalIgnoreCase));

            if (isZipArchive && !string.IsNullOrWhiteSpace(packageUrl))
            {
                return await DownloadAndRegisterZipPackageAsync(dependency, validatedManifestId, packageUrl, cancellationToken);
            }

            // 4. Direct per-file download is only possible when files exist, and each file has a direct download URL and hash.
            if (dependency.Files?.Count > 0 && dependency.Files.All(f => !string.IsNullOrWhiteSpace(f.DownloadUrl) && !string.IsNullOrWhiteSpace(f.Hash)))
            {
                return await DownloadAndRegisterManifestFilesAsync(dependency, validatedManifestId, cancellationToken);
            }

            // 5. Fallback: Search & acquire from connected content provider pipeline (GeneralsOnline, ModDB, GenPatcher, etc.)
            return await SearchAndAcquireFallbackManifestAsync(dependency, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<ProfileSharingService>.Instance).LogError(ex, "Error acquiring missing manifest {ManifestId}", dependency.ManifestId);
            return OperationResult<bool>.CreateFailure($"Failed to acquire manifest: {ex.Message}");
        }
    }

    private async Task<OperationResult<bool>> TryRegisterFromExistingCasBlobsAsync(
        ManifestId validatedManifestId,
        SharedManifestDependency dependency,
        CancellationToken cancellationToken)
    {
        if (casService == null || dependency.Files is not { Count: > 0 } || dependency.Files.Any(f => string.IsNullOrWhiteSpace(f.Hash)))
        {
            return OperationResult<bool>.CreateSuccess(false);
        }

        foreach (var file in dependency.Files)
        {
            var blobPathResult = await casService.GetContentPathAsync(file.Hash, dependency.ContentType, cancellationToken);
            if (!blobPathResult.Success || !File.Exists(blobPathResult.Data))
            {
                return OperationResult<bool>.CreateSuccess(false);
            }
        }

        var stagingBase = Path.Combine(Path.GetTempPath(), AppConstants.AppName, "CasMaterializeStaging");
        var stagingDir = Path.Combine(stagingBase, Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(stagingDir);
            foreach (var file in dependency.Files)
            {
                var blobPathResult = await casService.GetContentPathAsync(file.Hash, dependency.ContentType, cancellationToken);
                var normalizedRelativePath = file.RelativePath.Replace('\\', '/');
                var destinationPath = Path.GetFullPath(Path.Combine(stagingDir, normalizedRelativePath));
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (blobPathResult.Success && !string.IsNullOrWhiteSpace(blobPathResult.Data) && File.Exists(blobPathResult.Data))
                {
                    File.Copy(blobPathResult.Data, destinationPath, true);
                }
                else
                {
                    return OperationResult<bool>.CreateFailure($"Failed to locate local CAS content for: {file.RelativePath}");
                }
            }

            return await RegisterExtractedManifestAsync(validatedManifestId, dependency, stagingDir, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to materialize manifest {ManifestId} from existing CAS objects.", validatedManifestId);
            return OperationResult<bool>.CreateSuccess(false);
        }
        finally
        {
            CleanupStagingDirectories(string.Empty, stagingDir, stagingBase);
        }
    }

    private async Task<OperationResult<bool>> DownloadAndRegisterZipPackageAsync(
        SharedManifestDependency dependency,
        ManifestId validatedManifestId,
        string packageUrl,
        CancellationToken cancellationToken)
    {
        var stagingBase = Path.Combine(Path.GetTempPath(), AppConstants.AppName, "SharedImportStaging");
        var stagingDir = Path.Combine(stagingBase, Guid.NewGuid().ToString("N"));
        var tempZipPath = Path.Combine(stagingBase, $"{Guid.NewGuid():N}.zip");

        try
        {
            Directory.CreateDirectory(stagingBase);
            Directory.CreateDirectory(stagingDir);

            var downloadResult = await DownloadPackageArchiveAsync(packageUrl, tempZipPath, dependency.DisplayName, cancellationToken);
            if (!downloadResult.Success)
            {
                return downloadResult;
            }

            var extractionResult = await ExtractAndVerifyPackageFilesAsync(tempZipPath, stagingDir, dependency, cancellationToken);
            if (!extractionResult.Success)
            {
                return extractionResult;
            }

            return await RegisterExtractedManifestAsync(validatedManifestId, dependency, stagingDir, cancellationToken);
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<ProfileSharingService>.Instance).LogError(ex, "Failed to download and register cloud package for {ManifestId}", dependency.ManifestId);
            return OperationResult<bool>.CreateFailure($"Failed to download cloud package: {ex.Message}");
        }
        finally
        {
            CleanupStagingDirectories(tempZipPath, stagingDir, stagingBase);
        }
    }

    private async Task<OperationResult<bool>> DownloadPackageArchiveAsync(
        string packageUrl,
        string tempZipPath,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri) || !await IsSafeRemoteUriAsync(uri, cancellationToken))
        {
            return OperationResult<bool>.CreateFailure($"Unsafe package download URL blocked: {packageUrl}");
        }

        using var response = await SendWithManualRedirectsAsync(safeHttpClient, uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
        {
            return OperationResult<bool>.CreateFailure(
                $"The cloud package for '{displayName}' has expired or is no longer available. Please request an updated share link from the author.");
        }

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(tempZipPath);

        byte[] buffer = new byte[16384];
        int bytesRead = 0;
        long totalDownloaded = 0;
        long maxAllowedBytes = ProfileSharingConstants.MaxDownloadedFileBytes;

        while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalDownloaded += bytesRead;

            if (totalDownloaded > maxAllowedBytes)
            {
                return OperationResult<bool>.CreateFailure($"Package download for {displayName} exceeded maximum size limit ({maxAllowedBytes} bytes).");
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<OperationResult<bool>> RegisterExtractedManifestAsync(
        ManifestId validatedManifestId,
        SharedManifestDependency dependency,
        string stagingDir,
        CancellationToken cancellationToken)
    {
        var contentManifest = new ContentManifest
        {
            Id = validatedManifestId,
            Name = dependency.DisplayName,
            Version = dependency.Version,
            ContentType = dependency.ContentType,
            TargetGame = dependency.TargetGame,
            Publisher = new PublisherInfo
            {
                Name = dependency.Publisher ?? ProfileSharingConstants.DefaultLocalPublisherName,
                PublisherType = dependency.PublisherType ?? PublisherTypeConstants.Local,
            },
            Files = dependency.Files.Select(f => new ManifestFile
            {
                RelativePath = f.RelativePath,
                Hash = f.Hash,
                Size = f.Size,
                SourceType = f.SourceType != ContentSourceType.Unknown ? f.SourceType : ContentSourceType.ContentAddressable,
                InstallTarget = f.InstallTarget,
                IsRequired = f.IsRequired,
                IsExecutable = f.IsExecutable,
                Permissions = f.Permissions,
                DownloadUrl = f.DownloadUrl,
                SourcePath = f.SourcePath,
                PatchSourceFile = f.PatchSourceFile,
                PackageInfo = f.PackageInfo,
            }).ToList(),
        };

        var addResult = await manifestPool.AddManifestAsync(contentManifest, stagingDir, cancellationToken: cancellationToken);
        if (!addResult.Success)
        {
            (logger ?? NullLogger<ProfileSharingService>.Instance).LogError(
                "Failed to register extracted manifest {ManifestId} into pool: {Error}",
                validatedManifestId,
                addResult.FirstError);
            return OperationResult<bool>.CreateFailure($"Failed to register extracted manifest '{validatedManifestId}': {addResult.FirstError}");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private void CleanupStagingFile(string tempZipPath)
    {
        try
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<ProfileSharingService>.Instance).LogWarning(ex, "Failed to delete temp zip {Path}", tempZipPath);
        }
    }

    private void CleanupStagingDirectories(string tempZipPath, string stagingDir, string stagingBase)
    {
        try
        {
            if (File.Exists(tempZipPath))
            {
                File.Delete(tempZipPath);
            }

            if (Directory.Exists(stagingDir) && Path.GetFullPath(stagingDir).StartsWith(Path.GetFullPath(stagingBase), StringComparison.Ordinal))
            {
                Directory.Delete(stagingDir, true);
            }
        }
        catch (Exception ex)
        {
            (logger ?? NullLogger<ProfileSharingService>.Instance).LogWarning(ex, "Failed to clean up staging directory {StagingDir}", stagingDir);
        }
    }

    private async Task<OperationResult<bool>> DownloadAndRegisterManifestFilesAsync(
        SharedManifestDependency dependency,
        ManifestId validatedManifestId,
        CancellationToken cancellationToken)
    {
        var stagingBase = Path.Combine(Path.GetTempPath(), AppConstants.AppName, "SharedImportStaging");
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
                TargetGame = dependency.TargetGame,
                Publisher = new PublisherInfo
                {
                    Name = dependency.Publisher ?? ProfileSharingConstants.DefaultCommunityPublisherName,
                    PublisherType = dependency.PublisherType ?? PublisherTypeConstants.Unknown,
                },
                Files = dependency.Files.Select(f => new ManifestFile
                {
                    RelativePath = f.RelativePath,
                    Hash = f.Hash,
                    Size = f.Size,
                    SourceType = f.SourceType != ContentSourceType.Unknown ? f.SourceType : ContentSourceType.ContentAddressable,
                    InstallTarget = f.InstallTarget,
                    IsRequired = f.IsRequired,
                    IsExecutable = f.IsExecutable,
                    Permissions = f.Permissions,
                    DownloadUrl = f.DownloadUrl,
                    SourcePath = f.SourcePath,
                    PatchSourceFile = f.PatchSourceFile,
                    PackageInfo = f.PackageInfo,
                }).ToList(),
            };

            var canonicalStagingPrefix = Path.GetFullPath(stagingDir) + Path.DirectorySeparatorChar;

            foreach (var file in dependency.Files)
            {
                var downloadResult = await DownloadAndVerifyFileAsync(file, stagingDir, canonicalStagingPrefix, cancellationToken);
                if (!downloadResult.Success)
                {
                    return downloadResult;
                }
            }

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
                logger?.LogWarning(ex, "Failed to clean up staging directory {StagingDir}", stagingDir);
            }
        }
    }

    private async Task<OperationResult<bool>> DownloadAndVerifyFileAsync(
        ManifestFile file,
        string stagingDir,
        string canonicalStagingPrefix,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file.DownloadUrl))
        {
            return OperationResult<bool>.CreateFailure($"Missing download URL for manifest file: {file.RelativePath}");
        }

        if (string.IsNullOrWhiteSpace(file.Hash))
        {
            return OperationResult<bool>.CreateFailure($"Manifest file {file.RelativePath} is missing required cryptographic hash.");
        }

        var normalizedRelativePath = file.RelativePath.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedRelativePath) ||
            Path.IsPathRooted(normalizedRelativePath) ||
            normalizedRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains(ProfileSharingConstants.ParentDirectorySegment))
        {
            return OperationResult<bool>.CreateFailure($"Invalid relative path in manifest: {file.RelativePath}");
        }

        var destination = Path.GetFullPath(Path.Combine(stagingDir, normalizedRelativePath));
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

        try
        {
            using var response = await SendWithManualRedirectsAsync(safeHttpClient, fileDownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var sha256 = SHA256.Create();
            using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = File.Create(destination);

            byte[] buffer = new byte[16384];
            int bytesRead = 0;
            long totalDownloaded = 0;
            long maxAllowedBytes = file.Size > 0
                ? Math.Min(file.Size + (1024 * 1024), ProfileSharingConstants.MaxDownloadedFileBytes)
                : ProfileSharingConstants.MaxDownloadedFileBytes;

            while ((bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalDownloaded += bytesRead;

                if (totalDownloaded > maxAllowedBytes)
                {
                    return OperationResult<bool>.CreateFailure($"Download size for {file.RelativePath} exceeded expected size limit ({maxAllowedBytes} bytes).");
                }
            }

            sha256.TransformFinalBlock([], 0, 0);
            var computedHash = Convert.ToHexString(sha256.Hash ?? []).ToLowerInvariant();

            if (!string.Equals(computedHash, file.Hash.Replace("-", string.Empty).ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<bool>.CreateFailure($"SHA-256 hash mismatch for {file.RelativePath}.");
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning(ex, "Failed to download file {RelativePath} from {Url}", file.RelativePath, file.DownloadUrl);
            return OperationResult<bool>.CreateFailure($"Failed to download {file.RelativePath}: {ex.Message}");
        }
    }

    private async Task<OperationResult<bool>> SearchAndAcquireFallbackManifestAsync(
        SharedManifestDependency dependency,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
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
            var match = searchResult.Data.FirstOrDefault(r =>
                r.Id.Equals(dependency.ManifestId, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                match = searchResult.Data.FirstOrDefault(r =>
                    r.Name.Equals(dependency.DisplayName, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    logger?.LogWarning(
                        "Fallback acquisition matched dependency '{DisplayName}' by display name rather than Manifest ID '{ManifestId}'.",
                        dependency.DisplayName,
                        dependency.ManifestId);
                }
            }

            if (match != null)
            {
                var acquireRes = await contentOrchestrator.AcquireContentAsync(match, progress, cancellationToken);
                if (acquireRes.Success)
                {
                    return OperationResult<bool>.CreateSuccess(true);
                }
            }
        }

        logger?.LogWarning(
            "Dependency '{DisplayName}' ({ManifestId}) could not be acquired from any connected content source.",
            dependency.DisplayName,
            dependency.ManifestId);
        return OperationResult<bool>.CreateFailure(
            $"Dependency '{dependency.DisplayName}' ({dependency.ManifestId}) was not found in the local cache or any connected content source.");
    }

    private OperationResult<SharedGameProfilePackage> DeserializeSharedPackage(string json)
    {
        SharedGameProfilePackage? package;
        try
        {
            package = JsonSerializer.Deserialize<SharedGameProfilePackage>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize shared profile package JSON.");
            return OperationResult<SharedGameProfilePackage>.CreateFailure($"Invalid shared profile package format: {ex.Message}");
        }

        if (package?.Profile == null || package.RequiredManifests == null)
        {
            return OperationResult<SharedGameProfilePackage>.CreateFailure("Package does not contain valid profile metadata or manifests list.");
        }

        if (package.SchemaVersion != ProfileSharingConstants.DefaultSchemaVersion)
        {
            return OperationResult<SharedGameProfilePackage>.CreateFailure($"Unsupported package schema version {package.SchemaVersion}. Expected version {ProfileSharingConstants.DefaultSchemaVersion}.");
        }

        return OperationResult<SharedGameProfilePackage>.CreateSuccess(package);
    }

    private async Task<OperationResult<ManifestInspectionSummary>> DiffManifestsAgainstPoolAsync(
        SharedGameProfilePackage package,
        CancellationToken cancellationToken)
    {
        var inspectedManifests = new List<SharedManifestDependency>();
        long totalMissingDownloadBytes = 0;
        int cachedCount = 0;
        int missingCount = 0;

        foreach (var reqManifest in package.RequiredManifests)
        {
            if (reqManifest.ContentType == ContentType.GameInstallation ||
                reqManifest.ManifestId.Contains(ManifestConstants.GameInstallationSegment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ManifestId.TryCreate(reqManifest.ManifestId, out _))
            {
                return OperationResult<ManifestInspectionSummary>.CreateFailure($"Invalid manifest identifier '{reqManifest.ManifestId}'. Manifest IDs must follow the 5-segment schema format.");
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
                DownloadSize = reqManifest.DownloadSize,
                IsCachedLocally = isCached,
                Hash = reqManifest.Hash,
                PackageUrl = reqManifest.PackageUrl,
                PackageHash = reqManifest.PackageHash,
                Files = reqManifest.Files,
            });
        }

        return OperationResult<ManifestInspectionSummary>.CreateSuccess(
            new ManifestInspectionSummary(inspectedManifests, cachedCount, missingCount, totalMissingDownloadBytes));
    }

    private async Task<(List<GameInstallation> Compatible, string? MatchedId)> FindCompatibleInstallationsAsync(
        GameType gameType,
        CancellationToken cancellationToken)
    {
        var compatibleInstallations = new List<GameInstallation>();
        string? matchedInstallationId = null;

        var installationsResult = await installationService.GetAllInstallationsAsync(cancellationToken);
        if (installationsResult is { Success: true, Data: not null })
        {
            foreach (var inst in installationsResult.Data)
            {
                bool isCompatible = gameType switch
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

        return (compatibleInstallations, matchedInstallationId);
    }

    private async Task<(string SuggestedName, bool HasConflict)> DetermineSuggestedProfileNameAsync(
        string profileName,
        CancellationToken cancellationToken)
    {
        string suggestedName = profileName;
        bool hasNameConflict = false;

        var allProfilesResult = await profileRepository.LoadAllProfilesAsync(cancellationToken);
        if (allProfilesResult is { Success: true, Data: not null })
        {
            var existingNames = allProfilesResult.Data.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (existingNames.Contains(profileName))
            {
                hasNameConflict = true;
                int counter = 1;
                suggestedName = $"{profileName} ({counter})";
                while (existingNames.Contains(suggestedName))
                {
                    suggestedName = $"{profileName} ({++counter})";
                }
            }
        }

        return (suggestedName, hasNameConflict);
    }
}
