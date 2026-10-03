using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.Providers.VersionSchemes;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Post-extraction factory for Generals Online content manifests.
/// Computes file hashes, updates manifest entries, and creates variant manifests (60Hz, Test Environment, MapPack, GameData)
/// from the extracted archive content.
/// </summary>
public class GeneralsOnlineManifestFactory(
    ILogger<GeneralsOnlineManifestFactory> logger,
    IProviderDefinitionLoader providerLoader) : IPublisherManifestFactory
{
    /// <summary>
    /// File info extracted from archive for manifest generation.
    /// </summary>
    /// <param name="RelativePath">The relative path within archive.</param>
    /// <param name="FileInfo">The file info.</param>
    /// <param name="Hash">The SHA-256 hash.</param>
    /// <param name="IsMap">Whether this is a map file.</param>
    /// <param name="IsGameData">Whether this is a game data file.</param>
    private readonly record struct ExtractedFileInfo(
        string RelativePath,
        FileInfo FileInfo,
        string Hash,
        bool IsMap,
        bool IsGameData);

    private static readonly MmddyyQfeVersionScheme GeneralsOnlineVersionScheme = new();

    /// <inheritdoc />
    public string PublisherId => PublisherTypeConstants.GeneralsOnline;

    /// <summary>
    /// Creates a content manifest for a specific Generals Online variant.
    /// </summary>
    /// <param name="release">The Generals Online release information.</param>
    /// <param name="variantSuffix">The suffix for the manifest ID (e.g., "60hz", "test").</param>
    /// <param name="displayName">The display name for this variant (e.g., "GeneralsOnline 60Hz").</param>
    /// <returns>A content manifest for the specified variant.</returns>
    public ContentManifest CreateVariantManifest(
        GeneralsOnlineRelease release,
        string variantSuffix,
        string displayName)
    {
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? GeneralsOnlineConstants.WebsiteUrl;
        var supportUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.SupportUrl) ?? GeneralsOnlineConstants.SupportUrl;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.DownloadPageUrl) ?? GeneralsOnlineConstants.DownloadPageUrl;
        var configuredIcon = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.IconUrl);
        var iconUrl = !string.IsNullOrWhiteSpace(configuredIcon) ? configuredIcon : GeneralsOnlineConstants.LogoSource;
        var configuredCover = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.CoverUrl);
        var coverSource = !string.IsNullOrWhiteSpace(configuredCover) ? configuredCover : GeneralsOnlineConstants.CoverSource;
        var tags = provider?.DefaultTags ?? [.. GeneralsOnlineConstants.Tags];

        // Parse version to extract numeric version (remove dots and QFE markers)
        var userVersion = ParseVersionForManifestId(release.Version);

        // Content name for GeneralsOnline (publisher is "generalsonline", content is the variant)
        // This will create IDs like: 1.1015255.generalsonline.gameclient.60hz
        var contentName = variantSuffix;

        var manifestId = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.GeneralsOnline,
            ContentType.GameClient,
            contentName,
            userVersion));

        var isTestEnv = variantSuffix.Equals(GeneralsOnlineConstants.VariantTestEnvironmentSuffix, StringComparison.OrdinalIgnoreCase);

        var description = isTestEnv
            ? GeneralsOnlineConstants.TestEnvironmentDescription
            : (provider?.Description ?? GeneralsOnlineConstants.ShortDescription);

        var dependencies = isTestEnv
            ? GeneralsOnlineDependencyBuilder.GetDependenciesForTestEnvironment(userVersion)
            : GeneralsOnlineDependencyBuilder.GetDependenciesFor60Hz(userVersion);

        var postInstallSteps = isTestEnv
            ? new List<InstallationStep>()
            : new List<InstallationStep> { CreateEacInstallStep() };

        return new ContentManifest
        {
            Id = manifestId,
            Name = displayName,
            Version = release.Version,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = PublisherTypeConstants.GeneralsOnline,
            OriginalContentId = $"{GeneralsOnlineConstants.ContentIdPrefix}{release.Version}",
            Publisher = new PublisherInfo
            {
                Name = GeneralsOnlineConstants.PublisherName,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Website = websiteUrl,
                SupportUrl = supportUrl,
                ContentIndexUrl = downloadPageUrl,
                UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
            },
            Metadata = CreateContentMetadata(
                description,
                release.ReleaseDate,
                iconUrl,
                [.. tags, .. GetVariantTags(variantSuffix)],
                release.Changelog,
                release.Version,
                coverSource),
            Files =
            [
                new ManifestFile
                {
                    RelativePath = Path.GetFileName(release.PortableUrl),
                    DownloadUrl = release.PortableUrl,
                    Size = release.PortableSize ?? 0, // Use 0 when size is unknown
                    SourceType = ContentSourceType.RemoteDownload,
                    Hash = release.Sha256 ?? string.Empty,
                },
            ],
            Dependencies = dependencies,
            InstallationInstructions = new InstallationInstructions
            {
                WorkspaceStrategy = WorkspaceConstants.DefaultWorkspaceStrategy,
                DownloadHash = release.Sha256,
                PostInstallSteps = postInstallSteps,
            },
        };
    }

    /// <summary>
    /// Creates content manifests from a GeneralsOnline release:
    /// - 60Hz game client variant
    /// - Test Environment game client variant
    /// - QuickMatch MapPack (required for multiplayer)
    /// - GeneralsOnlineGameData data patch (optional patch, auto-installed for game client)
    /// This creates the initial manifests with download URLs.
    /// </summary>
    /// <param name="release">The GeneralsOnlineRelease to create the manifests from.</param>
    /// <returns>A list containing the ContentManifest instances.</returns>
    public List<ContentManifest> CreateManifests(GeneralsOnlineRelease release)
    {
        List<ContentManifest> manifests = [];

        // Create manifest for 60Hz variant
        manifests.Add(CreateVariantManifest(release, GeneralsOnlineConstants.Variant60HzSuffix, GameClientConstants.GeneralsOnline60HzDisplayName));

        // Create manifest for Test Environment variant
        manifests.Add(CreateVariantManifest(release, GeneralsOnlineConstants.VariantTestEnvironmentSuffix, GameClientConstants.GeneralsOnlineTestEnvironmentDisplayName));

        // Create manifest for QuickMatch MapPack (required dependency for game client)
        manifests.Add(CreateQuickMatchMapPackManifest(release));

        // Create manifest for GeneralsOnlineGameData data patch (optional patch, auto-installed for game client)
        manifests.Add(CreateGameDataPatchManifest(release));

        return manifests;
    }

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        var publisherMatches = string.Equals(manifest.Publisher?.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase);
        var isGameClient = manifest.ContentType == ContentType.GameClient;
        var isMapPack = manifest.ContentType == ContentType.MapPack;
        var isPatch = manifest.ContentType == ContentType.Patch;
        return publisherMatches && (isGameClient || isMapPack || isPatch);
    }

    /// <inheritdoc />
    public async Task<OperationResult<List<ContentManifest>>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating GeneralsOnline manifests from extracted content in: {Directory}", extractedDirectory);

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Step 1: Create variant manifests with basic info from original manifest
            var variants = CreateVariantManifestsFromOriginal(originalManifest);

            // Step 2: Scan extracted files and update manifests with actual files and hashes
            var updateResult = await UpdateManifestsWithExtractedFiles(variants, extractedDirectory, cancellationToken);
            if (!updateResult.Success)
            {
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    updateResult.FirstError ?? "Failed to update manifests with extracted files");
            }

            return OperationResult<List<ContentManifest>>.CreateSuccess(updateResult.Data ?? []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create GeneralsOnline manifests from extracted content");
            return OperationResult<List<ContentManifest>>.CreateFailure(
                $"Failed to create manifests: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string GetManifestDirectory(
        ContentManifest manifest,
        string extractedDirectory)
    {
        // GeneralsOnline uses the root extracted directory for all variants
        return extractedDirectory;
    }

    /// <summary>
    /// Creates manifests from an existing local installation without downloading.
    /// This is used when importing manually.
    /// </summary>
    /// <param name="installationPath">The path to the local installation directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of created content manifests.</returns>
    public async Task<List<ContentManifest>> CreateManifestsFromLocalInstallAsync(
        string installationPath,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating GeneralsOnline manifests from local install at: {Path}", installationPath);

        // Verify a supported entry point exists. The bootstrapper alone suffices: it
        // names its target in EasyAntiCheat/Settings.json, so the wrapped binary needs
        // no fixed file name. Lookups are case-insensitive for case-sensitive volumes.
        var has60Hz = Path.Combine(installationPath, GameClientConstants.GeneralsOnline60HzExecutable)
            .TryGetFileCaseInsensitive(out _);
        var hasBootstrapper = Path.Combine(installationPath, GameClientConstants.GeneralsOnlineEacLauncherExecutable)
            .TryGetFileCaseInsensitive(out _);

        if (!has60Hz && !hasBootstrapper)
        {
            logger.LogWarning("No GeneralsOnline executables found in {Path}", installationPath);
            return [];
        }

        // Create a synthetic release object
        var release = new GeneralsOnlineRelease
        {
            Version = GameClientConstants.AutoDetectedVersion,
            VersionDate = DateTime.UtcNow,
            ReleaseDate = DateTime.UtcNow,
            PortableUrl = string.Empty,
            PortableSize = 0,
            Changelog = string.Empty,
        };

        // Create the base manifests
        var manifests = CreateManifests(release);

        // Update with file hashes from the installation
        var updateResult = await UpdateManifestsWithExtractedFiles(manifests, installationPath, cancellationToken);
        if (!updateResult.Success)
        {
            logger.LogWarning(
                "Failed to create GeneralsOnline manifests from local install at {Path}: {Error}",
                installationPath,
                updateResult.FirstError);
            return [];
        }

        return updateResult.Data ?? [];
    }

    /// <summary>
    /// Creates the Easy Anti-Cheat install step. The product ID binds to the
    /// package's own bootstrapper settings at execution time, so publisher-side
    /// product rotations install correctly without a GenHub change. No explicit
    /// step key: it derives from the resolved arguments.
    /// </summary>
    /// <returns>The EAC install step.</returns>
    private static InstallationStep CreateEacInstallStep() => CreateEacInstallStep(string.Empty);

    /// <summary>
    /// Creates the Easy Anti-Cheat install step. For packages whose EAC settings
    /// point to a secondary binary (e.g. test environment), the production
    /// product ID is used for live 60Hz gameplay.
    /// </summary>
    /// <param name="extractPath">The extracted package directory.</param>
    /// <returns>The EAC install step.</returns>
    private static InstallationStep CreateEacInstallStep(string extractPath)
    {
        var hasLive60HzBinary = !string.IsNullOrEmpty(extractPath) &&
            File.Exists(Path.Combine(extractPath, GameClientConstants.GeneralsOnline60HzExecutable));

        var isTestEnvironment = GeneralsOnlineEacSettings.TryRead(extractPath, out var settings)
            && settings is not null
            && !string.Equals(
                GeneralsOnlineEacSettings.NormalizeExecutableName(settings.Executable),
                GameClientConstants.GeneralsOnline60HzExecutable,
                StringComparison.OrdinalIgnoreCase)
            && hasLive60HzBinary;

        if (isTestEnvironment)
        {
            return new InstallationStep
            {
                Name = GeneralsOnlineConstants.EacStepName,
                Kind = InstallationStepKind.RunVerifiedInstaller,
                TargetRelativePath = GameClientConstants.GeneralsOnlineEacSetupExecutable,
                Arguments = [GeneralsOnlineConstants.EacInstallCommand, GeneralsOnlineConstants.EacProductId],
                RequiresElevation = true,
                StatusMessage = GeneralsOnlineConstants.EacStatusMessage,
                RunOnce = true,
            };
        }

        return new InstallationStep
        {
            Name = GeneralsOnlineConstants.EacStepName,
            Kind = InstallationStepKind.RunVerifiedInstaller,
            TargetRelativePath = GameClientConstants.GeneralsOnlineEacSetupExecutable,
            Arguments = [GeneralsOnlineConstants.EacInstallCommand, string.Empty],
            ArgumentBindings =
            [
                new InstallationArgumentBinding
                {
                    ArgumentIndex = 1,
                    Source = ManifestConstants.InstallationBindingJsonSource,
                    RelativePath = GeneralsOnlineConstants.EacSettingsRelativePath,
                    Key = GeneralsOnlineConstants.EacSettingsProductIdKey,
                },
            ],
            RequiresElevation = true,
            StatusMessage = GeneralsOnlineConstants.EacStatusMessage,
            RunOnce = true,
        };
    }

    private static int ParseVersionForManifestId(string version)
    {
        var scheme = GeneralsOnlineVersionScheme;
        if (scheme.TryParse(version, out var parsed) && parsed.Components.Count > 3 && parsed.Components[3] > 9)
        {
            throw new ArgumentException(
                $"Generals Online version '{version}' has a QFE value ({parsed.Components[3]}) exceeding 9, which cannot be encoded into a 7-digit legacy manifest ID without year collision.",
                nameof(version));
        }

        return GameVersionHelper.GetGeneralsOnlineManifestIdComponent(version);
    }

    /// <summary>
    /// Determines whether a manifest-relative path is the named file at the archive root.
    /// Nested files that merely share the name are not the published entry point.
    /// </summary>
    private static bool IsArchiveRootFile(string relativePath, string fileName) =>
        string.Equals(
            relativePath.Replace('\\', '/').TrimStart('/'),
            fileName,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether a manifest-relative path belongs to Easy Anti-Cheat binaries, services, or plugin directories.
    /// </summary>
    private static bool IsEasyAntiCheatFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (IsArchiveRootFile(relativePath, GameClientConstants.GeneralsOnlineEacLauncherExecutable) ||
            IsArchiveRootFile(relativePath, GameClientConstants.GeneralsOnlineEacSetupExecutable) ||
            IsArchiveRootFile(relativePath, GameClientConstants.GeneralsOnlineEosSdkDll))
        {
            return true;
        }

        return normalized.StartsWith("EasyAntiCheat/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("plugins/easyanticheat/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("plugins/goanticheat/", StringComparison.OrdinalIgnoreCase);
    }

    private static ManifestFile CreateMapManifestFile(string relativePath, FileInfo fileInfo, string hash)
    {
        // For maps, the relative path should be relative to the Maps directory
        // e.g., "Maps/SomeMap/SomeMap.map" -> "SomeMap/SomeMap.map"
        var mapRelativePath = relativePath;
        if (relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + "/", StringComparison.OrdinalIgnoreCase))
        {
            mapRelativePath = relativePath[(GeneralsOnlineConstants.MapsSubdirectory.Length + 1)..];
        }

        return new ManifestFile
        {
            RelativePath = mapRelativePath,
            Size = fileInfo.Length,
            Hash = hash,
            SourceType = ContentSourceType.ContentAddressable,
            SourcePath = fileInfo.FullName,
            InstallTarget = ContentInstallTarget.UserMapsDirectory,
            IsExecutable = false,
        };
    }

    private static ManifestFile CreateGameDataManifestFile(string relativePath, FileInfo fileInfo, string hash)
    {
        return new ManifestFile
        {
            RelativePath = relativePath,
            Size = fileInfo.Length,
            Hash = hash,
            SourceType = ContentSourceType.ContentAddressable,
            SourcePath = fileInfo.FullName,
            InstallTarget = ContentInstallTarget.UserDataDirectory,
            IsExecutable = false,
        };
    }

    /// <summary>
    /// Gets variant-specific tags for a given variant suffix.
    /// </summary>
    /// <param name="variantSuffix">The variant suffix (e.g., "60hz", "test").</param>
    /// <returns>A list of variant-specific tags.</returns>
    private static List<string> GetVariantTags(string variantSuffix)
    {
        if (variantSuffix.Equals(GeneralsOnlineConstants.Variant60HzSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return [GeneralsOnlineVariantTags.Tag60Hz];
        }

        if (variantSuffix.Equals(GeneralsOnlineConstants.VariantTestEnvironmentSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return [GeneralsOnlineVariantTags.TagTestEnvironment];
        }

        if (variantSuffix.Equals(GeneralsOnlineConstants.QuickMatchMapPackSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return [GeneralsOnlineVariantTags.TagQuickMatchMaps];
        }

        if (variantSuffix.Equals(GeneralsOnlineConstants.GameDataPatchSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return [GeneralsOnlineVariantTags.TagGameData];
        }

        return [];
    }

    private static bool IsTestEnvironmentManifest(ContentManifest manifest)
    {
        if (manifest.ContentType != ContentType.GameClient)
        {
            return false;
        }

        if (manifest.Id.Value.EndsWith($".{GeneralsOnlineConstants.VariantTestEnvironmentSuffix}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return manifest.Metadata?.Tags is not null && manifest.Metadata.Tags.Any(t => t.Equals(GeneralsOnlineVariantTags.TagTestEnvironment, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ComputeFileHashAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Creates a content metadata instance with unified variant grouping and artwork applied.
    /// </summary>
    private static ContentMetadata CreateContentMetadata(
        string? description,
        DateTime releaseDate,
        string? iconUrl,
        IReadOnlyList<string> tags,
        string? changelogUrl,
        string? version,
        string? coverSource = null)
    {
        return new ContentMetadata
        {
            Description = description ?? string.Empty,
            ReleaseDate = releaseDate,
            IconUrl = iconUrl,
            ThemeColor = GeneralsOnlineConstants.ThemeColor,
            Tags = [.. tags],
            ChangelogUrl = changelogUrl,
            VariantGroupId = GeneralsOnlineVariantGrouping.BuildVariantGroupId(version),
            VariantFamilyName = GeneralsOnlineVariantGrouping.BuildVariantFamilyName(version),
            CoverUrl = !string.IsNullOrWhiteSpace(coverSource) ? coverSource : GeneralsOnlineConstants.CoverSource,
        };
    }

    /// <summary>
    /// Creates a content manifest for the GeneralsOnlineGameData data patch.
    /// This manifest contains game data files (community balance patch and core INI configuration).
    /// </summary>
    /// <param name="release">The Generals Online release information.</param>
    /// <returns>A content manifest for the GeneralsOnlineGameData data patch.</returns>
    private ContentManifest CreateGameDataPatchManifest(GeneralsOnlineRelease release)
    {
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? GeneralsOnlineConstants.WebsiteUrl;
        var supportUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.SupportUrl) ?? GeneralsOnlineConstants.SupportUrl;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.DownloadPageUrl) ?? GeneralsOnlineConstants.DownloadPageUrl;
        var configuredIcon = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.IconUrl);
        var iconUrl = !string.IsNullOrWhiteSpace(configuredIcon) ? configuredIcon : GeneralsOnlineConstants.LogoSource;
        var configuredCover = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.CoverUrl);
        var coverSource = !string.IsNullOrWhiteSpace(configuredCover) ? configuredCover : GeneralsOnlineConstants.CoverSource;
        var userVersion = ParseVersionForManifestId(release.Version);
        var manifestId = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.GeneralsOnline,
            ContentType.Patch,
            GeneralsOnlineConstants.GameDataPatchSuffix,
            userVersion));

        return new ContentManifest
        {
            Id = manifestId,
            Name = GeneralsOnlineConstants.GameDataDisplayName,
            Version = release.Version,
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = PublisherTypeConstants.GeneralsOnline,
            OriginalContentId = $"{GeneralsOnlineConstants.ContentIdPrefix}{release.Version}_Patch",
            Publisher = new PublisherInfo
            {
                Name = GeneralsOnlineConstants.PublisherName,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Website = websiteUrl,
                SupportUrl = supportUrl,
                ContentIndexUrl = downloadPageUrl,
                UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
            },
            Metadata = new ContentMetadata
            {
                Description = GeneralsOnlineConstants.GameDataDescription,
                ReleaseDate = release.ReleaseDate,
                IconUrl = iconUrl,
                CoverUrl = coverSource,
                ThemeColor = GeneralsOnlineConstants.ThemeColor,
                Tags = [.. GeneralsOnlineConstants.GameDataTags, .. GetVariantTags(GeneralsOnlineConstants.GameDataPatchSuffix)],
                ChangelogUrl = release.Changelog,
                VariantGroupId = GeneralsOnlineVariantGrouping.BuildVariantGroupId(release.Version),
                VariantFamilyName = GeneralsOnlineVariantGrouping.BuildVariantFamilyName(release.Version),
            },

            // Files will be populated during extraction
            Files = [],
            Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesForGameData(userVersion),
            InstallationInstructions = new InstallationInstructions(),
        };
    }

    /// <summary>
    /// Creates a content manifest for the QuickMatch MapPack.
    /// This manifest contains all maps required for GeneralsOnline QuickMatch multiplayer.
    /// </summary>
    /// <param name="release">The Generals Online release information.</param>
    /// <returns>A content manifest for the QuickMatch MapPack.</returns>
    private ContentManifest CreateQuickMatchMapPackManifest(GeneralsOnlineRelease release)
    {
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? GeneralsOnlineConstants.WebsiteUrl;
        var supportUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.SupportUrl) ?? GeneralsOnlineConstants.SupportUrl;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.DownloadPageUrl) ?? GeneralsOnlineConstants.DownloadPageUrl;
        var iconUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.IconUrl) ?? GeneralsOnlineConstants.LogoSource;
        var coverSource = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.CoverUrl) ?? GeneralsOnlineConstants.CoverSource;
        var userVersion = ParseVersionForManifestId(release.Version);
        var manifestId = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.GeneralsOnline,
            ContentType.MapPack,
            GeneralsOnlineConstants.QuickMatchMapPackSuffix,
            userVersion));

        return new ContentManifest
        {
            Id = manifestId,
            Name = GeneralsOnlineConstants.QuickMatchMapPackDisplayName,
            Version = release.Version,
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = PublisherTypeConstants.GeneralsOnline,
            OriginalContentId = $"{GeneralsOnlineConstants.ContentIdPrefix}{release.Version}_MapPack",
            Publisher = new PublisherInfo
            {
                Name = GeneralsOnlineConstants.PublisherName,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
                Website = websiteUrl,
                SupportUrl = supportUrl,
                ContentIndexUrl = downloadPageUrl,
                UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
            },
            Metadata = CreateContentMetadata(
                GeneralsOnlineConstants.QuickMatchMapPackDescription,
                release.ReleaseDate,
                iconUrl,
                GeneralsOnlineConstants.MapPackTags,
                release.Changelog,
                release.Version,
                coverSource),
            Files = [], // Files will be populated during extraction
            Dependencies =
            [

                // MapPack requires Zero Hour installation
                GeneralsOnlineDependencyBuilder.CreateZeroHourDependencyForGeneralsOnline(),
            ],
            InstallationInstructions = new InstallationInstructions(),
        };
    }

    /// <summary>
    /// Creates variant manifests (60Hz, Test Environment, QuickMatch MapPack, and GeneralsOnlineGameData data patch) from an original manifest.
    /// This is used after downloading and extracting the portable ZIP.
    /// </summary>
    /// <param name="originalManifest">The original manifest.</param>
    /// <returns>List of variant manifests with basic information populated.</returns>
    private List<ContentManifest> CreateVariantManifestsFromOriginal(ContentManifest originalManifest)
    {
        List<ContentManifest> manifests = [];
        var version = originalManifest.Version ?? GeneralsOnlineConstants.UnknownVersion;
        var userVersion = ParseVersionForManifestId(version);

        var originalProviderName = !string.IsNullOrEmpty(originalManifest.OriginalProviderName)
            ? originalManifest.OriginalProviderName
            : PublisherTypeConstants.GeneralsOnline;
        var originalContentId = !string.IsNullOrEmpty(originalManifest.OriginalContentId)
            ? originalManifest.OriginalContentId
            : $"{GeneralsOnlineConstants.ContentIdPrefix}{version}";

        // Get URLs from provider definition (prefer original manifest metadata if available)
        var provider = providerLoader.GetProvider(PublisherTypeConstants.GeneralsOnline);
        var websiteUrl = provider?.Endpoints.WebsiteUrl ?? GeneralsOnlineConstants.WebsiteUrl;
        var supportUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.SupportUrl) ?? GeneralsOnlineConstants.SupportUrl;
        var downloadPageUrl = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.DownloadPageUrl) ?? GeneralsOnlineConstants.DownloadPageUrl;
        var configuredIcon = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.IconUrl);
        var iconUrl = !string.IsNullOrWhiteSpace(configuredIcon) ? configuredIcon : GeneralsOnlineConstants.LogoSource;
        var configuredCover = provider?.Endpoints.GetEndpoint(ProviderEndpointConstants.CoverUrl);
        var coverSource = !string.IsNullOrWhiteSpace(configuredCover) ? configuredCover : GeneralsOnlineConstants.CoverSource;

        // Create publisher info once (shared by all variants)
        var publisherInfo = new PublisherInfo
        {
            Name = GeneralsOnlineConstants.PublisherName,
            PublisherType = PublisherTypeConstants.GeneralsOnline,
            Website = websiteUrl,
            SupportUrl = supportUrl,
            ContentIndexUrl = downloadPageUrl,
            UpdateCheckIntervalHours = GeneralsOnlineConstants.UpdateCheckIntervalHours,
        };

        // Create metadata template
        var releaseDate = originalManifest.Metadata?.ReleaseDate ?? DateTime.UtcNow;
        var changelogUrl = originalManifest.Metadata?.ChangelogUrl;

        // Create 60Hz variant
        manifests.Add(new ContentManifest
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.GameClient,
                GeneralsOnlineConstants.Variant60HzSuffix,
                userVersion)),
            Name = GameClientConstants.GeneralsOnline60HzDisplayName,
            Version = version,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = originalProviderName,
            OriginalContentId = originalContentId,
            Publisher = publisherInfo,
            Metadata = CreateContentMetadata(
                GeneralsOnlineConstants.ShortDescription,
                releaseDate,
                iconUrl,
                [.. GeneralsOnlineConstants.Tags, .. GetVariantTags(GeneralsOnlineConstants.Variant60HzSuffix)],
                changelogUrl,
                version,
                coverSource),
            Files = [],
            Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesFor60Hz(userVersion),
            InstallationInstructions = originalManifest.InstallationInstructions ?? new InstallationInstructions
            {
                WorkspaceStrategy = WorkspaceConstants.DefaultWorkspaceStrategy,
            },
        });

        // Create Test Environment variant
        manifests.Add(new ContentManifest
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.GameClient,
                GeneralsOnlineConstants.VariantTestEnvironmentSuffix,
                userVersion)),
            Name = GameClientConstants.GeneralsOnlineTestEnvironmentDisplayName,
            Version = version,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = originalProviderName,
            OriginalContentId = originalContentId,
            Publisher = publisherInfo,
            Metadata = CreateContentMetadata(
                GeneralsOnlineConstants.TestEnvironmentDescription,
                releaseDate,
                iconUrl,
                [.. GeneralsOnlineConstants.Tags, .. GetVariantTags(GeneralsOnlineConstants.VariantTestEnvironmentSuffix)],
                changelogUrl,
                version,
                coverSource),
            Files = [],
            Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesForTestEnvironment(userVersion),
            InstallationInstructions = new InstallationInstructions
            {
                WorkspaceStrategy = WorkspaceConstants.DefaultWorkspaceStrategy,
            },
        });

        // Create QuickMatch MapPack
        manifests.Add(new ContentManifest
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.MapPack,
                GeneralsOnlineConstants.QuickMatchMapPackSuffix,
                userVersion)),
            Name = GeneralsOnlineConstants.QuickMatchMapPackDisplayName,
            Version = version,
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = originalProviderName,
            OriginalContentId = $"{originalContentId}_MapPack",
            Publisher = publisherInfo,
            Metadata = CreateContentMetadata(
                GeneralsOnlineConstants.QuickMatchMapPackDescription,
                releaseDate,
                iconUrl,
                [.. GeneralsOnlineConstants.MapPackTags, .. GetVariantTags(GeneralsOnlineConstants.QuickMatchMapPackSuffix)],
                changelogUrl,
                version,
                coverSource),
            Files = [],
            Dependencies =
            [
                GeneralsOnlineDependencyBuilder.CreateZeroHourDependencyForGeneralsOnline(),
            ],
            InstallationInstructions = new InstallationInstructions(),
        });

        // Create GeneralsOnlineGameData data patch
        manifests.Add(new ContentManifest
        {
            Id = ManifestId.Create(ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.Patch,
                GeneralsOnlineConstants.GameDataPatchSuffix,
                userVersion)),
            Name = GeneralsOnlineConstants.GameDataDisplayName,
            Version = version,
            ContentType = ContentType.Patch,
            TargetGame = GameType.ZeroHour,
            OriginalProviderName = originalProviderName,
            OriginalContentId = $"{originalContentId}_Patch",
            Publisher = publisherInfo,
            Metadata = CreateContentMetadata(
                GeneralsOnlineConstants.GameDataDescription,
                releaseDate,
                iconUrl,
                [.. GeneralsOnlineConstants.GameDataTags, .. GetVariantTags(GeneralsOnlineConstants.GameDataPatchSuffix)],
                changelogUrl,
                version,
                coverSource),
            Files = [],
            Dependencies = GeneralsOnlineDependencyBuilder.GetDependenciesForGameData(userVersion),
            InstallationInstructions = new InstallationInstructions(),
        });

        return manifests;
    }

    private async Task<OperationResult<List<ContentManifest>>> UpdateManifestsWithExtractedFiles(
        List<ContentManifest> manifests,
        string extractPath,
        CancellationToken cancellationToken)
    {
        var filesWithHashes = await ScanExtractedFilesAsync(extractPath, cancellationToken);
        var updatedManifests = new List<ContentManifest>();

        foreach (var manifest in manifests)
        {
            var manifestFiles = BuildManifestFilesForManifest(manifest, filesWithHashes, extractPath);

            if (manifestFiles.Count == 0)
            {
                if (manifest.ContentType is ContentType.MapPack or ContentType.Patch || IsTestEnvironmentManifest(manifest))
                {
                    logger.LogInformation(
                        "Skipping empty {Type} manifest '{Name}' because no matching files were found in extract path",
                        manifest.ContentType,
                        manifest.Name);
                    continue;
                }

                logger.LogError(
                    "Manifest '{Name}' of type {Type} has zero files in extract path '{ExtractPath}'",
                    manifest.Name,
                    manifest.ContentType,
                    extractPath);
                return OperationResult<List<ContentManifest>>.CreateFailure(
                    $"Manifest '{manifest.Name}' of type {manifest.ContentType} has no files in extract path '{extractPath}'.");
            }

            var instructions = BuildInstallationInstructions(manifest, filesWithHashes, extractPath);

            var updated = new ContentManifest(manifest)
            {
                Files = manifestFiles,
                InstallationInstructions = instructions,
            };
            if (updated.ContentType == ContentType.GameClient)
            {
                DeclareGameClientLaunch(updated, extractPath);
            }

            updatedManifests.Add(updated);
        }

        ReconcileMissingExtractedDependencies(updatedManifests);

        return OperationResult<List<ContentManifest>>.CreateSuccess(updatedManifests);
    }

    private async Task<List<ExtractedFileInfo>> ScanExtractedFilesAsync(
        string extractPath,
        CancellationToken cancellationToken)
    {
        var allFiles = Directory.GetFiles(extractPath, "*", SearchOption.AllDirectories);
        logger.LogInformation("Processing {Count} files", allFiles.Length);

        var filesWithHashes = new List<ExtractedFileInfo>(allFiles.Length);

        foreach (var filePath in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(extractPath, filePath);
            var fileInfo = new FileInfo(filePath);

            var isMap = relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        relativePath.StartsWith(GeneralsOnlineConstants.MapsSubdirectory + "/", StringComparison.OrdinalIgnoreCase);

            var isGameData = relativePath.StartsWith(GeneralsOnlineConstants.GameDataSubdirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                             relativePath.StartsWith(GeneralsOnlineConstants.GameDataSubdirectory + "/", StringComparison.OrdinalIgnoreCase);

            // Compute hash for each file
            var hash = await ComputeFileHashAsync(filePath, cancellationToken);

            filesWithHashes.Add(new ExtractedFileInfo(relativePath, fileInfo, hash, isMap, isGameData));
        }

        return filesWithHashes;
    }

    private List<ManifestFile> BuildManifestFilesForManifest(
        ContentManifest manifest,
        List<ExtractedFileInfo> filesWithHashes,
        string extractPath)
    {
        var manifestFiles = new List<ManifestFile>();

        if (manifest.ContentType == ContentType.MapPack)
        {
            foreach (var file in filesWithHashes)
            {
                if (file.IsMap)
                {
                    manifestFiles.Add(CreateMapManifestFile(file.RelativePath, file.FileInfo, file.Hash));
                }
            }

            logger.LogInformation("MapPack manifest '{Name}' updated with {Count} files", manifest.Name, manifestFiles.Count);
        }
        else if (manifest.ContentType == ContentType.Patch)
        {
            foreach (var file in filesWithHashes)
            {
                if (file.IsGameData)
                {
                    manifestFiles.Add(CreateGameDataManifestFile(file.RelativePath, file.FileInfo, file.Hash));
                }
            }

            logger.LogInformation("GameData patch manifest '{Name}' updated with {Count} files", manifest.Name, manifestFiles.Count);
        }
        else if (IsTestEnvironmentManifest(manifest))
        {
            var hasDedicatedTestExe = filesWithHashes.Any(file =>
                !file.IsMap && !file.IsGameData && IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnlineTestEnvironmentExecutable));

            var hasDefaultExe = filesWithHashes.Any(file =>
                !file.IsMap && !file.IsGameData && IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnlineDefaultExecutable));

            if (!hasDedicatedTestExe && !hasDefaultExe)
            {
                logger.LogInformation(
                    "Test environment executable not found in extract path; skipping test env files");
                return manifestFiles;
            }

            var targetExecutable = hasDedicatedTestExe
                ? GameClientConstants.GeneralsOnlineTestEnvironmentExecutable
                : GameClientConstants.GeneralsOnlineDefaultExecutable;

            foreach (var file in filesWithHashes)
            {
                if (file.IsMap || file.IsGameData)
                {
                    continue;
                }

                // Exclude 60Hz executable and Easy Anti-Cheat components from Test Environment client
                if (IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnline60HzExecutable) ||
                    IsEasyAntiCheatFile(file.RelativePath))
                {
                    continue;
                }

                var isExecutable = IsArchiveRootFile(file.RelativePath, targetExecutable);

                manifestFiles.Add(new ManifestFile
                {
                    RelativePath = file.RelativePath,
                    Size = file.FileInfo.Length,
                    Hash = file.Hash,
                    SourceType = ContentSourceType.ContentAddressable,
                    SourcePath = file.FileInfo.FullName,
                    InstallTarget = ContentInstallTarget.Workspace,
                    IsExecutable = isExecutable,
                });
            }

            logger.LogInformation("Test Environment GameClient manifest '{Name}' updated with {Count} files", manifest.Name, manifestFiles.Count);
        }
        else
        {
            var targetExecutable = DetermineGameClientTargetExecutable(filesWithHashes, extractPath);

            foreach (var file in filesWithHashes)
            {
                if (file.IsMap || file.IsGameData)
                {
                    continue;
                }

                // Exclude Test Environment dedicated executable from 60Hz GameClient
                if (IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnlineTestEnvironmentExecutable))
                {
                    continue;
                }

                var isExecutable = IsArchiveRootFile(file.RelativePath, targetExecutable);

                manifestFiles.Add(new ManifestFile
                {
                    RelativePath = file.RelativePath,
                    Size = file.FileInfo.Length,
                    Hash = file.Hash,
                    SourceType = ContentSourceType.ContentAddressable,
                    SourcePath = file.FileInfo.FullName,
                    InstallTarget = ContentInstallTarget.Workspace,
                    IsExecutable = isExecutable,
                });
            }

            logger.LogInformation("GameClient manifest '{Name}' updated with {Count} files", manifest.Name, manifestFiles.Count);
        }

        return manifestFiles;
    }

    private string DetermineGameClientTargetExecutable(
        List<ExtractedFileInfo> filesWithHashes,
        string extractPath)
    {
        var hasEacLauncher = filesWithHashes.Any(file =>
            !file.IsMap && !file.IsGameData && IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnlineEacLauncherExecutable));

        var has60Hz = filesWithHashes.Any(file =>
            !file.IsMap && !file.IsGameData && IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnline60HzExecutable));

        if (hasEacLauncher && GeneralsOnlineEacSettings.TryRead(extractPath, out var settings) && settings is not null)
        {
            if (string.Equals(
                GeneralsOnlineEacSettings.NormalizeExecutableName(settings.Executable),
                GameClientConstants.GeneralsOnline60HzExecutable,
                StringComparison.OrdinalIgnoreCase))
            {
                return GameClientConstants.GeneralsOnlineEacLauncherExecutable;
            }

            if (has60Hz)
            {
                logger.LogInformation(
                    "Generals Online EAC settings specify '{Configured}', but 60Hz live client '{LiveBinary}' is present. Targeting live binary directly.",
                    settings.Executable,
                    GameClientConstants.GeneralsOnline60HzExecutable);
                return GameClientConstants.GeneralsOnline60HzExecutable;
            }

            return GameClientConstants.GeneralsOnlineEacLauncherExecutable;
        }

        return hasEacLauncher
            ? GameClientConstants.GeneralsOnlineEacLauncherExecutable
            : GameClientConstants.GeneralsOnline60HzExecutable;
    }

    private InstallationInstructions BuildInstallationInstructions(
        ContentManifest manifest,
        List<ExtractedFileInfo> filesWithHashes,
        string extractPath)
    {
        var isTestEnv = IsTestEnvironmentManifest(manifest);
        var hasEacSetup = !isTestEnv && filesWithHashes.Any(file =>
            !file.IsMap && !file.IsGameData &&
            IsArchiveRootFile(file.RelativePath, GameClientConstants.GeneralsOnlineEacSetupExecutable));

        var inheritedPostSteps = (manifest.InstallationInstructions?.PostInstallSteps ?? [])
            .Where(s => s != null &&
                (!isTestEnv || !string.Equals(s.TargetRelativePath, GameClientConstants.GeneralsOnlineEacSetupExecutable, StringComparison.OrdinalIgnoreCase)) &&
                (hasEacSetup || !string.Equals(s.TargetRelativePath, GameClientConstants.GeneralsOnlineEacSetupExecutable, StringComparison.OrdinalIgnoreCase)));

        var instructions = new InstallationInstructions
        {
            WorkspaceStrategy = manifest.InstallationInstructions?.WorkspaceStrategy ?? WorkspaceConstants.DefaultWorkspaceStrategy,
            DownloadHash = manifest.InstallationInstructions?.DownloadHash,
            PostInstallSteps = [.. inheritedPostSteps],
        };

        if (!isTestEnv &&
            manifest.ContentType == ContentType.GameClient &&
            hasEacSetup &&
            instructions.PostInstallSteps.All(s => s == null || (!string.Equals(
                s.TargetRelativePath,
                GameClientConstants.GeneralsOnlineEacSetupExecutable,
                StringComparison.OrdinalIgnoreCase) && !string.Equals(s.StepKey, GeneralsOnlineConstants.EacStepKey, StringComparison.OrdinalIgnoreCase))))
        {
            instructions.PostInstallSteps.Add(CreateEacInstallStep(extractPath));
        }

        return instructions;
    }

    /// <summary>
    /// Declares the game client's entry point and, for bootstrapper entries, the child
    /// process the bootstrapper starts, so the launch pipeline reads data instead of
    /// guessing from file names.
    /// </summary>
    /// <param name="manifest">The game client manifest to enrich.</param>
    /// <param name="extractPath">The extracted package directory.</param>
    private void DeclareGameClientLaunch(ContentManifest manifest, string extractPath)
    {
        var files = ManifestVariantResolver.ResolveFiles(manifest);
        var entry = files.FirstOrDefault(file => file.IsExecutable);
        if (entry is null)
        {
            return;
        }

        manifest.EntryPoint = entry.RelativePath;

        if (!IsArchiveRootFile(entry.RelativePath, GameClientConstants.GeneralsOnlineEacLauncherExecutable))
        {
            return;
        }

        DeclareBootstrapperRelationship(manifest, extractPath);
    }

    /// <summary>
    /// Declares which binary the Easy Anti-Cheat bootstrapper starts, read from the
    /// package's own settings file. Absence degrades to launch-time legacy guessing.
    /// </summary>
    /// <param name="manifest">The game client manifest to enrich.</param>
    /// <param name="extractPath">The extracted package directory.</param>
    private void DeclareBootstrapperRelationship(ContentManifest manifest, string extractPath)
    {
        if (!GeneralsOnlineEacSettings.TryRead(extractPath, out var settings) || settings is null)
        {
            logger.LogWarning(
                "Bootstrapper entry without readable {Settings}; launch will guess the child process",
                GeneralsOnlineConstants.EacSettingsRelativePath);
            return;
        }

        var childName = Path.GetFileNameWithoutExtension(GeneralsOnlineEacSettings.NormalizeExecutableName(settings.Executable));
        if (!LaunchRelationship.IsValidProcessName(childName))
        {
            logger.LogWarning(
                "Bootstrapper settings name an unusable executable '{Executable}'; launch will guess the child process",
                settings.Executable);
            return;
        }

        var files = ManifestVariantResolver.ResolveFiles(manifest);
        var configuredFile = GeneralsOnlineEacSettings.NormalizeExecutableName(settings.Executable);
        if (files.All(file => !IsArchiveRootFile(file.RelativePath, configuredFile)))
        {
            logger.LogWarning(
                "Bootstrapper settings name '{Executable}', which is not in the package; launch will guess the child process",
                settings.Executable);
            return;
        }

        manifest.LaunchRelationship = new LaunchRelationship
        {
            ProcessName = childName,
            DiscoveryTimeoutMs = ProcessConstants.SpawnedChildDiscoveryTimeoutMs,
        };
        logger.LogInformation(
            "Declared launch relationship for '{Name}': bootstrapper starts '{Child}'",
            manifest.Name,
            childName);
    }

    private void ReconcileMissingExtractedDependencies(List<ContentManifest> manifests)
    {
        if (manifests.All(m => m.ContentType != ContentType.MapPack))
        {
            RemoveMissingDependencies(
                manifests,
                d => d.DependencyType == ContentType.MapPack,
                "MapPack");
        }

        if (manifests.All(m => m.ContentType != ContentType.Patch))
        {
            RemoveMissingDependencies(
                manifests,
                d => d.DependencyType == ContentType.Patch &&
                    (d.Id.Value.Contains(GeneralsOnlineConstants.GameDataPatchSuffix, StringComparison.OrdinalIgnoreCase) || d.PublisherType == PublisherTypeConstants.GeneralsOnline),
                "GameData patch");
        }
    }

    private void RemoveMissingDependencies(
        List<ContentManifest> manifests,
        Func<ContentDependency, bool> matchPredicate,
        string dependencyDescription)
    {
        foreach (var m in manifests)
        {
            if (m.Dependencies.Any(matchPredicate))
            {
                logger.LogWarning(
                    "Removing {DependencyDescription} dependency from manifest '{Name}' because it was not found in archive",
                    dependencyDescription,
                    m.Name);
                m.Dependencies = m.Dependencies.Where(d => !matchPredicate(d)).ToList();
            }
        }
    }
}
