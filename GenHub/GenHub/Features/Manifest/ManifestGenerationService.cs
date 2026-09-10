using CsvHelper;
using CsvHelper.Configuration;
using GenHub.Core.Constants;
using GenHub.Core.Features.GameInstallations;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Validation;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.ContentResolvers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Manifest;

/// <summary>
/// Service for generating content manifests from game installations and content packages.
/// </summary>
/// <remarks>
/// Provides methods to create <see cref="ContentManifest"/> objects for different content types
/// including GameInstallation and GameClient manifests with proper metadata and file references.
/// </remarks>
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "ManifestGenerationService coordinates manifest creation across file hashing, catalog resolution, configuration, and notification services injected via dependency injection.")]
public class ManifestGenerationService(
    ILogger<ManifestGenerationService> logger,
    IFileHashProvider hashProvider,
    IManifestIdService manifestIdService,
    IDownloadService downloadService,
    IConfigurationProviderService configurationProvider,
    ILanguageDetector? languageDetector = null,
    CsvResolver? csvResolver = null,
    INotificationService? notificationService = null) : IManifestGenerationService
{
    private enum AuthoritativeFileStatus
    {
        AddedMatching,
        AddedDiffering,
        MissingRequired,
        MissingOptional,
        Skipped,
    }

    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null,
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HashSet<string> FallbackFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".016",
        ".256",
        ".ani",
        ".asi",
        ".big",
        ".bik",
        ".bmp",
        ".cfg",
        ".csf",
        ".dat",
        ".dll",
        ".exe",
        ".flt",
        ".ico",
        ".ini",
        ".lcf",
        ".m3d",
        ".map",
        ".scb",
        ".str",
        ".sys",
        ".tga",
        ".txt",
        ".vp6",
        ".w3d",
        ".wav",
    };

    private readonly ILanguageDetector _languageDetector = languageDetector ?? new LanguageDetector();

    /// <summary>
    /// Creates a manifest builder for a game installation with string version normalization.
    /// </summary>
    /// <param name="gameInstallationPath">Path to the game installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="installationType">The installation type (Steam, EaApp).</param>
    /// <param name="manifestVersion">The manifest version (e.g., "1.08", "1.04", or integer like 0, 1, 2). If null, defaults to 0.</param>
    /// <param name="language">Optional explicit language code (e.g., "EN", "DE"). If null, language is detected automatically.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public Task<IContentManifestBuilder> CreateGameInstallationManifestAsync(
        string gameInstallationPath,
        GameType gameType,
        GameInstallationType installationType,
        string? manifestVersion = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        return CreateGameInstallationManifestAsync(
            gameInstallationPath,
            gameType,
            installationType,
            manifestVersion,
            language,
            progress: null,
            cancellationToken);
    }

    /// <summary>
    /// Creates a manifest builder for a game installation with string version normalization and progress reporting.
    /// </summary>
    /// <param name="gameInstallationPath">Path to the game installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="installationType">The installation type (Steam, EaApp).</param>
    /// <param name="manifestVersion">The manifest version (e.g., "1.08", "1.04", or integer like 0, 1, 2). If null, defaults to 0.</param>
    /// <param name="language">Optional explicit language code (e.g., "EN", "DE"). If null, language is detected automatically.</param>
    /// <param name="progress">Optional progress reporter receiving file indexing progress updates.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateGameInstallationManifestAsync(
        string gameInstallationPath,
        GameType gameType,
        GameInstallationType installationType,
        string? manifestVersion,
        string? language,
        IProgress<ValidationProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedVersion = ResolveManifestVersion(gameType, manifestVersion);

            logger.LogDebug(
                "Creating GameInstallation manifest for {GameType} at {GameInstallationPath}",
                gameType,
                gameInstallationPath);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, hashProvider, manifestIdService, downloadService, configurationProvider)
                .WithBasicInfo(installationType, gameType, resolvedVersion)
                .WithContentType(ContentType.GameInstallation, gameType);

            // Add publisher info with user-friendly display names matching InstallationTypeDisplayConverter
            var (publisherName, website, supportUrl) = PublisherInfoConstants.GetPublisherInfo(installationType);
            var publisher = new PublisherInfo
            {
                Name = publisherName,
                Website = website,
                SupportUrl = supportUrl,
                PublisherType = PublisherTypeConstants.FromInstallationType(installationType),
            };
            builder.WithPublisher(publisher.Name, publisher.Website, publisher.SupportUrl, string.Empty, publisher.PublisherType);

            // Add essential game files
            await AddGameFilesToManifest(builder, gameInstallationPath, gameType, resolvedVersion, language, progress, cancellationToken);

            logger.LogInformation(
                "Created GameInstallation manifest for {InstallationType} {GameType} (Publisher: {PublisherName})",
                installationType,
                gameType,
                publisher.Name);

            return builder;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating GameInstallation manifest for {GameType} at {GameInstallationPath}",
                gameType,
                gameInstallationPath);
            throw;
        }
    }

    /// <summary>
    /// Creates a manifest builder for a game installation with integer version.
    /// </summary>
    /// <param name="gameInstallationPath">Path to the game installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="installationType">The installation type (Steam, EaApp).</param>
    /// <param name="manifestVersion">The manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="language">Optional explicit language code (e.g., "EN", "DE"). If null, language is detected automatically.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public Task<IContentManifestBuilder> CreateGameInstallationManifestAsync(
        string gameInstallationPath,
        GameType gameType,
        GameInstallationType installationType,
        int manifestVersion = 0,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        return CreateGameInstallationManifestAsync(
            gameInstallationPath,
            gameType,
            installationType,
            manifestVersion.ToString(),
            language,
            progress: null,
            cancellationToken);
    }

    /// <summary>
    /// Creates a manifest builder for a game installation with integer version and progress reporting.
    /// </summary>
    /// <param name="gameInstallationPath">Path to the game installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="installationType">The installation type (Steam, EaApp).</param>
    /// <param name="manifestVersion">The manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="language">Optional explicit language code (e.g., "EN", "DE"). If null, language is detected automatically.</param>
    /// <param name="progress">Optional progress reporter receiving file indexing progress updates.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public Task<IContentManifestBuilder> CreateGameInstallationManifestAsync(
        string gameInstallationPath,
        GameType gameType,
        GameInstallationType installationType,
        int manifestVersion,
        string? language,
        IProgress<ValidationProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return CreateGameInstallationManifestAsync(
            gameInstallationPath,
            gameType,
            installationType,
            manifestVersion.ToString(),
            language,
            progress,
            cancellationToken);
    }

    /// <summary>
    /// Creates a content manifest for a content package.
    /// </summary>
    /// <param name="contentDirectory">Path to the content directory.</param>
    /// <param name="publisherId">The publisher identifier used to deterministically generate the manifest id.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="contentType">Type of content (Mod, Patch, Addon, etc).</param>
    /// <param name="targetGame">Target game type.</param>
    /// <param name="dependencies">Dependencies for this content.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateContentManifestAsync(
        string contentDirectory,
        string publisherId,
        string contentName,
        int manifestVersion = 0,
        ContentType contentType = ContentType.Mod,
        GameType targetGame = GameType.Generals,
        params ContentDependency[] dependencies)
    {
        try
        {
            logger.LogDebug(
                "Creating {ContentType} manifest for {ContentName} at {ContentDirectory} (Publisher: {PublisherId})",
                contentType,
                contentName,
                contentDirectory,
                publisherId);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, hashProvider, manifestIdService, downloadService, configurationProvider)
                .WithBasicInfo(publisherId, contentName, manifestVersion.ToString())
                .WithContentType(contentType, targetGame);

            // Add dependencies
            foreach (var dependency in dependencies)
            {
                builder.AddDependency(
                    dependency.Id,
                    dependency.Name,
                    dependency.DependencyType,
                    dependency.InstallBehavior);
            }

            // Add files from content directory
            if (!string.IsNullOrEmpty(contentDirectory) && Directory.Exists(contentDirectory))
            {
                await builder.AddFilesFromDirectoryAsync(contentDirectory, ContentSourceType.ContentAddressable);
            }
            else
            {
                logger.LogWarning("Content directory {ContentDirectory} not found or empty. Manifest will have no files.", contentDirectory);
            }

            return builder;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating content manifest for {ContentName} at {ContentDirectory}",
                contentName,
                contentDirectory);
            throw;
        }
    }

    /// <summary>
    /// Creates a content bundle from multiple content items.
    /// </summary>
    /// <param name="publisherId">The publisher identifier used to generate the bundle id.</param>
    /// <param name="bundleName">The bundle name.</param>
    /// <param name="manifestVersion">The manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="publisher">The publisher information.</param>
    /// <param name="items">The bundle items.</param>
    /// <returns>A <see cref="Task"/> that returns the created <see cref="ContentBundle"/>.</returns>
    public async Task<ContentBundle> CreateContentBundleAsync(
        string publisherId,
        string bundleName,
        int manifestVersion = 0,
        PublisherInfo? publisher = null,
        params BundleItem[] items)
    {
        try
        {
            logger.LogDebug("Creating content bundle {BundleName} version {ManifestVersion}", bundleName, manifestVersion);

            var bundleId = ManifestId.Create($"{manifestVersion}.0.bundle.{bundleName.ToLowerInvariant().Replace(" ", string.Empty)}");

            var bundle = new ContentBundle
            {
                Id = bundleId,
                Name = bundleName,
                Version = manifestVersion.ToString(),
                Items = [.. items],
            };

            return await Task.FromResult(bundle);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating content bundle {BundleName}", bundleName);
            throw;
        }
    }

    /// <summary>
    /// Creates a publisher referral manifest with a deterministic id.
    /// </summary>
    /// <param name="publisherId">The publisher identifier used to generate the referral id.</param>
    /// <param name="referralName">Display name for the referral.</param>
    /// <param name="manifestVersion">Manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="targetPublisherId">The target publisher id being referred to.</param>
    /// <param name="referralUrl">The URL for the referral.</param>
    /// <param name="description">Optional description for the referral.</param>
    /// <returns>A <see cref="Task"/> that returns the created <see cref="ContentManifest"/>.</returns>
    public async Task<ContentManifest> CreatePublisherReferralAsync(
        string publisherId,
        string referralName,
        int manifestVersion = 0,
        string targetPublisherId = "",
        string referralUrl = "",
        string description = "")
    {
        try
        {
            logger.LogDebug(
                "Creating publisher referral {ReferralName} for {TargetPublisherId}",
                referralName,
                targetPublisherId);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, hashProvider, manifestIdService, downloadService, configurationProvider)
                .WithBasicInfo(publisherId, referralName, manifestVersion.ToString())

                // Note: Publisher referrals are typically game-agnostic, but we default to ZeroHour for compatibility
                .WithContentType(ContentType.PublisherReferral, GameType.ZeroHour)
                .WithMetadata(description);

            return await Task.FromResult(builder.Build());
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating publisher referral {ReferralName}",
                referralName);
            throw;
        }
    }

    /// <summary>
    /// Creates a content referral.
    /// </summary>
    /// <param name="publisherId">The publisher identifier.</param>
    /// <param name="referralName">The referral name.</param>
    /// <param name="manifestVersion">The manifest version.</param>
    /// <param name="targetContentId">The target content ID.</param>
    /// <param name="targetPublisherId">The target publisher ID.</param>
    /// <param name="referralUrl">The referral URL.</param>
    /// <param name="description">The description.</param>
    /// <returns>A <see cref="ContentManifest"/> for the referral.</returns>
    public async Task<ContentManifest> CreateContentReferralAsync(
        string publisherId,
        string referralName,
        int manifestVersion,
        string targetContentId,
        string targetPublisherId,
        string referralUrl,
        string description)
    {
        try
        {
            logger.LogDebug(
                "Creating content referral {ReferralName} for {TargetContentId}",
                referralName,
                targetContentId);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, hashProvider, manifestIdService, downloadService, configurationProvider)
                .WithBasicInfo(publisherId, referralName, manifestVersion.ToString())
                .WithContentType(ContentType.ContentReferral, GameType.ZeroHour) // Default to ZeroHour
                .WithMetadata(description);

            return await Task.FromResult(builder.Build());
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error creating content referral {ReferralName}",
                referralName);
            throw;
        }
    }

    /// <summary>
    /// Saves a manifest to the specified output path.
    /// </summary>
    /// <param name="manifest">The manifest to save.</param>
    /// <param name="outputPath">The output path.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SaveManifestAsync(ContentManifest manifest, string outputPath)
    {
        try
        {
            logger.LogDebug("Saving manifest {ManifestId} to {OutputPath}", manifest.Id, outputPath);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = JsonOptions;

            await using var stream = File.Create(outputPath);
            await JsonSerializer.SerializeAsync(stream, manifest, options);

            logger.LogInformation("Manifest {ManifestId} saved to {OutputPath}", manifest.Id, outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save manifest {ManifestId} to {OutputPath}", manifest.Id, outputPath);
            throw;
        }
    }

    /// <summary>
    /// Creates a manifest builder for a game client.
    /// </summary>
    /// <param name="installationPath">Path to the game client installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="clientName">The name of the game client.</param>
    /// <param name="clientVersion">The version of the game client.</param>
    /// <param name="executablePath">The full path to the game executable.</param>
    /// <param name="publisherInfo">Optional publisher info. If provided, overrides detection from name.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateGameClientManifestAsync(
        string installationPath,
        GameType gameType,
        string clientName,
        string clientVersion,
        string executablePath,
        PublisherInfo? publisherInfo = null)
    {
        try
        {
            logger.LogDebug("Creating GameClient manifest for {ClientName} at {InstallationPath}", clientName, installationPath);

            // Validate executable exists
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException("Executable path cannot be null or empty", nameof(executablePath));
            }

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException($"Game executable not found at: {executablePath}", executablePath);
            }

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;

            // Determine publisher name: Use provided info, or fall back to name inference
            PublisherInfo publisher;
            if (publisherInfo != null)
            {
                publisher = publisherInfo;
            }
            else
            {
                var publisherName = clientName switch
                {
                    _ when clientName.Contains("steam", StringComparison.InvariantCultureIgnoreCase) => PublisherInfoConstants.Steam.Name,
                    _ when clientName.Contains("ea", StringComparison.InvariantCultureIgnoreCase) => PublisherInfoConstants.EaApp.Name,
                    _ => PublisherInfoConstants.Retail.Name,
                };
                publisher = new PublisherInfo { Name = publisherName };
            }

            var contentName = gameType.ToString().ToLowerInvariant();
            var builder = new ContentManifestBuilder(builderLogger, hashProvider, manifestIdService, downloadService, configurationProvider)
                .WithBasicInfo(publisher, contentName, clientVersion)
                .WithContentType(ContentType.GameClient, gameType);

            await AddClientFilesToManifest(builder, installationPath, gameType, executablePath, publisher.Name);

            logger.LogInformation("Created GameClient manifest for {ClientName} (Publisher: {PublisherName})", clientName, publisher.Name);

            return builder;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating GameClient manifest for {ClientName} at {InstallationPath}", clientName, installationPath);
            throw;
        }
    }

    /// <summary>
    /// Creates a manifest builder for a GeneralsOnline game client with special handling.
    /// </summary>
    /// <param name="installationPath">Path to the game client installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="clientName">The name of the GeneralsOnline client.</param>
    /// <param name="clientVersion">The version of the client (typically "Auto-Updated").</param>
    /// <param name="executablePath">The full path to the GeneralsOnline executable.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateGeneralsOnlineClientManifestAsync(
        string installationPath,
        GameType gameType,
        string clientName,
        string clientVersion,
        string executablePath)
    {
        try
        {
            logger.LogDebug("Creating GeneralsOnline client manifest for {ClientName} at {InstallationPath}", clientName, installationPath);

            // Validate executable exists
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException("Executable path cannot be null or empty", nameof(executablePath));
            }

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException($"GeneralsOnline executable not found at: {executablePath}", executablePath);
            }

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;

            // GeneralsOnline-specific publisher info
            var publisher = new PublisherInfo
            {
                Name = PublisherInfoConstants.GeneralsOnline.Name,
                Website = PublisherInfoConstants.GeneralsOnline.Website,
                SupportUrl = PublisherInfoConstants.GeneralsOnline.SupportUrl,
                PublisherType = PublisherTypeConstants.GeneralsOnline,
            };

            // Create unique manifest name based on executable to distinguish variants (60Hz, standard)
            var executableFileName = Path.GetFileNameWithoutExtension(executablePath).ToLowerInvariant();
            var contentName = $"{gameType.ToString().ToLowerInvariant()}{executableFileName.Replace("-", string.Empty).Replace(".", string.Empty)}";
            var builder = new ContentManifestBuilder(builderLogger, hashProvider, manifestIdService, downloadService, configurationProvider)
                .WithBasicInfo(publisher, contentName, clientVersion)
                .WithContentType(ContentType.GameClient, gameType)
                .WithMetadata(
                    "GeneralsOnline community client with auto-updates and enhanced compatibility",
                    tags: ["community", "enhanced", "multiplayer", "auto-update"]);

            // GeneralsOnline only supports Zero Hour, not vanilla Generals
            // Add dependency constraints to enforce this at manifest build time
            // The dependency validation will check CompatibleGameTypes during profile launch

            // Add GeneralsOnline client files to manifest
            // NOTE: Hash validation is intentionally relaxed for GeneralsOnline clients
            // because they could be updated by the GeneralsOnline updater at any time.
            await AddGeneralsOnlineClientFilesToManifest(builder, installationPath, gameType, executablePath);

            logger.LogInformation("Created GeneralsOnline client manifest for {ClientName} (Publisher: Generals Online)", clientName);

            return builder;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating GeneralsOnline client manifest for {ClientName} at {InstallationPath}", clientName, installationPath);
            throw;
        }
    }

    /// <summary>
    /// Formats a list of file paths with truncation and an ellipsis suffix if count exceeds <see cref="ManifestConstants.MaxMissingFilesNotificationDisplayCount"/>.
    /// </summary>
    /// <param name="files">The list of file paths.</param>
    /// <returns>A formatted comma-separated string of files, truncated if necessary.</returns>
    internal static string FormatFileListWithEllipsis(IReadOnlyList<string> files)
    {
        var list = string.Join(", ", files.Take(ManifestConstants.MaxMissingFilesNotificationDisplayCount));
        var extra = files.Count > ManifestConstants.MaxMissingFilesNotificationDisplayCount
            ? $" and {files.Count - ManifestConstants.MaxMissingFilesNotificationDisplayCount} more"
            : string.Empty;
        return $"{list}{extra}";
    }

    /// <summary>
    /// Constructs a user-facing warning notification message detailing missing and/or skipped required files.
    /// </summary>
    /// <param name="gameType">The target game type.</param>
    /// <param name="missingRequiredFiles">The list of required files missing from disk.</param>
    /// <param name="skippedRequiredFiles">The list of required files skipped due to access errors or symlinks.</param>
    /// <returns>A formatted warning message.</returns>
    internal static string GetIncompleteInstallationWarningMessage(
        GameType gameType,
        IReadOnlyList<string> missingRequiredFiles,
        IReadOnlyList<string> skippedRequiredFiles)
    {
        if (missingRequiredFiles.Count > 0 && skippedRequiredFiles.Count > 0)
        {
            var missingList = FormatFileListWithEllipsis(missingRequiredFiles);
            var skippedList = FormatFileListWithEllipsis(skippedRequiredFiles);
            return $"{gameType} has {missingRequiredFiles.Count} missing required file(s) ({missingList}) and {skippedRequiredFiles.Count} unreadable/skipped file(s) ({skippedList}). A game repair or permission check is recommended.";
        }

        if (missingRequiredFiles.Count > 0)
        {
            var missingList = FormatFileListWithEllipsis(missingRequiredFiles);
            return $"{gameType} is missing {missingRequiredFiles.Count} required file(s): {missingList}. A clean reinstall or repair via EA App/Steam is recommended.";
        }

        var unreadableList = FormatFileListWithEllipsis(skippedRequiredFiles);
        return $"{gameType} could not read {skippedRequiredFiles.Count} required file(s) (e.g. file lock, permissions, or symlink): {unreadableList}. Please verify permissions or close background processes.";
    }

    /// <summary>
    /// Determines if a file should be skipped during manifest generation.
    /// </summary>
    private static bool ShouldSkipFile(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith(SteamConstants.BackupDirName + "/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith(FileTypes.GitDirectoryName + "/", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(SteamConstants.BackupExtension, StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(FileTypes.LegacyBackupExtension, StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(SteamConstants.ProxyLauncherFileName, StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(SteamConstants.TrackingFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetCatalogInfo(GameType gameType, string version, out (string FileName, string Sha256) info)
    {
        if (gameType == GameType.Generals && version is "1.08" or "1.8")
        {
            info = (CsvConstants.GeneralsCsvFileName, CsvConstants.Generals108Sha256);
            return true;
        }

        if (gameType == GameType.ZeroHour && version is "1.04" or "1.4")
        {
            info = (CsvConstants.ZeroHourCsvFileName, CsvConstants.ZeroHour104Sha256);
            return true;
        }

        info = default;
        return false;
    }

    private static List<CsvCatalogEntry> FilterEntriesByGameAndLanguage(
        IEnumerable<CsvCatalogEntry> records,
        string targetGame,
        string targetLanguage)
    {
        var result = new List<CsvCatalogEntry>();
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.RelativePath))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(targetGame) &&
                !string.Equals(record.GameType, targetGame, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (MatchesLanguage(record.Language, targetLanguage))
            {
                result.Add(record);
            }
        }

        return result;
    }

    private static bool MatchesLanguage(string? entryLanguage, string targetLanguage)
    {
        if (string.Equals(targetLanguage, CsvConstants.AllLanguagesFilter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(entryLanguage) ||
            string.Equals(entryLanguage, CsvConstants.AllLanguagesFilter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedEntryLang = ContentSearchQuery.NormalizeLanguage(entryLanguage);
        return string.Equals(normalizedEntryLang, targetLanguage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that a relative path stays within the base installation directory and returns its exact path.
    /// </summary>
    private static string? GetSafeExactPath(string installationPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (normalizedRelative.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            normalizedRelative.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        var fullInstallationPath = Path.GetFullPath(installationPath);
        var candidatePath = Path.GetFullPath(Path.Combine(fullInstallationPath, normalizedRelative));

        var rootWithSeparator = Path.TrimEndingDirectorySeparator(fullInstallationPath) + Path.DirectorySeparatorChar;
        if (!candidatePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return candidatePath;
    }

    /// <summary>
    /// Resolves a single directory child segment case-insensitively while skipping reparse points.
    /// </summary>
    private static string? ResolveChildDirectory(string currentDir, string segment, EnumerationOptions options)
    {
        if (segment == "." || segment == "..")
        {
            return null;
        }

        try
        {
            var match = Directory.EnumerateDirectories(currentDir, "*", options)
                .FirstOrDefault(d => string.Equals(Path.GetFileName(d), segment, StringComparison.OrdinalIgnoreCase));

            return (match != null && !IsReparsePoint(match)) ? match : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves a file child segment case-insensitively while skipping reparse points.
    /// </summary>
    private static string? ResolveChildFile(string currentDir, string fileName, EnumerationOptions options)
    {
        try
        {
            var match = Directory.EnumerateFiles(currentDir, "*", options)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));

            return (match != null && !IsReparsePoint(match)) ? match : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true when the directory is missing or is a reparse point other than the installation root itself.
    /// </summary>
    private static bool IsInvalidIntermediateDirectory(string currentDir, string fullInstallationPath)
    {
        return !Directory.Exists(currentDir) ||
               (!string.Equals(currentDir, fullInstallationPath, StringComparison.OrdinalIgnoreCase) && IsReparsePoint(currentDir));
    }

    /// <summary>
    /// Finds a file in the installation directory using case-insensitive path resolution.
    /// Rejects paths containing symbolic links or reparse points to prevent path traversal.
    /// </summary>
    private static string? FindFileCaseInsensitive(string installationPath, string relativePath)
    {
        var exactPath = GetSafeExactPath(installationPath, relativePath);
        if (exactPath == null)
        {
            return null;
        }

        var fullInstallationPath = Path.GetFullPath(installationPath);
        if (File.Exists(exactPath))
        {
            return HasReparsePointInPath(fullInstallationPath, exactPath) ? null : exactPath;
        }

        var segments = relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var currentDir = fullInstallationPath;

        var enumOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (IsInvalidIntermediateDirectory(currentDir, fullInstallationPath))
            {
                return null;
            }

            var nextDir = ResolveChildDirectory(currentDir, segments[i], enumOptions);
            if (nextDir == null)
            {
                return null;
            }

            currentDir = nextDir;
        }

        if (IsInvalidIntermediateDirectory(currentDir, fullInstallationPath))
        {
            return null;
        }

        return ResolveChildFile(currentDir, segments[^1], enumOptions);
    }

    /// <summary>
    /// Checks whether the target path or any intermediate directory beneath the base path is a reparse point or symlink.
    /// </summary>
    private static bool HasReparsePointInPath(string basePath, string targetPath)
    {
        try
        {
            var normalizedBase = Path.TrimEndingDirectorySeparator(Path.GetFullPath(basePath));
            var currentPath = Path.GetFullPath(targetPath);

            while (!string.IsNullOrEmpty(currentPath) && !string.Equals(currentPath, normalizedBase, StringComparison.OrdinalIgnoreCase))
            {
                if ((File.Exists(currentPath) || Directory.Exists(currentPath)) && IsReparsePoint(currentPath))
                {
                    return true;
                }

                currentPath = Path.GetDirectoryName(currentPath);
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string ResolveManifestVersion(GameType gameType, string? manifestVersion)
    {
        if (!string.IsNullOrWhiteSpace(manifestVersion) && !int.TryParse(manifestVersion, out _))
        {
            return manifestVersion;
        }

        return gameType == GameType.Generals
            ? ManifestConstants.GeneralsManifestVersion
            : ManifestConstants.ZeroHourManifestVersion;
    }

    /// <summary>
    /// Determines whether the specified file path is a symbolic link or reparse point.
    /// </summary>
    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void RecordAuthoritativeStatus(
        AuthoritativeFileStatus status,
        CsvCatalogEntry entry,
        ref int fileCount,
        List<string> differingFiles,
        List<string> missingRequiredFiles,
        List<string> skippedRequiredFiles)
    {
        switch (status)
        {
            case AuthoritativeFileStatus.AddedMatching:
                fileCount++;
                break;
            case AuthoritativeFileStatus.AddedDiffering:
                fileCount++;
                differingFiles.Add(entry.RelativePath);
                break;
            case AuthoritativeFileStatus.MissingRequired:
                missingRequiredFiles.Add(entry.RelativePath);
                break;
            case AuthoritativeFileStatus.MissingOptional:
                break;
            case AuthoritativeFileStatus.Skipped:
                if (entry.IsRequired)
                {
                    skippedRequiredFiles.Add(entry.RelativePath);
                }

                break;
            default:
                break;
        }
    }

    private static bool IsAuthoritativeMatch(CsvCatalogEntry entry, long actualLength, string computedHash)
    {
        return entry.Size > 0 &&
               actualLength == entry.Size &&
               !string.IsNullOrWhiteSpace(entry.Sha256) &&
               string.Equals(computedHash, entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds authoritative vanilla game files to a manifest builder using the CSV catalog authority.
    /// </summary>
    /// <param name="builder">The manifest builder.</param>
    /// <param name="installationPath">The installation path.</param>
    /// <param name="gameType">The game type.</param>
    /// <param name="manifestVersion">Optional manifest version.</param>
    /// <param name="language">Optional explicit language code.</param>
    /// <param name="progress">Optional progress reporter receiving file indexing progress updates.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddGameFilesToManifest(
        IContentManifestBuilder builder,
        string installationPath,
        GameType gameType,
        string? manifestVersion,
        string? language,
        IProgress<ValidationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var detectedLanguage = string.IsNullOrWhiteSpace(language)
            ? await _languageDetector.DetectAsync(installationPath, cancellationToken)
            : language;

        var normalizedLanguage = ContentSearchQuery.NormalizeLanguage(detectedLanguage);
        var version = ResolveManifestVersion(gameType, manifestVersion);

        logger.LogInformation(
            "Starting authoritative manifest generation for {GameType} v{Version} ({Language}) at {InstallationPath}",
            gameType,
            version,
            normalizedLanguage,
            installationPath);

        var authoritativeEntries = await GetAuthoritativeEntriesAsync(gameType, version, normalizedLanguage, cancellationToken);

        if (authoritativeEntries.Count == 0)
        {
            logger.LogWarning(
                "No authoritative CSV entries found for {GameType} v{Version} ({Language}). Falling back to directory scan manifest generation.",
                gameType,
                version,
                normalizedLanguage);

            await AddGameFilesFromDirectoryScanAsync(builder, installationPath, gameType, cancellationToken);
            return;
        }

        var totalEntries = authoritativeEntries.Count;
        var progressNotificationId = Guid.NewGuid();
        if (notificationService != null)
        {
            var progressNotification = new NotificationMessage(
                NotificationType.Info,
                ManifestConstants.IndexingNotificationTitle,
                $"Scanning {gameType} installation files (0/{totalEntries} verified)...",
                autoDismissMilliseconds: null,
                isPersistent: true)
            {
                Id = progressNotificationId,
            };
            notificationService.Show(progressNotification);
        }

        var fileCount = 0;
        var missingRequiredFiles = new List<string>();
        var skippedRequiredFiles = new List<string>();
        var differingFiles = new List<string>();
        var lastLogTimestamp = Stopwatch.GetTimestamp();
        var lastNotificationTimestamp = Stopwatch.GetTimestamp();

        try
        {
            for (var i = 0; i < totalEntries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = authoritativeEntries[i];
                var currentIndex = i + 1;

                UpdateVerificationNotification(
                    progressNotificationId,
                    gameType,
                    currentIndex,
                    totalEntries,
                    entry.RelativePath,
                    ref lastNotificationTimestamp);

                var result = await TryAddAuthoritativeEntryAsync(
                    builder,
                    installationPath,
                    entry,
                    currentIndex,
                    totalEntries,
                    progressNotificationId,
                    cancellationToken);

                RecordAuthoritativeStatus(
                    result,
                    entry,
                    ref fileCount,
                    differingFiles,
                    missingRequiredFiles,
                    skippedRequiredFiles);

                progress?.Report(new ValidationProgress(currentIndex, totalEntries, entry.RelativePath));

                LogVerificationProgress(
                    gameType,
                    currentIndex,
                    totalEntries,
                    entry.RelativePath,
                    ref lastLogTimestamp);
            }
        }
        finally
        {
            notificationService?.Dismiss(progressNotificationId);
        }

        logger.LogInformation(
            "Completed authoritative manifest generation for {GameType}: {TotalFiles} vanilla files added ({DifferingCount} differed from catalog, {MissingCount} required files missing, {SkippedCount} required files skipped)",
            gameType,
            fileCount,
            differingFiles.Count,
            missingRequiredFiles.Count,
            skippedRequiredFiles.Count);

        NotifyManifestGenerationCompletion(
            gameType,
            fileCount,
            totalEntries,
            missingRequiredFiles,
            skippedRequiredFiles,
            differingFiles);
    }

    private void UpdateVerificationNotification(
        Guid progressNotificationId,
        GameType gameType,
        int currentIndex,
        int totalEntries,
        string relativePath,
        ref long lastNotificationTimestamp)
    {
        if (currentIndex != 1 &&
            !(Stopwatch.GetElapsedTime(lastNotificationTimestamp).TotalMilliseconds >= ManifestConstants.NotificationUpdateThrottleMs))
        {
            return;
        }

        var percent = (double)currentIndex / totalEntries * 100;
        notificationService?.Update(
            progressNotificationId,
            $"Verifying {gameType} files: {currentIndex}/{totalEntries} ({percent:F0}%) - {relativePath}",
            ManifestConstants.IndexingNotificationTitle);
        lastNotificationTimestamp = Stopwatch.GetTimestamp();
    }

    private void LogVerificationProgress(
        GameType gameType,
        int currentIndex,
        int totalEntries,
        string relativePath,
        ref long lastLogTimestamp)
    {
        var isThrottled = currentIndex != 1 &&
            currentIndex % ManifestConstants.ProgressLoggingThrottleInterval != 0 &&
            currentIndex != totalEntries &&
            !(Stopwatch.GetElapsedTime(lastLogTimestamp).TotalSeconds >= ManifestConstants.ProgressLogThrottleSeconds);

        if (isThrottled)
        {
            return;
        }

        var percent = (double)currentIndex / totalEntries * 100;
        logger.LogInformation(
            "Generating manifest for {GameType}: {Current}/{Total} files processed ({Percent:F0}%) - {RelativePath}",
            gameType,
            currentIndex,
            totalEntries,
            percent,
            relativePath);
        lastLogTimestamp = Stopwatch.GetTimestamp();
    }

    private void NotifyManifestGenerationCompletion(
        GameType gameType,
        int fileCount,
        int totalEntries,
        IReadOnlyList<string> missingRequiredFiles,
        IReadOnlyList<string> skippedRequiredFiles,
        IReadOnlyList<string> differingFiles)
    {
        var totalIncompleteRequiredCount = missingRequiredFiles.Count + skippedRequiredFiles.Count;
        if (totalIncompleteRequiredCount > 0)
        {
            var warningMessage = GetIncompleteInstallationWarningMessage(
                gameType,
                missingRequiredFiles,
                skippedRequiredFiles);

            notificationService?.ShowWarning(
                ManifestConstants.IncompleteInstallationNotificationTitle,
                warningMessage,
                autoDismissMs: ManifestConstants.WarningNotificationAutoDismissMs);
        }
        else
        {
            var differingMessage = differingFiles.Count > 0
                ? $" ({differingFiles.Count} differing from catalog)"
                : string.Empty;
            notificationService?.ShowSuccess(
                ManifestConstants.IndexedNotificationTitle,
                $"Completed verification for {gameType} ({fileCount}/{totalEntries} files verified{differingMessage}).",
                autoDismissMs: ManifestConstants.DefaultNotificationAutoDismissMs);
        }
    }

    /// <summary>
    /// Fallback method that scans the game installation directory and adds common files to the manifest
    /// when no authoritative CSV catalog is available (e.g., unpatched, custom, or legacy versions).
    /// </summary>
    private async Task AddGameFilesFromDirectoryScanAsync(
        IContentManifestBuilder builder,
        string installationPath,
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Starting fallback directory scan manifest generation for {GameType} at {InstallationPath}", gameType, installationPath);

        var progressNotificationId = Guid.NewGuid();
        if (notificationService != null)
        {
            var progressNotification = new NotificationMessage(
                NotificationType.Info,
                ManifestConstants.IndexingNotificationTitle,
                $"Scanning {gameType} directory files...",
                autoDismissMilliseconds: null,
                isPersistent: true)
            {
                Id = progressNotificationId,
            };
            notificationService.Show(progressNotification);
        }

        try
        {
            var executableName = gameType == GameType.Generals ? GameClientConstants.GeneralsExecutable : GameClientConstants.ZeroHourExecutable;
            await TryAddPrimaryExecutableAsync(builder, installationPath, executableName);

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            var scannedFiles = 0;
            var lastScanNotificationTimestamp = Stopwatch.GetTimestamp();
            var lastScanLogTimestamp = Stopwatch.GetTimestamp();

            foreach (var file in Directory.EnumerateFiles(installationPath, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                scannedFiles++;
                var relativePath = Path.GetRelativePath(installationPath, file).Replace('\\', '/');

                if (scannedFiles == 1 ||
                    scannedFiles % ManifestConstants.ProgressLoggingThrottleInterval == 0 ||
                    Stopwatch.GetElapsedTime(lastScanLogTimestamp).TotalSeconds >= ManifestConstants.ProgressLogThrottleSeconds)
                {
                    logger.LogInformation(
                        "Scanning {GameType} directory: {Count} files processed ({CurrentFile})",
                        gameType,
                        scannedFiles,
                        relativePath);
                    lastScanLogTimestamp = Stopwatch.GetTimestamp();
                }

                if (scannedFiles == 1 ||
                    Stopwatch.GetElapsedTime(lastScanNotificationTimestamp).TotalMilliseconds >= ManifestConstants.NotificationUpdateThrottleMs)
                {
                    notificationService?.Update(
                        progressNotificationId,
                        $"Scanning {gameType} directory: {scannedFiles} files processed ({relativePath})",
                        ManifestConstants.IndexingNotificationTitle);
                    lastScanNotificationTimestamp = Stopwatch.GetTimestamp();
                }

                await TryAddFallbackFileAsync(builder, installationPath, file, executableName, progressNotificationId);
            }

            notificationService?.ShowSuccess(
                ManifestConstants.IndexedNotificationTitle,
                $"Completed file scan for {gameType} ({scannedFiles} files scanned).",
                autoDismissMs: ManifestConstants.DefaultNotificationAutoDismissMs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to enumerate files during directory scan at {InstallationPath}", installationPath);
            notificationService?.ShowWarning(
                ManifestConstants.DirectoryScanWarningNotificationTitle,
                $"Failed to complete directory scan for {gameType}.",
                autoDismissMs: ManifestConstants.WarningNotificationAutoDismissMs);
        }
        finally
        {
            notificationService?.Dismiss(progressNotificationId);
        }
    }

    private async Task TryAddPrimaryExecutableAsync(
        IContentManifestBuilder builder,
        string installationPath,
        string executableName)
    {
        var executablePath = Path.Combine(installationPath, executableName);

        if (!File.Exists(executablePath))
        {
            return;
        }

        var sourcePath = ResolveSourcePathWithBackup(executablePath, executableName);

        try
        {
            if (File.GetAttributes(executablePath).HasFlag(FileAttributes.ReparsePoint) ||
                (sourcePath != executablePath && File.GetAttributes(sourcePath).HasFlag(FileAttributes.ReparsePoint)))
            {
                logger.LogWarning(
                    "Primary executable {ExecutableName} at {ExecutablePath} is a reparse point or symbolic link and will be skipped",
                    executableName,
                    executablePath);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to read attributes for primary executable {ExecutableName} at {ExecutablePath}", executableName, executablePath);
            return;
        }

        try
        {
            await builder.AddGameInstallationFileAsync(executableName, sourcePath, isExecutable: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to add primary executable {ExecutableName} to manifest from {SourcePath}", executableName, sourcePath);
        }
    }

    private async Task TryAddFallbackFileAsync(
        IContentManifestBuilder builder,
        string installationPath,
        string file,
        string executableName,
        Guid? progressNotificationId = null)
    {
        var relativePath = Path.GetRelativePath(installationPath, file).Replace('\\', '/');

        if (ShouldSkipFile(relativePath))
        {
            return;
        }

        if (relativePath.Equals(executableName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var extension = Path.GetExtension(file);
        if (!FallbackFileExtensions.Contains(extension))
        {
            return;
        }

        var sourcePath = ResolveSourcePathWithBackup(file, relativePath);
        var isExecutable = ExecutableFileClassifier.RequiresExecutePermission(relativePath, sourcePath);

        try
        {
            var fileInfo = new FileInfo(sourcePath);
            if (fileInfo.Length >= ManifestConstants.LargeFileProgressThresholdBytes)
            {
                var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
                logger.LogInformation(
                    "Calculating hash for fallback file {RelativePath} ({SizeMB:F1} MB)...",
                    relativePath,
                    sizeMb);

                if (progressNotificationId.HasValue)
                {
                    notificationService?.Update(
                        progressNotificationId.Value,
                        $"Calculating hash for {relativePath} ({sizeMb:F1} MB)...",
                        ManifestConstants.IndexingNotificationTitle);
                }
            }

            await builder.AddGameInstallationFileAsync(relativePath, sourcePath, isExecutable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to add fallback file {RelativePath} to manifest", relativePath);
        }
    }

    private async Task<AuthoritativeFileStatus> TryAddAuthoritativeEntryAsync(
        IContentManifestBuilder builder,
        string installationPath,
        CsvCatalogEntry entry,
        int currentIndex,
        int totalEntries,
        Guid? progressNotificationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.RelativePath))
        {
            return AuthoritativeFileStatus.Skipped;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolvedFilePath = FindFileCaseInsensitive(installationPath, entry.RelativePath);
            if (resolvedFilePath == null || !File.Exists(resolvedFilePath))
            {
                if (entry.IsRequired)
                {
                    logger.LogWarning(
                        "Required vanilla file missing from installation: {RelativePath}",
                        entry.RelativePath);
                    return AuthoritativeFileStatus.MissingRequired;
                }

                return AuthoritativeFileStatus.MissingOptional;
            }

            var sourcePath = ResolveSourcePathWithBackup(resolvedFilePath, entry.RelativePath);
            if (IsReparsePoint(sourcePath))
            {
                logger.LogWarning(
                    "Source path {SourcePath} for {RelativePath} is a reparse point or symbolic link and will be skipped",
                    sourcePath,
                    entry.RelativePath);
                return AuthoritativeFileStatus.Skipped;
            }

            var fileInfo = new FileInfo(sourcePath);
            if (fileInfo.Length >= ManifestConstants.LargeFileProgressThresholdBytes)
            {
                ReportLargeFileHashProgress(
                    entry.RelativePath,
                    fileInfo.Length,
                    currentIndex,
                    totalEntries,
                    progressNotificationId);
            }

            var computedHash = await hashProvider.ComputeFileHashAsync(sourcePath, cancellationToken);
            var isAuthoritativeMatch = IsAuthoritativeMatch(entry, fileInfo.Length, computedHash);

            if (entry.Size > 0 && !isAuthoritativeMatch)
            {
                logger.LogWarning(
                    "Local file ({ActualSize} bytes, hash: {ActualHash}) for {RelativePath} differs from catalog (size: {ExpectedSize}, hash: {ExpectedHash}). Attaching locally computed hash and size. Source: {SourcePath}",
                    fileInfo.Length,
                    computedHash,
                    entry.RelativePath,
                    entry.Size,
                    entry.Sha256,
                    sourcePath);
            }

            var isExecutable = ExecutableFileClassifier.RequiresExecutePermission(entry.RelativePath, sourcePath);

            await builder.AddGameInstallationFileAsync(
                entry.RelativePath,
                sourcePath,
                isExecutable,
                permissions: null,
                hash: computedHash,
                size: fileInfo.Length,
                isRequired: entry.IsRequired);

            return isAuthoritativeMatch ? AuthoritativeFileStatus.AddedMatching : AuthoritativeFileStatus.AddedDiffering;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                ex,
                "Failed to add authoritative vanilla file {RelativePath} to manifest",
                entry.RelativePath);
            return AuthoritativeFileStatus.Skipped;
        }
    }

    private void ReportLargeFileHashProgress(
        string relativePath,
        long length,
        int currentIndex,
        int totalEntries,
        Guid? progressNotificationId)
    {
        var sizeMb = length / (1024.0 * 1024.0);
        var percent = (double)currentIndex / totalEntries * 100;
        logger.LogInformation(
            "Calculating SHA-256 for {RelativePath} ({SizeMB:F1} MB) [{Current}/{Total} ({Percent:F0}%)]...",
            relativePath,
            sizeMb,
            currentIndex,
            totalEntries,
            percent);

        if (progressNotificationId.HasValue)
        {
            notificationService?.Update(
                progressNotificationId.Value,
                $"Calculating SHA-256 for {relativePath} ({sizeMb:F1} MB) - {currentIndex}/{totalEntries} ({percent:F0}%)",
                ManifestConstants.IndexingNotificationTitle);
        }
    }

    /// <summary>
    /// Adds GeneralsOnline client-specific files (executable, configs, DLLs) to the manifest.
    /// Hash validation is relaxed for auto-updated GeneralsOnline executables.
    /// </summary>
    /// <param name="builder">The manifest builder.</param>
    /// <param name="installationPath">The installation path.</param>
    /// <param name="gameType">The game type.</param>
    /// <param name="executablePath">The full path to the GeneralsOnline executable.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddGeneralsOnlineClientFilesToManifest(IContentManifestBuilder builder, string installationPath, GameType gameType, string executablePath)
    {
        try
        {
            if (File.Exists(executablePath))
            {
                var executableFileName = Path.GetFileName(executablePath);
                await builder.AddGameInstallationFileAsync(executableFileName, executablePath, isExecutable: true);
                logger.LogDebug("Added GeneralsOnline executable {ExecutableName} to GameClient manifest", executableFileName);
            }
            else
            {
                logger.LogError("GeneralsOnline executable not found at {ExecutablePath} - GameClient manifest will be incomplete", executablePath);
                throw new FileNotFoundException($"GeneralsOnline executable not found at: {executablePath}", executablePath);
            }

            // Add GeneralsOnline-specific DLLs if present
            // These are required for GeneralsOnline portable installation
            var generalsOnlineDlls = GameClientConstants.GeneralsOnlineDlls;

            var executableDirectory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrEmpty(executableDirectory))
            {
                foreach (var dllName in generalsOnlineDlls)
                {
                    var dllPath = Path.Combine(executableDirectory, dllName);
                    if (File.Exists(dllPath))
                    {
                        await builder.AddGameInstallationFileAsync(dllName, dllPath);
                        logger.LogDebug("Added GeneralsOnline DLL {DllName} to GameClient manifest", dllName);
                    }
                }
            }

            // Add GeneralsOnline-specific configuration files
            var configFiles = GameClientConstants.ConfigFiles;

            foreach (var configFile in configFiles)
            {
                var configPath = Path.Combine(installationPath, configFile);
                if (File.Exists(configPath))
                {
                    await builder.AddGameInstallationFileAsync(configFile, configPath);
                    logger.LogDebug("Added GeneralsOnline config file {ConfigFile} to GameClient manifest", configFile);
                }
            }

            // Add GeneralsOnline data directory if present (contains portable installation files)
            var goDataDir = Path.Combine(installationPath, "GeneralsOnlineGameData");
            if (Directory.Exists(goDataDir))
            {
                // Add GOSplash.bmp (splash screen)
                var splashPath = Path.Combine(goDataDir, "GOSplash.bmp");
                if (File.Exists(splashPath))
                {
                    await builder.AddGameInstallationFileAsync("GeneralsOnlineGameData/GOSplash.bmp", splashPath);
                    logger.LogDebug("Added GeneralsOnline splash screen to manifest");
                }

                // Add MapCacheGO.ini (map cache configuration)
                var mapCachePath = Path.Combine(goDataDir, "MapCacheGO.ini");
                if (File.Exists(mapCachePath))
                {
                    await builder.AddGameInstallationFileAsync("GeneralsOnlineGameData/MapCacheGO.ini", mapCachePath);
                    logger.LogDebug("Added GeneralsOnline map cache configuration to manifest");
                }
            }

            // Add Maps directory if present (GeneralsOnline-specific maps)
            // NOTE: Maps are optional - not all installations include them
            var mapsDir = Path.Combine(installationPath, "Maps");
            var mapCount = 0;
            if (Directory.Exists(mapsDir))
            {
                try
                {
                    var mapFolders = await Task.Run(() => Directory.GetDirectories(mapsDir, "[GO]*", SearchOption.TopDirectoryOnly));
                    foreach (var mapFolder in mapFolders)
                    {
                        var mapFolderName = Path.GetFileName(mapFolder);
                        var mapFiles = await Task.Run(() => Directory.GetFiles(mapFolder, "*.*", SearchOption.AllDirectories));
                        foreach (var mapFile in mapFiles)
                        {
                            var relativePath = Path.Combine("Maps", mapFolderName, Path.GetFileName(mapFile));
                            await builder.AddGameInstallationFileAsync(relativePath, mapFile);
                            mapCount++;
                        }
                    }

                    if (mapCount > 0)
                    {
                        logger.LogDebug("Added {MapCount} GeneralsOnline map files to manifest", mapCount);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to add GeneralsOnline maps from {MapsDir}", mapsDir);
                }
            }

            logger.LogInformation(
                "Added GeneralsOnline client files to manifest for {GameType}: executable + {DllCount} DLLs + {ConfigCount} configs + {MapCount} maps",
                gameType,
                generalsOnlineDlls.Count(dll => File.Exists(Path.Combine(executableDirectory ?? string.Empty, dll))),
                configFiles.Count(cfg => File.Exists(Path.Combine(installationPath, cfg))),
                mapCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding GeneralsOnline client files to manifest");
            throw;
        }
    }

    /// <summary>
    /// Adds client-specific files (executable, configs, DLLs) to the manifest.
    /// </summary>
    /// <param name="builder">The manifest builder.</param>
    /// <param name="installationPath">The installation path.</param>
    /// <param name="gameType">The game type.</param>
    /// <param name="executablePath">The full path to the game executable.</param>
    /// <param name="publisherName">The publisher name (for publisher-specific logic).</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddClientFilesToManifest(IContentManifestBuilder builder, string installationPath, GameType gameType, string executablePath, string publisherName)
    {
        try
        {
            // Add the game executable first (required for mixed installations)
            var generalsExeInInstall = Path.Combine(installationPath, GameClientConstants.GeneralsExecutable);
            var isDatExecutable = Path.GetFileName(executablePath).Equals(GameClientConstants.SteamGameDatExecutable, StringComparison.OrdinalIgnoreCase) ||
                                  executablePath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

            if (isDatExecutable && File.Exists(generalsExeInInstall))
            {
                var generalsFileName = Path.GetFileName(generalsExeInInstall);
                var generalsSourcePath = ResolveSourcePathWithBackup(generalsExeInInstall, generalsFileName);
                await builder.AddGameInstallationFileAsync(generalsFileName, generalsSourcePath, isExecutable: true);

                var datFileName = Path.GetFileName(executablePath);
                var datSourcePath = ResolveSourcePathWithBackup(executablePath, datFileName);
                await builder.AddGameInstallationFileAsync(datFileName, datSourcePath, isExecutable: false);
            }
            else if (File.Exists(executablePath))
            {
                var executableFileName = Path.GetFileName(executablePath);

                var sourcePath = ResolveSourcePathWithBackup(executablePath, executableFileName);
                await builder.AddGameInstallationFileAsync(executableFileName, sourcePath, isExecutable: true);
            }
            else
            {
                logger.LogError("Executable not found at {ExecutablePath} - GameClient manifest will be incomplete", executablePath);
                throw new FileNotFoundException($"Game executable not found at: {executablePath}", executablePath);
            }

            // Add required DLLs that might be next to the executable
            var requiredDlls = GameClientConstants.RequiredDlls;

            var executableDirectory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrEmpty(executableDirectory))
            {
                foreach (var dllName in requiredDlls)
                {
                    var dllPath = Path.Combine(executableDirectory, dllName);
                    if (File.Exists(dllPath))
                    {
                        await builder.AddGameInstallationFileAsync(dllName, dllPath);
                        logger.LogDebug("Added required DLL {DllName} to GameClient manifest", dllName);
                    }
                }

                // For EA App/Steam clients, also include all OTHER DLLs in the same directory
                // This ensures we don't miss any obfuscated or version-specific wrappers like P2XDLL.DLL
                if (publisherName == PublisherInfoConstants.Steam.Name || publisherName == PublisherInfoConstants.EaApp.Name)
                {
                    try
                    {
                        var allDlls = Directory.GetFiles(executableDirectory, "*.dll", SearchOption.TopDirectoryOnly);
                        foreach (var dllPath in allDlls)
                        {
                            var dllName = Path.GetFileName(dllPath);
                            if (!requiredDlls.Contains(dllName, StringComparer.OrdinalIgnoreCase))
                            {
                                await builder.AddGameInstallationFileAsync(dllName, dllPath);
                                logger.LogDebug("Added auxiliary DLL {DllName} (publisher-specific) to GameClient manifest", dllName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to collect auxiliary DLLs for {PublisherName} client", publisherName);
                    }
                }
            }

            // Add client-specific configuration files
            var configFiles = GameClientConstants.ConfigFiles;

            foreach (var configFile in configFiles)
            {
                var configPath = Path.Combine(installationPath, configFile);
                if (File.Exists(configPath))
                {
                    await builder.AddGameInstallationFileAsync(configFile, configPath);
                    logger.LogDebug("Added config file {ConfigFile} to GameClient manifest", configFile);
                }
            }

            // For Steam/EA installations, also add game.dat and Generals.dat as alternative executables
            // This allows launching without Steam integration or via specific entry points
            var gameDatPath = Path.Combine(installationPath, GameClientConstants.SteamGameDatExecutable);
            if (File.Exists(gameDatPath) &&
                !executablePath.EndsWith(GameClientConstants.SteamGameDatExecutable, StringComparison.OrdinalIgnoreCase) &&
                !(isDatExecutable && File.Exists(generalsExeInInstall)))
            {
                await builder.AddGameInstallationFileAsync(GameClientConstants.SteamGameDatExecutable, gameDatPath, isExecutable: false);
                logger.LogDebug("Added game.dat to GameClient manifest (non-executable, for Steam-free launch)");
            }

            var generalsDatPath = Path.Combine(installationPath, "Generals.dat");
            if (File.Exists(generalsDatPath) && !executablePath.EndsWith("Generals.dat", StringComparison.OrdinalIgnoreCase))
            {
                await builder.AddGameInstallationFileAsync("Generals.dat", generalsDatPath, isExecutable: false);
                logger.LogDebug("Added Generals.dat to GameClient manifest");
            }

            var gameDatExists = File.Exists(Path.Combine(installationPath, GameClientConstants.SteamGameDatExecutable));
            logger.LogInformation(
                "Added GameClient files to manifest for {GameType}: executable + {DllCount} DLLs + {ConfigCount} configs{GameDat}",
                gameType,
                requiredDlls.Count(dll => File.Exists(Path.Combine(executableDirectory ?? string.Empty, dll))),
                configFiles.Count(cfg => File.Exists(Path.Combine(installationPath, cfg))),
                gameDatExists ? " + game.dat" : string.Empty);

            // For modern installations using game.exe, ensure it's included correctly
            var gameExePath = Path.Combine(installationPath, GameClientConstants.GameExecutable);
            if (File.Exists(gameExePath) && !executablePath.EndsWith(GameClientConstants.GameExecutable, StringComparison.OrdinalIgnoreCase))
            {
                await builder.AddGameInstallationFileAsync(GameClientConstants.GameExecutable, gameExePath, isExecutable: true);
                logger.LogDebug("Added game.exe engine to GameClient manifest");
            }

            // Ensure steam_appid.txt is included if present (critical for Steam launch)
            var steamAppIdPath = Path.Combine(installationPath, "steam_appid.txt");
            if (File.Exists(steamAppIdPath))
            {
                await builder.AddGameInstallationFileAsync("steam_appid.txt", steamAppIdPath);
                logger.LogDebug("Added steam_appid.txt to GameClient manifest");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding client files to manifest");
            throw;
        }
    }

    /// <summary>
    /// Resolves authoritative CSV entries for the specified game type, version, and language.
    /// </summary>
    private async Task<IReadOnlyList<CsvCatalogEntry>> GetAuthoritativeEntriesAsync(
        GameType gameType,
        string version,
        string language,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCatalogInfo(gameType, version, out var catalogInfo))
        {
            logger.LogWarning("No authoritative CSV catalog configured for {GameType} version {Version}", gameType, version);
            return [];
        }

        if (csvResolver != null)
        {
            var resolved = await TryResolveAuthoritativeEntriesAsync(
                csvResolver,
                gameType,
                version,
                language,
                catalogInfo.FileName,
                catalogInfo.Sha256,
                cancellationToken);

            if (resolved != null)
            {
                return resolved;
            }
        }

        return LoadAuthoritativeEntriesFromFallback(gameType, language, catalogInfo.FileName);
    }

    private async Task<IReadOnlyList<CsvCatalogEntry>?> TryResolveAuthoritativeEntriesAsync(
        CsvResolver resolver,
        GameType gameType,
        string version,
        string language,
        string csvFileName,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        var gameTypeStr = gameType == GameType.ZeroHour ? CsvConstants.ZeroHourGameType : CsvConstants.GeneralsGameType;
        var csvRemoteUrl = gameType == GameType.ZeroHour ? CsvConstants.ZeroHourCsvUrl : CsvConstants.GeneralsCsvUrl;

        try
        {
            var searchResult = new ContentSearchResult
            {
                Id = string.Empty,
                Name = $"{gameTypeStr} {version} ({language})",
                Version = version,
                TargetGame = gameType,
                ContentType = ContentType.GameInstallation,
                SourceUrl = csvRemoteUrl,
                ResolverId = CsvConstants.ResolverId,
                ResolverMetadata =
                {
                    [CsvConstants.GameTypeMetadataKey] = gameTypeStr,
                    [CsvConstants.VersionMetadataKey] = version,
                    [CsvConstants.LanguageMetadataKey] = language,
                    [CsvConstants.CsvUrlMetadataKey] = csvFileName,
                    [CsvConstants.Sha256MetadataKey] = expectedSha256,
                },
            };

            var resolveResult = await resolver.ResolveAsync(searchResult, cancellationToken);
            if (resolveResult.Success && resolveResult.Data?.Files != null && resolveResult.Data.Files.Count > 0)
            {
                logger.LogDebug(
                    "Resolved {Count} authoritative files via CSV resolver for {GameType} v{Version} ({Language})",
                    resolveResult.Data.Files.Count,
                    gameType,
                    version,
                    language);

                return resolveResult.Data.Files.Select(f => new CsvCatalogEntry
                {
                    RelativePath = f.RelativePath,
                    Size = f.Size,
                    Sha256 = f.Hash,
                    GameType = gameTypeStr,
                    Language = language,
                    IsRequired = f.IsRequired,
                    DownloadUrl = f.DownloadUrl,
                }).ToList();
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Failed to resolve CSV catalog via HTTP for {GameType} ({Language}), falling back to local/embedded registry", gameType, language);
        }
        catch (TaskCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            logger.LogWarning(ex, "Timeout resolving CSV catalog via HTTP for {GameType} ({Language}), falling back to local/embedded registry", gameType, language);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to resolve CSV catalog via I/O for {GameType} ({Language}), falling back to local/embedded registry", gameType, language);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Failed to resolve CSV catalog via resolver for {GameType} ({Language}), falling back to local/embedded registry", gameType, language);
        }

        return null;
    }

    /// <summary>
    /// Loads authoritative CSV entries from embedded assembly assets or local registry files.
    /// </summary>
    private IReadOnlyList<CsvCatalogEntry> LoadAuthoritativeEntriesFromFallback(
        GameType gameType,
        string language,
        string csvFileName)
    {
        var gameTypeStr = gameType == GameType.ZeroHour ? CsvConstants.ZeroHourGameType : CsvConstants.GeneralsGameType;

        var embeddedEntries = TryLoadAuthoritativeEntriesFromEmbeddedResource(gameType, gameTypeStr, language, csvFileName);
        if (embeddedEntries.Count > 0)
        {
            return embeddedEntries;
        }

        return TryLoadAuthoritativeEntriesFromLocalDisk(gameType, gameTypeStr, language, csvFileName);
    }

    private IReadOnlyList<CsvCatalogEntry> TryLoadAuthoritativeEntriesFromEmbeddedResource(
        GameType gameType,
        string gameTypeStr,
        string language,
        string csvFileName)
    {
        // Try embedded resource from GenHub.Core
        try
        {
            var assembly = typeof(CsvConstants).Assembly;
            var resourceName = $"{CsvConstants.EmbeddedResourceNamespace}.{csvFileName}";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                using var csv = new CsvReader(reader, CsvConfig);
                var records = csv.GetRecords<CsvCatalogEntry>().ToList();
                var filtered = FilterEntriesByGameAndLanguage(records, gameTypeStr, language);
                if (filtered.Count > 0)
                {
                    logger.LogDebug("Loaded {Count} authoritative entries from embedded resource {Resource}", filtered.Count, resourceName);
                    return filtered;
                }
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "I/O failure loading authoritative CSV from embedded resource for {GameType}", gameType);
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "Format error loading authoritative CSV from embedded resource for {GameType}", gameType);
        }
        catch (CsvHelperException ex)
        {
            logger.LogWarning(ex, "CSV parse error loading authoritative CSV from embedded resource for {GameType}", gameType);
        }

        return [];
    }

    private IReadOnlyList<CsvCatalogEntry> TryLoadAuthoritativeEntriesFromLocalDisk(
        GameType gameType,
        string gameTypeStr,
        string language,
        string csvFileName)
    {
        // Try local disk path if running from repo or development tree
        try
        {
            var possiblePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "docs", CsvConstants.RegistryDocsFolder, csvFileName),
                Path.Combine(Directory.GetCurrentDirectory(), "docs", CsvConstants.RegistryDocsFolder, csvFileName),
                Path.Combine(AppContext.BaseDirectory, csvFileName),
            };

            foreach (var path in possiblePaths.Where(File.Exists))
            {
                using var reader = new StreamReader(path);
                using var csv = new CsvReader(reader, CsvConfig);
                var records = csv.GetRecords<CsvCatalogEntry>().ToList();
                var filtered = FilterEntriesByGameAndLanguage(records, gameTypeStr, language);
                if (filtered.Count > 0)
                {
                    logger.LogDebug("Loaded {Count} authoritative entries from local path {Path}", filtered.Count, path);
                    return filtered;
                }
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "I/O failure loading authoritative CSV from local disk for {GameType}", gameType);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied loading authoritative CSV from local disk for {GameType}", gameType);
        }
        catch (CsvHelperException ex)
        {
            logger.LogWarning(ex, "CSV parse error loading authoritative CSV from local disk for {GameType}", gameType);
        }

        return [];
    }

    /// <summary>
    /// Resolves the source path for a file, checking for a backup (.bak) version first.
    /// Rejects candidate backup files that are symbolic links or reparse points.
    /// </summary>
    private string ResolveSourcePathWithBackup(string filePath, string manifestFileName)
    {
        var backupPath = filePath + SteamConstants.BackupExtension;
        if (File.Exists(backupPath))
        {
            if (!IsReparsePoint(backupPath))
            {
                logger.LogInformation("Using backup file {Backup} as source for {File} in manifest", Path.GetFileName(backupPath), manifestFileName);
                return backupPath;
            }

            logger.LogWarning("Backup source {Backup} for {File} is a reparse point or symbolic link and will be skipped", Path.GetFileName(backupPath), manifestFileName);
        }

        var legacyBackupPath = filePath + FileTypes.LegacyBackupExtension;
        if (File.Exists(legacyBackupPath))
        {
            if (!IsReparsePoint(legacyBackupPath))
            {
                logger.LogInformation("Using backup file {Backup} as source for {File} in manifest", Path.GetFileName(legacyBackupPath), manifestFileName);
                return legacyBackupPath;
            }

            logger.LogWarning("Backup source {Backup} for {File} is a reparse point or symbolic link and will be skipped", Path.GetFileName(legacyBackupPath), manifestFileName);
        }

        return filePath;
    }
}
