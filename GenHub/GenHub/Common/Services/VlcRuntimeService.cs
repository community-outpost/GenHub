using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services;

/// <summary>
/// Service managing discovery and on-demand installation of the native LibVLC runtime.
/// </summary>
public class VlcRuntimeService : IVlcRuntimeService
{
    /// <summary>
    /// Expected SHA-512 digest of the official VideoLAN.LibVLC.Windows 3.0.24 NuGet package.
    /// </summary>
    public const string DefaultPackageSha512 = "1ADE0A9399D2355559EF3EDD1671C37F0A4B40C408A964C9E9FB211673FFD00DDEAD4741923ECBE4F2E65AB5719045528745859390978B012EF0C7BF7370DBEE";

    [SuppressMessage("csharpsquid", "S1075", Justification = "Official NuGet package download endpoints for VideoLAN.LibVLC.Windows")]
    private const string PrimaryDownloadUrl = "https://globalcdn.nuget.org/packages/videolan.libvlc.windows.3.0.24.nupkg";

    [SuppressMessage("csharpsquid", "S1075", Justification = "Official NuGet package download endpoints for VideoLAN.LibVLC.Windows")]
    private const string FallbackDownloadUrl = "https://www.nuget.org/api/v2/package/VideoLAN.LibVLC.Windows/3.0.24";

    private const int BufferSize = 81920;

    private static readonly TimeSpan DownloadInactivityTimeout = TimeSpan.FromSeconds(60);

    private readonly HttpClient _httpClient;
    private readonly ILogger<VlcRuntimeService> _logger;
    private readonly SemaphoreSlim _installLock = new(1, 1);
    private readonly string _runtimeDir;
    private readonly string? _systemVlcDir;
    private readonly string? _expectedSha512;

    private VlcRuntimeStatus _status = VlcRuntimeStatus.NotInstalled;
    private string? _runtimeDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="VlcRuntimeService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for downloading runtime packages.</param>
    /// <param name="logger">The logger instance.</param>
    public VlcRuntimeService(HttpClient httpClient, ILogger<VlcRuntimeService> logger)
        : this(
            httpClient,
            logger,
            GetDefaultRuntimeDirectory(),
            GetDefaultSystemVlcPath(),
            DefaultPackageSha512)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VlcRuntimeService"/> class with custom directories for testing.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for downloading runtime packages.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="runtimeDirectory">The destination directory for the native runtime.</param>
    /// <param name="systemVlcDirectory">The system VLC installation directory to check, if any.</param>
    /// <param name="expectedSha512">Optional expected SHA-512 hash of the package to verify integrity.</param>
    internal VlcRuntimeService(
        HttpClient httpClient,
        ILogger<VlcRuntimeService> logger,
        string runtimeDirectory,
        string? systemVlcDirectory = null,
        string? expectedSha512 = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runtimeDir = runtimeDirectory ?? throw new ArgumentNullException(nameof(runtimeDirectory));
        _systemVlcDir = systemVlcDirectory;
        _expectedSha512 = expectedSha512;
    }

    /// <inheritdoc/>
    public VlcRuntimeStatus Status => _status;

    /// <inheritdoc/>
    public string? RuntimeDirectory => _runtimeDirectory;

