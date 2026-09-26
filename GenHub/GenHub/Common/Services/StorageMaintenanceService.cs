using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services;

/// <summary>
/// Performs automated storage maintenance, cache consolidation, and legacy directory cleanup across GenHub data roots.
/// </summary>
/// <param name="configurationProvider">The configuration provider resolving application data paths.</param>
/// <param name="logger">Optional logger for diagnostics.</param>
public class StorageMaintenanceService(
    IConfigurationProviderService configurationProvider,
    ILogger<StorageMaintenanceService>? logger = null) : IStorageMaintenanceService
{
    private readonly IConfigurationProviderService _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
    private readonly ILogger<StorageMaintenanceService>? _logger = logger;

    /// <inheritdoc />
    public Task RunMaintenanceAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ExecuteMaintenance(cancellationToken), cancellationToken);
    }

    private void ExecuteMaintenance(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var dataRoot = _configurationProvider.GetApplicationDataPath();
            if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot))
            {
                return;
            }

            MigratePublisherStudioSettings(dataRoot);
            MigrateImageCache(dataRoot);
            MigrateModBuilderSampleCache(dataRoot);
            CleanOrphanedExecutables(dataRoot);
            CleanEmptyGhostDirectories(dataRoot);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unexpected error during storage maintenance execution");
        }
    }

    private void MigratePublisherStudioSettings(string dataRoot)
    {
        try
        {
            // Legacy path had AppConstants.AppName ("GenHub") nested inside dataRoot: dataRoot/GenHub/publisher_studio_settings.json
            var legacyFile = Path.Combine(dataRoot, AppConstants.AppName, PublisherStudioConstants.SettingsFileName);
            var targetDir = Path.Combine(dataRoot, PublisherStudioConstants.StudioFolderName);
            var targetFile = Path.Combine(targetDir, PublisherStudioConstants.SettingsFileName);

            if (File.Exists(legacyFile))
            {
                Directory.CreateDirectory(targetDir);
                if (!File.Exists(targetFile))
                {
                    File.Move(legacyFile, targetFile);
                    _logger?.LogInformation("Migrated legacy publisher studio settings to {TargetFile}", targetFile);
                }
                else
                {
                    File.Delete(legacyFile);
                    _logger?.LogInformation("Removed redundant legacy publisher studio settings file at {LegacyFile}", legacyFile);
                }
            }

            var legacyDir = Path.Combine(dataRoot, AppConstants.AppName);
            if (Directory.Exists(legacyDir) && !Directory.EnumerateFileSystemEntries(legacyDir).Any())
            {
                Directory.Delete(legacyDir, recursive: false);
                _logger?.LogInformation("Cleaned up empty legacy directory {LegacyDir}", legacyDir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to migrate legacy publisher studio settings");
        }
    }

    private void MigrateImageCache(string dataRoot)
    {
        try
        {
            var legacyDir = Path.Combine(dataRoot, "Images");
            if (!Directory.Exists(legacyDir))
            {
                return;
            }

            var targetDir = Path.Combine(dataRoot, DirectoryNames.Cache, "Images");
            Directory.CreateDirectory(targetDir);

            foreach (var filePath in Directory.GetFiles(legacyDir, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var fileName = Path.GetFileName(filePath);
                    var destPath = Path.Combine(targetDir, fileName);
                    if (!File.Exists(destPath))
                    {
                        File.Move(filePath, destPath);
                    }
                    else
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger?.LogDebug(ex, "Failed to migrate image cache file {File}", filePath);
                }
            }

            if (!Directory.EnumerateFileSystemEntries(legacyDir).Any())
            {
                Directory.Delete(legacyDir, recursive: false);
                _logger?.LogInformation("Migrated legacy Images cache to {TargetDir} and removed legacy folder", targetDir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to migrate legacy Images cache directory");
        }
    }

    private void MigrateModBuilderSampleCache(string dataRoot)
    {
        try
        {
            var legacyDir = Path.Combine(dataRoot, ModBuilderConstants.SampleCacheDirName);
            if (!Directory.Exists(legacyDir))
            {
                return;
            }

            var targetDir = Path.Combine(dataRoot, DirectoryNames.Cache, ModBuilderConstants.SampleCacheDirName);
            Directory.CreateDirectory(targetDir);

            foreach (var filePath in Directory.GetFiles(legacyDir, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var fileName = Path.GetFileName(filePath);
                    var destPath = Path.Combine(targetDir, fileName);
                    if (!File.Exists(destPath))
                    {
                        File.Move(filePath, destPath);
                    }
                    else
                    {
                        File.Delete(filePath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger?.LogDebug(ex, "Failed to migrate modbuilder sample cache file {File}", filePath);
                }
            }

            if (!Directory.EnumerateFileSystemEntries(legacyDir).Any())
            {
                Directory.Delete(legacyDir, recursive: false);
                _logger?.LogInformation("Migrated legacy ModBuilderSampleCache to {TargetDir} and removed legacy folder", targetDir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to migrate legacy ModBuilderSampleCache directory");
        }
    }

    private void CleanOrphanedExecutables(string dataRoot)
    {
        try
        {
            var legacyExe = Path.Combine(dataRoot, "GenHub.Windows.exe");
            var primaryExe = Path.Combine(dataRoot, "GenHub.exe");
            var currentDir = Path.Combine(dataRoot, "current");

            // Only delete GenHub.Windows.exe if this root is verified to be a Velopack root
            // with a current GenHub.exe or current/ directory, ensuring we do not touch
            // any active standalone build root.
            if (File.Exists(legacyExe) && (File.Exists(primaryExe) || Directory.Exists(currentDir)))
            {
                File.Delete(legacyExe);
                _logger?.LogInformation("Removed obsolete legacy launcher executable {LegacyExe}", legacyExe);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to remove obsolete legacy executable");
        }
    }

    private void CleanEmptyGhostDirectories(string dataRoot)
    {
        string[] candidates =
        [
            DirectoryNames.Backups,
            MapManagerConstants.MapPacksSubdirectoryName,
            ActionSetConstants.Paths.SubActionSetMarkers,
        ];

        foreach (var name in candidates)
        {
            try
            {
                var dirPath = Path.Combine(dataRoot, name);
                if (Directory.Exists(dirPath) && !Directory.EnumerateFileSystemEntries(dirPath).Any())
                {
                    Directory.Delete(dirPath, recursive: false);
                    _logger?.LogInformation("Removed empty legacy ghost directory {Directory}", dirPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogDebug(ex, "Could not delete empty ghost directory {Directory}", name);
            }
        }
    }
}
