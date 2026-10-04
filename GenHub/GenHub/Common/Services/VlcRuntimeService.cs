using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services;

/// <summary>
/// Service managing discovery and on-demand installation of the native LibVLC runtime.
/// </summary>
public class VlcRuntimeService : IVlcRuntimeService
{
    private const string PrimaryDownloadUrl = "https://globalcdn.nuget.org/packages/videolan.libvlc.windows.3.0.24.nupkg";
    private const string FallbackDownloadUrl = "https://www.nuget.org/api/v2/package/VideoLAN.LibVLC.Windows/3.0.24";
    private const string X64EntryPrefix = "build/x64/";
    private const string IncludePrefix = "build/x64/include/";
    private const int BufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly ILogger<VlcRuntimeService> _logger;
    private readonly SemaphoreSlim _installLock = new(1, 1);
    private readonly string _runtimeDir;
    private readonly string? _systemVlcDir;

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
            Path.Combine(AppDataPathHelper.GetDataRoot(), "runtimes", "vlc", "win-x64"),
            GetDefaultSystemVlcPath())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VlcRuntimeService"/> class with custom directories for testing.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for downloading runtime packages.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="runtimeDirectory">The destination directory for the native runtime.</param>
    /// <param name="systemVlcDirectory">The system VLC installation directory to check, if any.</param>
    internal VlcRuntimeService(
        HttpClient httpClient,
        ILogger<VlcRuntimeService> logger,
        string runtimeDirectory,
        string? systemVlcDirectory = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runtimeDir = runtimeDirectory ?? throw new ArgumentNullException(nameof(runtimeDirectory));
        _systemVlcDir = systemVlcDirectory;
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
            var tempArchive = Path.Combine(Path.GetTempPath(), $"genhub-vlc-{Guid.NewGuid():N}.tmp");
            var stagingDir = Path.Combine(Path.GetTempPath(), $"genhub-vlc-staging-{Guid.NewGuid():N}");

            try
            {
                // Download package with fallback
                var downloaded = await TryDownloadAsync(PrimaryDownloadUrl, tempArchive, progress, cancellationToken).ConfigureAwait(false);
                if (!downloaded)
                {
                    _logger.LogWarning("Primary LibVLC download failed, attempting fallback URL...");
                    downloaded = await TryDownloadAsync(FallbackDownloadUrl, tempArchive, progress, cancellationToken).ConfigureAwait(false);
                }

                if (!downloaded)
                {
                    _logger.LogError("Failed to download LibVLC package from all available sources.");
                    _status = VlcRuntimeStatus.Failed;
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Unpack x64 binaries from nupkg/zip
                _logger.LogInformation("Extracting native LibVLC x64 binaries to staging directory...");
                Directory.CreateDirectory(stagingDir);
                var canonicalStaging = Path.GetFullPath(stagingDir) + Path.DirectorySeparatorChar;

                using (var archive = ZipFile.OpenRead(tempArchive))
                {
                    foreach (var entry in archive.Entries)
                    {
                        var entryName = entry.FullName.Replace('\\', '/');
                        if (!entryName.StartsWith(X64EntryPrefix, StringComparison.OrdinalIgnoreCase) ||
                            entryName.StartsWith(IncludePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var relativePath = entryName[X64EntryPrefix.Length..];
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

                        entry.ExtractToFile(destPath, overwrite: true);
                    }
                }

                // Verify extraction
                var libvlc = Path.Combine(stagingDir, "libvlc.dll");
                var libvlccore = Path.Combine(stagingDir, "libvlccore.dll");
                if (!File.Exists(libvlc) || !File.Exists(libvlccore))
                {
                    throw new FileNotFoundException("Extracted LibVLC staging folder does not contain essential DLLs.");
                }

                // Promote staging directory to final target directory
                if (Directory.Exists(_runtimeDir))
                {
                    try
                    {
                        Directory.Delete(_runtimeDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean existing runtime directory prior to upgrade; overwriting in-place.");
                    }
                }

                var targetParent = Path.GetDirectoryName(_runtimeDir);
                if (!string.IsNullOrWhiteSpace(targetParent))
                {
                    Directory.CreateDirectory(targetParent);
                }

                if (!Directory.Exists(_runtimeDir))
                {
                    Directory.Move(stagingDir, _runtimeDir);
                }
                else
                {
                    // Copy recursively if Move is blocked
                    CopyDirectory(stagingDir, _runtimeDir);
                }

                _runtimeDirectory = _runtimeDir;
                _status = VlcRuntimeStatus.Available;
                _logger.LogInformation("Successfully installed native LibVLC runtime to {RuntimeDirectory}", _runtimeDir);
                return true;
            }
            finally
            {
                if (File.Exists(tempArchive))
                {
                    try
                    {
                        File.Delete(tempArchive);
                    }
                    catch
                    {
                        // Ignore temp cleanup
                    }
                }

                if (Directory.Exists(stagingDir))
                {
                    try
                    {
                        Directory.Delete(stagingDir, recursive: true);
                    }
                    catch
                    {
                        // Ignore temp cleanup
                    }
                }
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

    private async Task<bool> TryDownloadAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

            var buffer = new byte[BufferSize];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                totalRead += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    progress.Report(Math.Min(1.0, (double)totalRead / totalBytes));
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Download failed from {Url}", url);
            return false;
        }
    }

    private void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(targetDir, relative);
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, dest, overwrite: true);
        }
    }
}