    /// <inheritdoc/>
    public bool IsAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Linux/macOS, LibVLCSharp discovers system packages dynamically.
            _status = VlcRuntimeStatus.Available;
            return true;
        }

        // 1. Check local on-demand runtime directory
        if (File.Exists(Path.Combine(_runtimeDir, "libvlc.dll")) &&
            File.Exists(Path.Combine(_runtimeDir, "libvlccore.dll")))
        {
            _runtimeDirectory = _runtimeDir;
            _status = VlcRuntimeStatus.Available;
            return true;
        }

        // 2. Check standard system VLC install directory on Windows
        if (!string.IsNullOrWhiteSpace(_systemVlcDir) &&
            File.Exists(Path.Combine(_systemVlcDir, "libvlc.dll")) &&
            File.Exists(Path.Combine(_systemVlcDir, "libvlccore.dll")))
        {
            _runtimeDirectory = _systemVlcDir;
            _status = VlcRuntimeStatus.Available;
            return true;
        }

        _status = VlcRuntimeStatus.NotInstalled;
        return false;
    }

    /// <inheritdoc/>
    public async Task<bool> InstallRuntimeAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogWarning("On-demand LibVLC acquisition is only supported on Windows.");
            _status = VlcRuntimeStatus.UnsupportedPlatform;
            return false;
        }

        var prefixes = GetPackagePrefixes();
        if (prefixes == null)
        {
            _logger.LogError("Unsupported process architecture for native LibVLC package: {Architecture}", RuntimeInformation.ProcessArchitecture);
            _status = VlcRuntimeStatus.UnsupportedPlatform;
            return false;
        }

        if (IsAvailable())
        {
            return true;
        }

        await _installLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsAvailable())
            {
                return true;
            }

            _status = VlcRuntimeStatus.Downloading;
            var targetParent = Path.GetDirectoryName(_runtimeDir) ?? Path.GetTempPath();
            Directory.CreateDirectory(targetParent);

            var tempArchive = Path.Combine(Path.GetTempPath(), $"genhub-vlc-{Guid.NewGuid():N}.tmp");
            var stagingDir = Path.Combine(targetParent, $".staging-{Guid.NewGuid():N}");

            try
            {
                var downloaded = await DownloadPackageAsync(tempArchive, progress, cancellationToken).ConfigureAwait(false);
                if (!downloaded)
                {
                    _status = VlcRuntimeStatus.Failed;
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var hashValid = await VerifyPackageHashAsync(tempArchive, cancellationToken).ConfigureAwait(false);
                if (!hashValid)
                {
                    _status = VlcRuntimeStatus.Failed;
                    return false;
                }

                _logger.LogInformation("Extracting native LibVLC binaries to staging directory...");
                await ExtractPackagePayloadAsync(tempArchive, stagingDir, prefixes.Value.EntryPrefix, prefixes.Value.IncludePrefix, cancellationToken).ConfigureAwait(false);

                PromoteStagingDirectory(stagingDir, _runtimeDir);

                _runtimeDirectory = _runtimeDir;
                _status = VlcRuntimeStatus.Available;
                _logger.LogInformation("Successfully installed native LibVLC runtime to {RuntimeDirectory}", _runtimeDir);
                return true;
            }
            finally
            {
                CleanupArtifacts(tempArchive, stagingDir);
            }
        }
        catch (OperationCanceledException)
        {
            _status = VlcRuntimeStatus.NotInstalled;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install LibVLC runtime.");
            _status = VlcRuntimeStatus.Failed;
            return false;
        }
        finally
        {
            _installLock.Release();
        }
    }

    private static string GetDefaultRuntimeDirectory()
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "win-x86",
            Architecture.X64 => "win-x64",
            _ => "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };

        return Path.Combine(AppDataPathHelper.GetDataRoot(), "runtimes", "vlc", arch);
    }

    private static (string EntryPrefix, string IncludePrefix)? GetPackagePrefixes()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => ("build/x64/", "build/x64/include/"),
            Architecture.X86 => ("build/x86/", "build/x86/include/"),
            _ => null,
        };
    }

    private static string? GetDefaultSystemVlcPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return !string.IsNullOrWhiteSpace(programFiles)
            ? Path.Combine(programFiles, "VideoLAN", "VLC")
            : null;
    }

    [SuppressMessage("csharpsquid", "S2325", Justification = "Static utility method for directory copying")]
    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var targetFilePath = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, targetFilePath, overwrite: true);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var targetSubDirPath = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, targetSubDirPath);
        }
    }

    private static void CleanupArtifacts(string? tempFile, string? stagingDir)
    {
        if (!string.IsNullOrWhiteSpace(tempFile) && File.Exists(tempFile))
        {
            try
            {
                File.Delete(tempFile);
            }
            catch
            {
                // Ignore temp cleanup failure
            }
        }

        if (!string.IsNullOrWhiteSpace(stagingDir) && Directory.Exists(stagingDir))
        {
            try
            {
                Directory.Delete(stagingDir, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure
            }
        }
    }

    private static async Task ExtractPackagePayloadAsync(
        string archivePath,
        string stagingDir,
        string entryPrefix,
        string includePrefix,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDir);
        var canonicalStaging = Path.GetFullPath(stagingDir) + Path.DirectorySeparatorChar;

        await using var archiveStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            var entryName = entry.FullName.Replace('\\', '/');
            if (!entryName.StartsWith(entryPrefix, StringComparison.OrdinalIgnoreCase) ||
                entryName.StartsWith(includePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = entryName[entryPrefix.Length..];
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.EndsWith('/'))
            {
                continue;
            }

            var destPath = Path.GetFullPath(Path.Combine(stagingDir, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!destPath.StartsWith(canonicalStaging, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Invalid archive entry path: {entry.FullName}");
            }

            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrWhiteSpace(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            await using var entryStream = entry.Open();
            await using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            await entryStream.CopyToAsync(destStream, cancellationToken).ConfigureAwait(false);
        }

        var libvlc = Path.Combine(stagingDir, "libvlc.dll");
        var libvlccore = Path.Combine(stagingDir, "libvlccore.dll");
        if (!File.Exists(libvlc) || !File.Exists(libvlccore))
        {
            throw new FileNotFoundException("Extracted LibVLC staging folder does not contain essential DLLs.");
        }
    }

    private async Task<bool> DownloadPackageAsync(string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var downloaded = await TryDownloadAsync(PrimaryDownloadUrl, destinationPath, progress, cancellationToken).ConfigureAwait(false);
        if (!downloaded)
        {
            _logger.LogWarning("Primary LibVLC download failed, attempting fallback URL...");
            downloaded = await TryDownloadAsync(FallbackDownloadUrl, destinationPath, progress, cancellationToken).ConfigureAwait(false);
        }

        if (!downloaded)
        {
            _logger.LogError("Failed to download LibVLC package from all available sources.");
        }

        return downloaded;
    }

    private async Task<bool> VerifyPackageHashAsync(string archivePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_expectedSha512))
        {
            return true;
        }

        await using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        using var sha = SHA512.Create();
        var hashBytes = await sha.ComputeHashAsync(fs, cancellationToken).ConfigureAwait(false);
        var actualHash = Convert.ToHexString(hashBytes);

        if (!string.Equals(actualHash, _expectedSha512, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Downloaded LibVLC package SHA-512 mismatch. Expected {Expected}, got {Actual}", _expectedSha512, actualHash);
            return false;
        }

        return true;
    }

    private void PromoteStagingDirectory(string stagingDir, string targetDir)
    {
        string? backupDir = null;
        try
        {
            if (Directory.Exists(targetDir))
            {
                backupDir = targetDir + ".old." + Guid.NewGuid().ToString("N");
                try
                {
                    Directory.Move(targetDir, backupDir);
                }
                catch (IOException)
                {
                    CopyDirectory(stagingDir, targetDir);
                    return;
                }
            }

            try
            {
                Directory.Move(stagingDir, targetDir);
            }
            catch (IOException)
            {
                CopyDirectory(stagingDir, targetDir);
            }

            if (backupDir != null && Directory.Exists(backupDir))
            {
                try
                {
                    Directory.Delete(backupDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up old runtime backup directory {BackupDir}", backupDir);
                }
            }
        }
        catch (Exception ex)
        {
            if (backupDir != null && Directory.Exists(backupDir) && !Directory.Exists(targetDir))
            {
                try
                {
                    Directory.Move(backupDir, targetDir);
                }
                catch (Exception restoreEx)
                {
                    _logger.LogError(restoreEx, "Failed to restore backup runtime directory {BackupDir}", backupDir);
                }
            }

            throw new InvalidOperationException("Failed to promote LibVLC staging directory into runtime directory.", ex);
        }
    }

    private async Task<bool> TryDownloadAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(DownloadInactivityTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

            var buffer = new byte[BufferSize];
            long totalRead = 0;
            int bytesRead;

            while (true)
            {
                timeoutCts.CancelAfter(DownloadInactivityTimeout);
                bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), linkedCts.Token).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                totalRead += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    progress.Report((double)totalRead / totalBytes);
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Download failed or timed out from {Url}", url);
            return false;
        }
    }
}
