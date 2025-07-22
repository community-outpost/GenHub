using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Abstract base class for platform-specific update installers implementing the Platform Adaptation pattern.
/// Orchestrates the download and installation process using the centralized download service.
/// </summary>
public abstract class BaseUpdateInstaller(
    IDownloadService downloadService,
    ILogger logger) : IUpdateInstaller, IDisposable
{
    private readonly IDownloadService _downloadService = downloadService ?? throw new ArgumentNullException(nameof(downloadService));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private bool _disposed;

    /// <inheritdoc/>
    public async Task<bool> DownloadAndInstallAsync(
        string downloadUrl,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new ArgumentException("Download URL cannot be null or empty.", nameof(downloadUrl));
        }

        try
        {
            _logger.LogInformation("Starting update download from: {DownloadUrl}", downloadUrl);

            ReportProgress(progress, "Preparing download...", 0);

            var tempDir = Path.Combine(Path.GetTempPath(), "GenHubUpdate", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var downloadPath = await DownloadUpdateFileAsync(downloadUrl, tempDir, progress, cancellationToken);
                if (string.IsNullOrEmpty(downloadPath) || !File.Exists(downloadPath))
                {
                    _logger.LogError("Download failed or file not found: {DownloadPath}", downloadPath);
                    return false;
                }

                return await InstallUpdateAsync(downloadPath, progress, cancellationToken);
            }
            finally
            {
                CleanupDirectory(tempDir);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update installation failed");
            ReportProgress(progress, $"Installation failed: {ex.Message}", 0, true, ex.Message);
            return false;
        }
    }

    /// <inheritdoc/>
    public virtual string? GetPlatformDownloadUrl(IEnumerable<GitHubReleaseAsset> assets)
    {
        if (assets == null)
        {
            return null;
        }

        var assetList = assets.ToList();
        if (assetList.Count == 0)
        {
            return null;
        }

        var platformPatterns = GetPlatformAssetPatterns();

        foreach (var pattern in platformPatterns)
        {
            var asset = assetList.FirstOrDefault(a =>
                a.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase));

            if (asset != null)
            {
                _logger.LogInformation("Found platform-specific asset: {AssetName}", asset.Name);
                return asset.BrowserDownloadUrl;
            }
        }

        var fallbackAsset = assetList.FirstOrDefault();
        if (fallbackAsset != null)
        {
            _logger.LogInformation("Using fallback asset: {AssetName}", fallbackAsset.Name);
            return fallbackAsset.BrowserDownloadUrl;
        }

        return null;
    }

    /// <summary>
    /// Disposes the resources used by this installer.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Determines if a file should be updated during installation.
    /// </summary>
    /// <param name="filePath">The file path to check.</param>
    /// <returns>True if the file should be updated.</returns>
    protected static bool ShouldUpdateFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Skip temporary/system files
        var skipExtensions = new[] { ".tmp", ".log", ".bak", ".pdb" };
        if (skipExtensions.Contains(extension))
        {
            return false;
        }

        // Skip configuration files that shouldn't be overwritten
        var skipFiles = new[] { "appsettings.json", "appsettings.production.json", "appsettings.development.json" };
        if (skipFiles.Contains(fileName))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts a filename from a URL.
    /// </summary>
    /// <param name="url">The URL to extract filename from.</param>
    /// <returns>The extracted filename or null if extraction fails.</returns>
    protected static string? GetFileNameFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        try
        {
            var uri = new Uri(url);
            return Path.GetFileName(uri.LocalPath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reports progress with standardized format.
    /// </summary>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="status">Status message.</param>
    /// <param name="percentComplete">Completion percentage.</param>
    /// <param name="hasError">Whether there's an error.</param>
    /// <param name="errorMessage">Error message if any.</param>
    protected static void ReportProgress(
        IProgress<UpdateProgress>? progress,
        string status,
        int percentComplete,
        bool hasError = false,
        string? errorMessage = null)
    {
        progress?.Report(new UpdateProgress
        {
            Status = status,
            PercentComplete = percentComplete,
            HasError = hasError,
            ErrorMessage = errorMessage,
            IsCompleted = percentComplete >= 100 && !hasError,
        });
    }

    /// <summary>
    /// Disposes the resources used by this installer.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // DownloadService is injected via DI, don't dispose it here
            _disposed = true;
        }
    }

    /// <summary>
    /// Gets platform-specific asset name patterns for download selection.
    /// Must be implemented by platform-specific installers.
    /// </summary>
    /// <returns>List of patterns to match asset names.</returns>
    protected abstract List<string> GetPlatformAssetPatterns();

    /// <summary>
    /// Creates and launches a platform-specific external updater.
    /// Must be implemented by platform-specific installers.
    /// </summary>
    /// <param name="sourceDirectory">Directory containing the new files.</param>
    /// <param name="targetDirectory">Target application directory.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if external updater was created and launched successfully.</returns>
    protected abstract Task<bool> CreateAndLaunchExternalUpdaterAsync(
        string sourceDirectory,
        string targetDirectory,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Handles installation of executable files. Override for platform-specific behavior.
    /// </summary>
    /// <param name="exePath">Path to the executable file.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation was successful.</returns>
    protected virtual Task<bool> InstallExecutableAsync(
        string exePath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Executable installation not implemented for this platform: {ExePath}", exePath);
        ReportProgress(progress, "Executable installation not supported on this platform", 0, true, "Platform not supported");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Schedules application shutdown after a brief delay.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if shutdown was scheduled successfully.</returns>
    protected Task<bool> ScheduleApplicationShutdownAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(
            async () =>
            {
                await Task.Delay(1000, cancellationToken);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
                    {
                        _logger.LogInformation("Initiating application shutdown for update...");
                        lifetime.Shutdown(0);
                    }
                });
            },
            cancellationToken);

        return Task.FromResult(true);
    }

    /// <summary>
    /// Handles installation of MSI files. Override for platform-specific behavior.
    /// </summary>
    /// <param name="msiPath">Path to the MSI file.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation was successful.</returns>
    protected virtual Task<bool> InstallMsiAsync(
        string msiPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("MSI installation not supported on this platform: {MsiPath}", msiPath);
        ReportProgress(progress, "MSI installation not supported on this platform", 0, true, "Platform not supported");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Handles installation of ZIP files using external updater.
    /// </summary>
    /// <param name="zipPath">Path to the ZIP file.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation was successful.</returns>
    protected virtual async Task<bool> InstallZipAsync(
        string zipPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, "Preparing ZIP update...", 95);

        try
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;

            var stagingDirectory = Path.Combine(Path.GetTempPath(), "GenHubStaging", Guid.NewGuid().ToString());
            Directory.CreateDirectory(stagingDirectory);

            try
            {
                await ExtractZipFileAsync(zipPath, stagingDirectory, progress, cancellationToken);
                var sourceRoot = FindApplicationSourceDirectory(stagingDirectory);

                if (!ValidateUpdateFiles(sourceRoot))
                {
                    ReportProgress(progress, "No valid update files found in archive", 0, true, "Invalid update package");
                    return false;
                }

                var success = await CreateAndLaunchExternalUpdaterAsync(sourceRoot, appDirectory, progress, cancellationToken);

                if (success)
                {
                    ReportProgress(progress, "Application will restart to complete installation.", 100, false);
                }

                return success;
            }
            finally
            {
                // External updater will clean up staging directory
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZIP update preparation failed");
            ReportProgress(progress, $"Update preparation failed: {ex.Message}", 0, true, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Handles unsupported file formats.
    /// </summary>
    /// <param name="filePath">Path to the unsupported file.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation was successful.</returns>
    protected virtual Task<bool> HandleUnsupportedFormat(
        string filePath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("Unsupported update file format: {Extension}", Path.GetExtension(filePath));
        ReportProgress(progress, "Unsupported update file format", 0, true, "The update file format is not supported on this platform.");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Downloads the update file using the centralized download service.
    /// </summary>
    /// <param name="downloadUrl">The URL to download from.</param>
    /// <param name="destinationDir">The destination directory.</param>
    /// <param name="progress">Progress reporter for update progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path to the downloaded file.</returns>
    private async Task<string> DownloadUpdateFileAsync(
        string downloadUrl,
        string destinationDir,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fileName = GetFileNameFromUrl(downloadUrl) ?? $"GenHubUpdate_{DateTime.Now:yyyyMMdd_HHmmss}.exe";
        var filePath = Path.Combine(destinationDir, fileName);

        // Create progress adapter to convert DownloadProgress to UpdateProgress
        var downloadProgress = progress != null ? new Progress<DownloadProgress>(dp =>
        {
            progress.Report(new UpdateProgress
            {
                Status = $"Downloading... {dp.FormattedProgress} ({dp.FormattedSpeed})",
                PercentComplete = (int)dp.Percentage,
                BytesDownloaded = dp.BytesReceived,
                TotalBytes = dp.TotalBytes,
                BytesPerSecond = dp.BytesPerSecond,
            });
        }) : null;

        // Use the centralized download service
        var downloadConfig = new DownloadConfiguration
        {
            Url = downloadUrl,
            DestinationPath = filePath,
            MaxRetryAttempts = 3,
            Timeout = TimeSpan.FromMinutes(30), // Allow long downloads for large updates
        };

        var result = await _downloadService.DownloadFileAsync(
            downloadConfig,
            progress: downloadProgress,
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            _logger.LogError("Download failed: {Error}", result.ErrorMessage);
            throw new InvalidOperationException($"Download failed: {result.ErrorMessage}");
        }

        _logger.LogInformation(
            "Download completed: {FilePath} ({FormattedSize}) in {ElapsedSeconds}s at {FormattedSpeed}",
            result.FilePath,
            result.FormattedBytesDownloaded,
            result.ElapsedSeconds,
            result.FormattedSpeed);

        return result.FilePath!;
    }

    /// <summary>
    /// Installs the downloaded update file based on its type.
    /// </summary>
    /// <param name="updateFilePath">Path to the update file.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation was successful.</returns>
    private async Task<bool> InstallUpdateAsync(
        string updateFilePath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, "Preparing installation...", 90);

        var fileExtension = Path.GetExtension(updateFilePath).ToLowerInvariant();

        try
        {
            return fileExtension switch
            {
                ".exe" => await InstallExecutableAsync(updateFilePath, progress, cancellationToken),
                ".msi" => await InstallMsiAsync(updateFilePath, progress, cancellationToken),
                ".zip" => await InstallZipAsync(updateFilePath, progress, cancellationToken),
                _ => await HandleUnsupportedFormat(updateFilePath, progress, cancellationToken),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Installation failed for file: {UpdateFilePath}", updateFilePath);
            ReportProgress(progress, $"Installation failed: {ex.Message}", 0, true, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Extracts ZIP file contents to staging directory.
    /// </summary>
    /// <param name="zipPath">Path to ZIP file.</param>
    /// <param name="stagingDirectory">Destination directory.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completed task.</returns>
    private Task ExtractZipFileAsync(
        string zipPath,
        string stagingDirectory,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var totalEntries = archive.Entries.Count;
            var processedEntries = 0;

            _logger.LogInformation("ZIP contains {TotalEntries} entries", totalEntries);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(entry.Name))
                {
                    var destinationPath = Path.Combine(stagingDirectory, entry.FullName);
                    var directoryPath = Path.GetDirectoryName(destinationPath);
                    if (directoryPath != null)
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    entry.ExtractToFile(destinationPath, true);
                }

                processedEntries++;
                var extractProgress = (double)processedEntries / totalEntries * 5; // 5% of overall progress
                ReportProgress(progress, $"Extracting files... {processedEntries}/{totalEntries}", (int)(95 + extractProgress));
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Finds the application source directory within extracted files.
    /// </summary>
    /// <param name="stagingDirectory">The staging directory to search.</param>
    /// <returns>Path to the application source directory.</returns>
    private string FindApplicationSourceDirectory(string stagingDirectory)
    {
        var allExtractedFiles = Directory.GetFiles(stagingDirectory, "*", SearchOption.AllDirectories);
        var executableFiles = allExtractedFiles.Where(f =>
            Path.GetExtension(f).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(f).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (executableFiles.Count != 0)
        {
            var executableDirs = executableFiles.Select(f => Path.GetDirectoryName(f)!).Distinct().ToList();
            var appSourceDir = executableDirs
                .GroupBy(dir => dir)
                .OrderByDescending(g => g.Count())
                .First().Key;

            _logger.LogInformation("Detected application source directory in ZIP: {SourceRoot}", appSourceDir);
            return appSourceDir;
        }

        return stagingDirectory;
    }

    /// <summary>
    /// Validates that update files are present and valid.
    /// </summary>
    /// <param name="sourceRoot">Root directory to validate.</param>
    /// <returns>True if valid update files are found.</returns>
    private bool ValidateUpdateFiles(string sourceRoot)
    {
        var filesToUpdate = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(ShouldUpdateFile)
            .ToList();

        _logger.LogInformation("Found {FileCount} files to update from source: {SourceRoot}", filesToUpdate.Count, sourceRoot);

        if (filesToUpdate.Count == 0)
        {
            _logger.LogWarning("No files found to update in the package");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Cleans up temporary directory.
    /// </summary>
    /// <param name="directory">Directory to clean up.</param>
    private void CleanupDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up directory: {Directory}", directory);
        }
    }
}
