using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
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
    IManifestIdService manifestIdService) : IManifestGenerationService
{
    private readonly ILogger<ManifestGenerationService> _logger = logger;
    private readonly IFileHashProvider _hashProvider = hashProvider;
    private readonly IManifestIdService _manifestIdService = manifestIdService;
    private int _fileCount = 0;
    private int _lastReportedCount = 0;

    /// <summary>
    /// Creates a manifest builder for a game installation.
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
        object? manifestVersion = null)
    {
        try
        {
            _logger.LogDebug(
                "Creating GameInstallation manifest for {GameType} at {GameInstallationPath}",
                gameType,
                gameInstallationPath);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, _hashProvider, _manifestIdService)
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

            _logger.LogInformation(
                "Created GameInstallation manifest for {InstallationType} {GameType} (Publisher: {PublisherName})",
                installationType,
                gameType,
                publisher.Name);

            return builder;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error creating GameInstallation manifest for {GameType} at {GameInstallationPath}",
                gameType,
                gameInstallationPath);
            throw;
        }
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
            _logger.LogDebug(
                "Creating {ContentType} manifest for {ContentName} at {ContentDirectory} (Publisher: {PublisherId})",
                contentType,
                contentName,
                contentDirectory,
                publisherId);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, _hashProvider, _manifestIdService)
                .WithBasicInfo(publisherId, contentName, manifestVersion)
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

            await Task.CompletedTask; // Make method async for consistency
            return builder;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error creating content manifest for {ContentName} at {ContentDirectory}",
                contentName,
                contentDirectory);
            throw;
        }
    }

    /// <summary>
    /// Creates a manifest builder for a standalone game version.
    /// </summary>
    /// <param name="gameDirectory">Path to the standalone game directory.</param>
    /// <param name="publisherId">The publisher identifier used to generate the manifest id.</param>
    /// <param name="gameName">Game version display name.</param>
    /// <param name="manifestVersion">Manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="executablePath">Path to the main executable.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateGameClientManifestAsync(
        string gameDirectory,
        string publisherId,
        string gameName,
        int manifestVersion = 0,
        string executablePath = "")
    {
        try
        {
            _logger.LogDebug(
                "Creating GameClient manifest for {GameName} at {GameDirectory}",
                gameName,
                gameDirectory);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, _hashProvider, _manifestIdService)
                .WithBasicInfo(publisherId, gameName, manifestVersion)
                .WithContentType(ContentType.GameClient, GameType.Generals); // TODO: Determine game type dynamically

            await Task.CompletedTask; // Make method async for consistency
            return builder;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error creating game version manifest for {GameName} at {GameDirectory}",
                gameName,
                gameDirectory);
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
            _logger.LogDebug("Creating content bundle {BundleName} version {ManifestVersion}", bundleName, manifestVersion);

            var bundleId = ManifestId.Create($"{manifestVersion}.0.bundle.{bundleName.ToLowerInvariant().Replace(" ", string.Empty)}");

            var bundle = new ContentBundle
            {
                Id = bundleId,
                Name = bundleName,
                Version = manifestVersion.ToString(),
                Items = items.ToList(),
            };

            return await Task.FromResult(bundle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating content bundle {BundleName}", bundleName);
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
            _logger.LogDebug(
                "Creating publisher referral {ReferralName} for {TargetPublisherId}",
                referralName,
                targetPublisherId);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, _hashProvider, _manifestIdService)
                .WithBasicInfo(publisherId, referralName, manifestVersion)
                .WithContentType(ContentType.PublisherReferral, GameType.Generals) // Default to Generals
                .WithMetadata(description);

            return await Task.FromResult(builder.Build());
        }
        catch (Exception ex)
        {
            _logger.LogError(
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
            _logger.LogDebug(
                "Creating content referral {ReferralName} for {TargetContentId}",
                referralName,
                targetContentId);

            var builderLogger = NullLogger<ContentManifestBuilder>.Instance;
            var builder = new ContentManifestBuilder(builderLogger, _hashProvider, _manifestIdService)
                .WithBasicInfo(publisherId, referralName, manifestVersion)
                .WithContentType(ContentType.ContentReferral, GameType.Generals) // Default to Generals
                .WithMetadata(description);

            return await Task.FromResult(builder.Build());
        }
        catch (Exception ex)
        {
            _logger.LogError(
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
            _logger.LogDebug("Saving manifest {ManifestId} to {OutputPath}", manifest.Id, outputPath);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            await using var stream = File.Create(outputPath);
            await JsonSerializer.SerializeAsync(stream, manifest, options);

            _logger.LogInformation("Manifest {ManifestId} saved to {OutputPath}", manifest.Id, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save manifest {ManifestId} to {OutputPath}", manifest.Id, outputPath);
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
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    public async Task<IContentManifestBuilder> CreateGameClientManifestAsync(
        string installationPath,
        GameType gameType,
        string clientName,
        string clientVersion,
        string executablePath)
    {
        try
        {
            _logger.LogDebug("Creating GameClient manifest for {ClientName} at {InstallationPath}", clientName, installationPath);

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

            // Determine publisher name using user-friendly display format matching InstallationTypeDisplayConverter
            var publisherName = clientName.ToLowerInvariant().Contains("steam") ? "Steam" :
                                clientName.ToLowerInvariant().Contains("ea") ? "EA App" :
                                "Retail Installation";
            var publisher = new PublisherInfo { Name = publisherName };
            var contentName = gameType.ToString().ToLowerInvariant() + "-client";
            var builder = new ContentManifestBuilder(builderLogger, _hashProvider, _manifestIdService)
                .WithBasicInfo(publisher, contentName, 1)
                .WithContentType(ContentType.GameClient, gameType);

            await AddClientFilesToManifest(builder, installationPath, gameType, executablePath);

            _logger.LogInformation("Created GameClient manifest for {ClientName} (Publisher: {PublisherName})", clientName, publisher.Name);

            return builder;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating GameClient manifest for {ClientName} at {InstallationPath}", clientName, installationPath);
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
            _logger.LogDebug("Creating GeneralsOnline client manifest for {ClientName} at {InstallationPath}", clientName, installationPath);

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
            var contentName = $"{gameType.ToString().ToLowerInvariant()}-{executableFileName}";
            var builder = new ContentManifestBuilder(builderLogger, _hashProvider, _manifestIdService)
                .WithBasicInfo(publisher, contentName, 1)
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

            _logger.LogInformation("Created GeneralsOnline client manifest for {ClientName} (Publisher: Generals Online)", clientName);

            return builder;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating GeneralsOnline client manifest for {ClientName} at {InstallationPath}", clientName, installationPath);
            throw;
        }
    }

    /// <summary>
    /// Determines if a directory should be skipped during manifest generation.
    /// </summary>
    /// <param name="directoryName">The directory name to check.</param>
    /// <returns>True if the directory should be skipped, false otherwise.</returns>
    private static bool ShouldSkipDirectory(string directoryName)
    {
        // Directories that are definitely not needed for game execution
        var skipDirectories = new[]
        {
            "RedistInstallers", // Redistributable installers (VC++ runtime, etc.)
            "Manuals",           // PDF/HTML game manuals
            "launcher",          // Third-party launcher files (not needed in isolated workspace)
            ".GenLauncherFolder", // GenTool launcher-specific folder
        };

        return skipDirectories.Contains(directoryName, StringComparer.OrdinalIgnoreCase);
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
            _lastReportedCount = 0;

            _logger.LogInformation("Starting manifest generation for {GameType} at {InstallationPath}", gameType, installationPath);

            // Add essential executable files
            var executableName = gameType == GameType.Generals ? GameClientConstants.GeneralsExecutable : GameClientConstants.ZeroHourExecutable;
            var executablePath = Path.Combine(installationPath, executableName);

            if (File.Exists(executablePath))
            {
                await builder.AddGameInstallationFileAsync(executableName, executablePath, true);
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
            };

            foreach (var pattern in commonFiles)
            {
                try
                {
                    var files = Directory.GetFiles(installationPath, pattern, SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        var relativePath = Path.GetFileName(file);
                        await builder.AddGameInstallationFileAsync(relativePath, file);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enumerate files with pattern {Pattern} at {InstallationPath}", pattern, installationPath);
                }
            }

            // Add all subdirectories except known non-game directories
            await AddAllSubdirectoriesAsync(builder, installationPath);

            _logger.LogInformation("Completed manifest generation for {GameType}: {TotalFiles} files added", gameType, _fileCount);
            _logger.LogDebug("Added game files to manifest for {GameType} at {InstallationPath}", gameType, installationPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding game files to manifest");
        }
    }

    /// <summary>
    /// Adds all subdirectories to the manifest, excluding known non-game directories.
    /// </summary>
    /// <param name="builder">The manifest builder.</param>
    /// <param name="installationPath">The installation path.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddAllSubdirectoriesAsync(IContentManifestBuilder builder, string installationPath)
    {
        try
        {
            var allDirectories = Directory.GetDirectories(installationPath, "*", SearchOption.TopDirectoryOnly);
            _logger.LogDebug("Found {DirectoryCount} subdirectories to process in {InstallationPath}", allDirectories.Length, installationPath);

            foreach (var dirPath in allDirectories)
            {
                var dirName = Path.GetFileName(dirPath);

                // Skip directories that are definitely not needed for game execution
                if (ShouldSkipDirectory(dirName))
                {
                    _logger.LogDebug("Skipping non-game directory: {DirectoryName}", dirName);
                    continue;
                }

                try
                {
                    await AddDirectoryFilesRecursivelyAsync(builder, installationPath, dirPath);
                    _logger.LogDebug("Successfully added files from directory: {DirectoryName}", dirName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add files from directory {Directory}", dirName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate subdirectories in {InstallationPath}", installationPath);
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
                await builder.AddGameInstallationFileAsync(relativePath, file);

                // Report progress every 50 files
                _fileCount++;
                if (_fileCount - _lastReportedCount >= 50)
                {
                    _logger.LogInformation("Scanning game files: {FileCount} files processed...", _fileCount);
                    _lastReportedCount = _fileCount;
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
            _logger.LogWarning(ex, "Failed to recursively add files from {DirectoryPath}", directoryPath);
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
            // CRITICAL: Add the GeneralsOnline executable first (required for mixed installations)
            if (File.Exists(executablePath))
            {
                var executableFileName = Path.GetFileName(executablePath);
                await builder.AddGameInstallationFileAsync(executableFileName, executablePath, isExecutable: true);
                _logger.LogDebug("Added GeneralsOnline executable {ExecutableName} to GameClient manifest", executableFileName);
            }
            else
            {
                _logger.LogError("GeneralsOnline executable not found at {ExecutablePath} - GameClient manifest will be incomplete", executablePath);
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
                        _logger.LogDebug("Added GeneralsOnline DLL {DllName} to GameClient manifest", dllName);
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
                    _logger.LogDebug("Added GeneralsOnline config file {ConfigFile} to GameClient manifest", configFile);
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
                    _logger.LogDebug("Added GeneralsOnline splash screen to manifest");
                }

                // Add MapCacheGO.ini (map cache configuration)
                var mapCachePath = Path.Combine(goDataDir, "MapCacheGO.ini");
                if (File.Exists(mapCachePath))
                {
                    await builder.AddGameInstallationFileAsync("GeneralsOnlineGameData/MapCacheGO.ini", mapCachePath);
                    _logger.LogDebug("Added GeneralsOnline map cache configuration to manifest");
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
                        _logger.LogDebug("Added {MapCount} GeneralsOnline map files to manifest", mapCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add GeneralsOnline maps from {MapsDir}", mapsDir);
                }
            }

            _logger.LogInformation(
                "Added GeneralsOnline client files to manifest for {GameType}: executable + {DllCount} DLLs + {ConfigCount} configs + {MapCount} maps",
                gameType,
                generalsOnlineDlls.Count(dll => File.Exists(Path.Combine(executableDirectory ?? string.Empty, dll))),
                configFiles.Count(cfg => File.Exists(Path.Combine(installationPath, cfg))),
                mapCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding GeneralsOnline client files to manifest");
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
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddClientFilesToManifest(IContentManifestBuilder builder, string installationPath, GameType gameType, string executablePath)
    {
        try
        {
            // Add the game executable first (required for mixed installations)
            if (File.Exists(executablePath))
            {
                var executableFileName = Path.GetFileName(executablePath);
                await builder.AddGameInstallationFileAsync(executableFileName, executablePath, isExecutable: true);
                _logger.LogDebug("Added executable {ExecutableName} to GameClient manifest", executableFileName);
            }
            else
            {
                _logger.LogError("Executable not found at {ExecutablePath} - GameClient manifest will be incomplete", executablePath);
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
                        _logger.LogDebug("Added required DLL {DllName} to GameClient manifest", dllName);
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
                    _logger.LogDebug("Added config file {ConfigFile} to GameClient manifest", configFile);
                }
            }

            _logger.LogInformation(
                "Added GameClient files to manifest for {GameType}: executable + {DllCount} DLLs + {ConfigCount} configs",
                gameType,
                requiredDlls.Count(dll => File.Exists(Path.Combine(executableDirectory ?? string.Empty, dll))),
                configFiles.Count(cfg => File.Exists(Path.Combine(installationPath, cfg))));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding client files to manifest");
            throw;
        }
    }
}
