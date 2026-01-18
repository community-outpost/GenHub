using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Features.Manifest;

/// <summary>
/// Service for generating content manifests from game installations and content packages.
/// </summary>
/// <remarks>
/// Provides methods to create <see cref="ContentManifest"/> objects for different content types
/// including GameInstallation and GameClient manifests with proper metadata and file references.
/// </remarks>
public class ManifestGenerationService(
    ILogger<ManifestGenerationService> logger,
    IFileHashProvider hashProvider,
    IManifestIdService manifestIdService,
    IDownloadService downloadService,
    IConfigurationProviderService configurationProvider) : IManifestGenerationService
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] SupportedLanguages = ["EN", "DE", "FR", "ES", "IT", "KO", "PL", "PT-BR", "ZH-CN", "ZH-TW"];

    private int _fileCount = 0;

    /// <summary>
    /// Creates a manifest builder for a game installation with string version normalization.
    /// </summary>
    /// <param name="gameInstallationPath">Path to the game installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="installationType">The installation type (Steam, EaApp).</param>
    /// <param name="manifestVersion">The manifest version (e.g., "1.08", "1.04", or integer like 0, 1, 2). If null, defaults to 0.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateGameInstallationManifestAsync(
        string gameInstallationPath,
        GameType gameType,
        GameInstallationType installationType,
        string? manifestVersion = null)
    {
        try
        {
            logger.LogDebug(
                "Creating GameInstallation manifest for {GameType} at {GameInstallationPath}",
                gameType,
                gameInstallationPath);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, hashProvider, manifestIdService, downloadService, configurationProvider)
                .WithBasicInfo(installationType, gameType, manifestVersion)
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
            await AddGameFilesToManifest(builder, gameInstallationPath, gameType);

            logger.LogInformation(
                "Created GameInstallation manifest for {InstallationType} {GameType} (Publisher: {PublisherName})",
                installationType,
                gameType,
                publisher.Name);

            return builder;
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
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateGameInstallationManifestAsync(
        string gameInstallationPath,
        GameType gameType,
        GameInstallationType installationType,
        int manifestVersion = 0)
    {
        return await CreateGameInstallationManifestAsync(gameInstallationPath, gameType, installationType, manifestVersion.ToString());
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

            var options = _jsonSerializerOptions;

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
                // Legacy/Fallback detection
                var publisherName = clientName.Contains("steam", StringComparison.InvariantCultureIgnoreCase) ? PublisherInfoConstants.Steam.Name :
                                    clientName.Contains("ea", StringComparison.InvariantCultureIgnoreCase) ? PublisherInfoConstants.EaApp.Name :
                                    PublisherInfoConstants.Retail.Name;
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

            // Create unique manifest name based on executable to distinguish variants (30Hz, 60Hz, standard)
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
    /// Determines if a file should be skipped during manifest generation.
    /// </summary>
    private static bool ShouldSkipFile(string relativePath)
    {
        return relativePath.EndsWith(SteamConstants.BackupExtension, StringComparison.OrdinalIgnoreCase) ||
               relativePath.EndsWith(SteamConstants.ProxyLauncherFileName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds game files to a manifest builder.
    /// </summary>
    /// <param name="builder">The manifest builder.</param>
    /// <param name="installationPath">The installation path.</param>
    /// <param name="gameType">The game type.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Scans the game installation directory and adds all files to the manifest,
    /// excluding known non-game directories (RedistInstallers, Manuals, etc.).
    /// This will be used for manual content packaging of game installations until we have CSV feature implemented that will act as a source of truth to generate the manifest.
    /// </remarks>
    private async Task AddGameFilesToManifest(IContentManifestBuilder builder, string installationPath, GameType gameType)
    {
        try
        {
            // Reset file counter for this manifest generation
            _fileCount = 0;

            logger.LogInformation("Starting manifest generation for {GameType} at {InstallationPath}", gameType, installationPath);

            // Add essential executable files
            var executableName = gameType == GameType.Generals ? GameClientConstants.GeneralsExecutable : GameClientConstants.ZeroHourExecutable;
            var executablePath = Path.Combine(installationPath, executableName);

            if (File.Exists(executablePath))
            {
                var sourcePath = ResolveSourcePathWithBackup(executablePath, executableName);
                await builder.AddGameInstallationFileAsync(executableName, sourcePath, isExecutable: true);
            }

            // Add common game files including DLLs and .big archives which are required for the game to run
            var commonFiles = new[]
            {
                "*.exe",
                "*.dll",
                "*.dat",
                "*.ini",
                "*.cfg",
                "*.big", // Essential: Archive files containing game assets, textures, audio, etc.
                "*.txt", // Essential: Text files like steam_appid.txt
            };

            foreach (var pattern in commonFiles)
            {
                try
                {
                    var files = Directory.GetFiles(installationPath, pattern, SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        var relativePath = Path.GetFileName(file);

                        // Skip backup files and the proxy launcher itself
                        if (ShouldSkipFile(relativePath))
                        {
                            continue;
                        }

                        // Skip the main executable as it was already added with backup handling
                        if (relativePath.Equals(executableName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        await builder.AddGameInstallationFileAsync(relativePath, file);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to enumerate files with pattern {Pattern} at {InstallationPath}", pattern, installationPath);
                }
            }

            // Add all subdirectories except known non-game directories
            // PRIORITY: Use CSV-based manifest generation if available
            var csvAdded = await AddFilesFromCsvAsync(builder, installationPath, gameType);

            logger.LogInformation("Completed manifest generation for {GameType}: {TotalFiles} files added", gameType, _fileCount);
            logger.LogDebug("Added game files to manifest for {GameType} at {InstallationPath}", gameType, installationPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding game files to manifest");
        }
    }

    /// <summary>
    /// Recursively adds all files from a directory to the manifest.
    /// </summary>
    /// <param name="builder">The manifest builder.</param>
    /// <param name="installationPath">The root installation path.</param>
    /// <param name="directoryPath">The directory to scan.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddDirectoryFilesRecursivelyAsync(IContentManifestBuilder builder, string installationPath, string directoryPath)
    {
        try
        {
            // Add all files in this directory (run synchronously on background thread)
            var files = await Task.Run(() => Directory.GetFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly));
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(installationPath, file);

                // Skip backup files and the proxy launcher itself
                if (ShouldSkipFile(relativePath))
                {
                    continue;
                }

                await builder.AddGameInstallationFileAsync(relativePath, file);

                // Report progress every 50 files
                _fileCount++;
                if (_fileCount % 50 == 0)
                {
                    logger.LogInformation("Scanning game files: {FileCount} files processed...", _fileCount);
                }
            }

            // Recursively process subdirectories (run synchronously on background thread)
            var subdirectories = await Task.Run(() => Directory.GetDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly));
            foreach (var subdir in subdirectories)
            {
                await AddDirectoryFilesRecursivelyAsync(builder, installationPath, subdir);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to recursively add files from {DirectoryPath}", directoryPath);
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
    /// <param name="publisherName">The publisher name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddClientFilesToManifest(IContentManifestBuilder builder, string installationPath, GameType gameType, string executablePath, string publisherName)
    {
        try
        {
            // Add the game executable first (required for mixed installations)
            if (File.Exists(executablePath))
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
            if (File.Exists(gameDatPath) && !executablePath.EndsWith(GameClientConstants.SteamGameDatExecutable, StringComparison.OrdinalIgnoreCase))
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
    /// Adds files to the manifest using a CSV source of truth.
    /// </summary>
    private async Task<bool> AddFilesFromCsvAsync(IContentManifestBuilder builder, string installationPath, GameType gameType)
    {
        try
        {
            var csvResourceName = gameType == GameType.Generals ? "GenHub.Core.Assets.Manifests.generals.csv" : "GenHub.Core.Assets.Manifests.zerohour.csv";
            var assembly = Assembly.Load("GenHub.Core");
            using var stream = assembly.GetManifestResourceStream(csvResourceName);

            if (stream == null)
            {
                logger.LogWarning("Embedded resource {ResourceName} not found", csvResourceName);
                return false;
            }

            using var reader = new StreamReader(stream);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
            };
            using var csv = new CsvReader(reader, config);

            var records = csv.GetRecords<ManifestFileEntry>().ToList();
            var installationFiles = Directory.GetFiles(installationPath, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(installationPath, f))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            logger.LogInformation("Processing {Count} entries from CSV for {GameType}", records.Count, gameType);

            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.RelativePath)) continue;

                var finalPath = record.RelativePath;
                var found = false;

                // 1. Check if the exact file exists
                if (installationFiles.Contains(finalPath))
                {
                    found = true;
                }

                // 2. If it's language-specific and exact file NOT found, try to resolve other language variants
                else if (!string.IsNullOrEmpty(record.Language))
                {
                    // Attempt to find any language-pivoted version of this file
                    foreach (var lang in SupportedLanguages)
                    {
                        var pivotedPath = record.RelativePath.Replace(record.Language, lang, StringComparison.OrdinalIgnoreCase);
                        if (installationFiles.Contains(pivotedPath))
                        {
                            finalPath = pivotedPath;
                            found = true;
                            logger.LogDebug("Resolved language file {Original} to {Pivoted}", record.RelativePath, pivotedPath);
                            break;
                        }
                    }
                }

                if (found)
                {
                    var fullPath = Path.Combine(installationPath, finalPath);

                    fullPath = ResolveSourcePathWithBackup(fullPath, finalPath);

                    var isExecutable = finalPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                       finalPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);

                    await builder.AddGameInstallationFileAsync(finalPath, fullPath, isExecutable);
                    _fileCount++;
                }
                else
                {
                    // If it's a core file (no language), log as missing
                    if (string.IsNullOrEmpty(record.Language))
                    {
                        logger.LogDebug("Core file {File} missing from installation", finalPath);
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add files from CSV for {GameType}", gameType);
            return false;
        }
    }

    /// <summary>
    /// Resolves the source path for a file, checking for a backup (.bak) version first.
    /// </summary>
    private string ResolveSourcePathWithBackup(string filePath, string manifestFileName)
    {
        var backupPath = filePath + SteamConstants.BackupExtension;
        if (File.Exists(backupPath))
        {
            logger.LogInformation("Using backup file {Backup} as source for {File} in manifest", Path.GetFileName(backupPath), manifestFileName);
            return backupPath;
        }

        return filePath;
    }

    /// <summary>
    /// Represents a file entry in the manifest CSV.
    /// </summary>
    private class ManifestFileEntry
    {
        /// <summary>
        /// Gets or sets the relative path of the file.
        /// </summary>
        public string RelativePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the language of the file (optional).
        /// </summary>
        public string Language { get; set; } = string.Empty;
    }
}
