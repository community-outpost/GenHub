using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Models.AppUpdate;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.AppUpdate.Services;

/// <summary>
/// Abstract base class for platform-specific update installers.
/// </summary>
public abstract class BaseUpdateInstaller(HttpClient httpClient, ILogger logger) : IUpdateInstaller, IDisposable
{
    /// <summary>
    /// The HTTP client used for downloading updates.
    /// </summary>
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    /// <summary>
    /// The logger instance for this installer.
    /// </summary>
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private bool _disposed;

    /// <summary>
    /// Disposes the resources used by this installer.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

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

            progress?.Report(new UpdateProgress
            {
                Status = "Preparing download...",
                PercentComplete = 0,
            });

            var tempDir = Path.Combine(Path.GetTempPath(), "GenHubUpdate", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var downloadPath = await DownloadFileAsync(downloadUrl, tempDir, progress, cancellationToken);
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
            progress?.Report(new UpdateProgress
            {
                Status = $"Installation failed: {ex.Message}",
                HasError = true,
                ErrorMessage = ex.Message,
            });
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
        if (!assetList.Any())
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
    /// Formats a byte count into a human-readable string.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>A formatted string representation of the byte count.</returns>
    protected static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Disposes the resources used by this installer.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Gets platform-specific asset name patterns for download selection.
    /// </summary>
    /// <returns>List of patterns to match asset names.</returns>
    protected abstract List<string> GetPlatformAssetPatterns();

    /// <summary>
    /// Creates and launches a platform-specific external updater.
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
    /// Installs the downloaded update file.
    /// </summary>
    /// <param name="updateFilePath">Path to the update file.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation was successful.</returns>
    protected virtual async Task<bool> InstallUpdateAsync(
        string updateFilePath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new UpdateProgress
        {
            Status = "Preparing installation...",
            PercentComplete = 90,
        });

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
            progress?.Report(new UpdateProgress
            {
                Status = $"Installation failed: {ex.Message}",
                HasError = true,
                ErrorMessage = ex.Message,
            });
            return false;
        }
    }

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
        return Task.FromResult(false);
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
        progress?.Report(new UpdateProgress
        {
            Status = "Preparing ZIP update...",
            PercentComplete = 95,
        });

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
                    return false;
                }

                var success = await CreateAndLaunchExternalUpdaterAsync(sourceRoot, appDirectory, progress, cancellationToken);

                if (success)
                {
                    progress?.Report(new UpdateProgress
                    {
                        Status = "Application will restart to complete installation.",
                        PercentComplete = 100,
                        IsCompleted = true,
                    });
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
            progress?.Report(new UpdateProgress
            {
                Status = $"Update preparation failed: {ex.Message}",
                HasError = true,
                ErrorMessage = ex.Message,
            });
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
        progress?.Report(new UpdateProgress
        {
            Status = "Unsupported update file format",
            HasError = true,
            ErrorMessage = "The update file format is not supported on this platform.",
        });
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
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    entry.ExtractToFile(destinationPath, true);
                }

                processedEntries++;
                var extractProgress = (double)processedEntries / totalEntries * 30;
                progress?.Report(new UpdateProgress
                {
                    Status = $"Extracting files... {processedEntries}/{totalEntries}",
                    PercentComplete = (int)(95 + (extractProgress * 0.05)),
                });
            }
        }, cancellationToken);
    }

    private string FindApplicationSourceDirectory(string stagingDirectory)
    {
        var allExtractedFiles = Directory.GetFiles(stagingDirectory, "*", SearchOption.AllDirectories);
        var executableFiles = allExtractedFiles.Where(f =>
            Path.GetExtension(f).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(f).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (executableFiles.Any())
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

    private bool ValidateUpdateFiles(string sourceRoot)
    {
        var filesToUpdate = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(ShouldUpdateFile)
            .ToList();

        _logger.LogInformation("Found {FileCount} files to update from source: {SourceRoot}", filesToUpdate.Count, sourceRoot);

        if (!filesToUpdate.Any())
        {
            _logger.LogWarning("No files found to update in the package");
            return false;
        }

        return true;
    }

    private async Task<string> DownloadFileAsync(
        string downloadUrl,
        string destinationDir,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fileName = GetFileNameFromUrl(downloadUrl) ?? $"GenHubUpdate_{DateTime.Now:yyyyMMdd_HHmmss}.exe";
        var filePath = Path.Combine(destinationDir, fileName);

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        var downloadedBytes = 0L;
        var buffer = new byte[81920];
        var stopwatch = Stopwatch.StartNew();

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true);

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            downloadedBytes += bytesRead;

            if (stopwatch.ElapsedMilliseconds > 100 || downloadedBytes == totalBytes)
            {
                var percentComplete = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;
                var bytesPerSecond = stopwatch.ElapsedMilliseconds > 0
                    ? (long)(downloadedBytes / (stopwatch.ElapsedMilliseconds / 1000.0))
                    : 0;

                progress?.Report(new UpdateProgress
                {
                    Status = $"Downloading... {FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)}",
                    PercentComplete = (int)percentComplete,
                    BytesDownloaded = downloadedBytes,
                    TotalBytes = totalBytes,
                    BytesPerSecond = bytesPerSecond,
                });

                stopwatch.Restart();
            }
        }

        _logger.LogInformation("Download completed: {FilePath}", filePath);
        return filePath;
    }

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
