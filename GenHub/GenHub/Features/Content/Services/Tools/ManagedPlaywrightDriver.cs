using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Tools;

/// <summary>
/// Owns the app-local Playwright driver (node executable plus driver package).
/// Release builds exclude the ~94 MB driver from the installer, so GenHub downloads
/// the pinned Microsoft.Playwright NuGet package once and extracts only the
/// current platform slice plus the shared driver package under its application-data
/// directory. Debug builds keep using the driver shipped beside the binaries.
/// </summary>
internal sealed class ManagedPlaywrightDriver(
    string driverDirectory,
    HttpClient httpClient,
    Func<string, Task<bool>> requestInstallConsentAsync,
    ILogger logger,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null,
    ManagedChromiumRuntimeCallbacks? callbacks = null,
    string? expectedPackageSha256 = null)
{
    private const string PlaywrightRootName = ".playwright";
    private const string PackageEntriesPrefix = ".playwright/package/";
    private const string NodeLicenseEntryName = ".playwright/node/LICENSE";
    private const string MarkerFileName = "driver.version";
    private const string DownloadFileName = "driver.download";
    private const string StagingDirectoryName = "staging";

    private readonly SemaphoreSlim _installLock = new(1, 1);

    /// <summary>
    /// Gets the expected managed node executable path for the current platform.
    /// </summary>
    internal string ManagedNodeExecutablePath => Path.Combine(
        driverDirectory,
        PlaywrightRootName,
        "node",
        ManagedChromiumRuntime.GetDriverPlatformFolder(),
        ManagedChromiumRuntime.GetDriverNodeBinaryName());

    private string ManagedPackageEntryPointPath => Path.Combine(driverDirectory, PlaywrightRootName, "package", "cli.js");

    /// <summary>
    /// Ensures a usable Playwright driver is available and points Playwright at it.
    /// Prefers an already provisioned managed driver, then a shipped driver (Debug
    /// builds), and only downloads the driver package as a last resort.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous provisioning operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the user declines installation or when the driver could not be provisioned.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the cancellation token is requested.</exception>
    public async Task EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        if (TryUseManagedDriver())
        {
            return;
        }

        if (ManagedChromiumRuntime.TryResolveDriverNodeExecutable() != null)
        {
            return;
        }

        await _installLock.WaitAsync(cancellationToken);
        try
        {
            if (TryUseManagedDriver())
            {
                return;
            }

            if (ManagedChromiumRuntime.TryResolveDriverNodeExecutable() != null)
            {
                return;
            }

            logger.LogDebug(
                "Managed Playwright driver is missing. Requesting user consent before installing under {DriverDirectory}",
                driverDirectory);

            var consented = await requestInstallConsentAsync(driverDirectory);
            cancellationToken.ThrowIfCancellationRequested();

            if (!consented)
            {
                logger.LogDebug("User declined managed Playwright driver installation under {DriverDirectory}", driverDirectory);
                throw new InvalidOperationException(
                    "ModDB requires GenHub's managed Playwright driver. The installation was declined.");
            }

            logger.LogDebug("Managed Playwright driver install consented. Installing under {DriverDirectory}", driverDirectory);

            Action? onInstallStarting = callbacks?.OnInstallStarting;
            Action<bool>? onInstallCompleted = callbacks?.OnInstallCompleted;
            Action? onInstallCanceled = callbacks?.OnInstallCanceled;
            Func<DownloadNotificationScope?>? scopeFactory = callbacks?.ScopeFactory;

            DownloadNotificationScope? scope = null;
            if (scopeFactory != null)
            {
                scope = scopeFactory();
            }
            else if (notificationService != null)
            {
                var contentName = localizationService?.GetString("ModDB.PlaywrightDriverRuntimeName")
                    ?? ModDBConstants.PlaywrightDriverRuntimeName;
                var startTitle = localizationService?.GetString("ModDB.PlaywrightDriverInstallTitle")
                    ?? ModDBConstants.PlaywrightDriverInstallTitle;
                var startMessage = localizationService?.GetString("ModDB.PlaywrightDriverDownloadingMessage")
                    ?? ModDBConstants.PlaywrightDriverDownloadingMessage;

                scope = new DownloadNotificationScope(
                    notificationService,
                    contentName,
                    new DownloadNotificationOptions(
                        StartTitle: startTitle,
                        StartMessage: startMessage),
                    localization: localizationService);
            }

            using (scope)
            {
                onInstallStarting?.Invoke();

                try
                {
                    var packagePath = await DownloadDriverPackageAsync(scope, cancellationToken);
                    ReportExtracting(scope);
                    ExtractDriverSlice(packagePath, cancellationToken);
                    SetNodeExecutableBit(ManagedNodeExecutablePath);
                    Environment.SetEnvironmentVariable(
                        ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable,
                        driverDirectory);
                }
                catch (OperationCanceledException)
                {
                    scope?.CompleteCanceled();
                    onInstallCanceled?.Invoke();
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    var failedTitle = localizationService?.GetString("ModDB.PlaywrightDriverInstallFailedTitle")
                        ?? ModDBConstants.PlaywrightDriverInstallFailedTitle;
                    var failedMessage = localizationService?.GetString("ModDB.PlaywrightDriverInstallFailedMessage")
                        ?? ModDBConstants.PlaywrightDriverInstallFailedMessage;
                    scope?.CompleteFailure(failedMessage, failedTitle);
                    onInstallCompleted?.Invoke(false);
                    throw new InvalidOperationException(
                        "GenHub could not install its managed Playwright driver. Check the network connection and try the ModDB action again.",
                        ex);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    scope?.CompleteCanceled();
                    onInstallCanceled?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (!File.Exists(ManagedNodeExecutablePath))
                {
                    var failedTitle = localizationService?.GetString("ModDB.PlaywrightDriverInstallFailedTitle")
                        ?? ModDBConstants.PlaywrightDriverInstallFailedTitle;
                    var failedMessage = localizationService?.GetString("ModDB.PlaywrightDriverInstallFailedMessage")
                        ?? ModDBConstants.PlaywrightDriverInstallFailedMessage;
                    scope?.CompleteFailure(failedMessage, failedTitle);
                    onInstallCompleted?.Invoke(false);
                    throw new InvalidOperationException(
                        "GenHub could not install its managed Playwright driver. Check the network connection and try the ModDB action again.");
                }

                var readyTitle = localizationService?.GetString("ModDB.PlaywrightDriverReadyTitle")
                    ?? ModDBConstants.PlaywrightDriverReadyTitle;
                var readyMessage = localizationService?.GetString("ModDB.PlaywrightDriverReadyMessage")
                    ?? ModDBConstants.PlaywrightDriverReadyMessage;
                scope?.CompleteSuccess(readyMessage, readyTitle);
                onInstallCompleted?.Invoke(true);
                logger.LogInformation("Managed Playwright driver installation completed in {DriverDirectory}", driverDirectory);
            }
        }
        finally
        {
            _installLock.Release();
        }
    }

    /// <summary>
    /// Checks whether the managed driver directory holds the pinned driver version.
    /// </summary>
    /// <returns>True when the marker, node executable, and driver package entry point exist.</returns>
    internal bool IsManagedDriverValid()
    {
        try
        {
            var markerPath = Path.Combine(driverDirectory, MarkerFileName);
            if (!File.Exists(markerPath))
            {
                return false;
            }

            var version = File.ReadAllText(markerPath).Trim();
            if (!string.Equals(version, ModDBConstants.PlaywrightDriverVersion, StringComparison.Ordinal))
            {
                return false;
            }

            return File.Exists(ManagedNodeExecutablePath) && File.Exists(ManagedPackageEntryPointPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogTrace(ex, "Transient error while validating managed Playwright driver in {DriverDirectory}", driverDirectory);
            return false;
        }
    }

    private static bool IsDriverEntry(string entryName, string nodePrefix)
    {
        return entryName.StartsWith(nodePrefix, StringComparison.Ordinal)
            || entryName.StartsWith(PackageEntriesPrefix, StringComparison.Ordinal)
            || string.Equals(entryName, NodeLicenseEntryName, StringComparison.Ordinal);
    }

    private static string? GetSafeExtractionPath(string stagingDirectory, string entryName)
    {
        var relativePath = entryName.Replace('/', Path.DirectorySeparatorChar);
        var destinationPath = Path.GetFullPath(Path.Combine(stagingDirectory, relativePath));
        var stagingFullPath = Path.GetFullPath(stagingDirectory);
        if (!destinationPath.StartsWith(stagingFullPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return destinationPath;
    }

    private static void SetNodeExecutableBit(string nodePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const UnixFileMode ExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        File.SetUnixFileMode(nodePath, ExecutableMode);
    }

    private async Task VerifyPackageIntegrityAsync(string downloadPath, CancellationToken cancellationToken)
    {
        // The archive becomes an executed binary, so verify the pinned hash before
        // extraction. This applies to override feeds too: they must serve identical bytes.
        var expectedHash = expectedPackageSha256 ?? ModDBConstants.PlaywrightDriverExpectedSha256;
        var actualHash = await DownloadSecurityValidator.ComputeSha256Async(downloadPath, cancellationToken);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(downloadPath);
            throw new InvalidDataException("Downloaded Playwright driver package failed integrity verification.");
        }
    }

    private bool TryUseManagedDriver()
    {
        if (!IsManagedDriverValid())
        {
            return false;
        }

        Environment.SetEnvironmentVariable(
            ManagedChromiumRuntime.DriverSearchPathEnvironmentVariable,
            driverDirectory);
        return true;
    }

    private async Task<string> DownloadDriverPackageAsync(DownloadNotificationScope? scope, CancellationToken cancellationToken)
    {
        var url = ApiConstants.GetNuGetPackageDownloadUrl(
            ModDBConstants.PlaywrightDriverPackageId,
            ModDBConstants.PlaywrightDriverVersion);
        var downloadPath = Path.Combine(driverDirectory, DownloadFileName);
        Directory.CreateDirectory(driverDirectory);

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? (long)ModDBConstants.PlaywrightDriverExpectedSizeBytes;
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

        // Scoped so the writer is closed before integrity verification re-opens the file.
        await using (var file = new FileStream(
            downloadPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            AppUpdateConstants.DefaultStreamBufferSize,
            useAsync: true))
        {
            var buffer = new byte[AppUpdateConstants.DefaultStreamBufferSize];
            long totalRead = 0;
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;
                ReportDownloadProgress(scope, totalRead, totalBytes);
            }
        }

        await VerifyPackageIntegrityAsync(downloadPath, cancellationToken);

        return downloadPath;
    }

    private void ReportDownloadProgress(DownloadNotificationScope? scope, long totalRead, long totalBytes)
    {
        if (scope == null || totalBytes <= 0)
        {
            return;
        }

        var fraction = Math.Clamp((double)totalRead / totalBytes, 0.05, 0.95);
        var mbDownloaded = totalRead / (1024.0 * 1024.0);
        var statusFormat = localizationService?.GetString("ModDB.PlaywrightDriverProgressStatusFormat")
            ?? ModDBConstants.PlaywrightDriverProgressStatusFormat;
        var status = string.Format(
            CultureInfo.CurrentCulture,
            statusFormat,
            mbDownloaded,
            ModDBConstants.PlaywrightDriverExpectedSizeMegabytes);
        scope.ReportFraction(fraction, status);
    }

    private void ReportExtracting(DownloadNotificationScope? scope)
    {
        if (scope == null)
        {
            return;
        }

        var extractingMessage = localizationService?.GetString("ModDB.PlaywrightDriverExtractingMessage")
            ?? ModDBConstants.PlaywrightDriverExtractingMessage;
        scope.ReportFraction(0.97, extractingMessage);
    }

    private void ExtractDriverSlice(string packagePath, CancellationToken cancellationToken)
    {
        var nodePrefix = $".playwright/node/{ManagedChromiumRuntime.GetDriverPlatformFolder()}/";
        var stagingDirectory = Path.Combine(driverDirectory, StagingDirectoryName);
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, true);
        }

        Directory.CreateDirectory(stagingDirectory);

        using (var archive = ZipFile.OpenRead(packagePath))
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsDriverEntry(entry.FullName, nodePrefix))
                {
                    continue;
                }

                var destinationPath = GetSafeExtractionPath(stagingDirectory, entry.FullName);
                if (destinationPath == null)
                {
                    logger.LogWarning("Skipping driver package entry outside staging directory: {Entry}", entry.FullName);
                    continue;
                }

                if (entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        var stagedRoot = Path.Combine(stagingDirectory, PlaywrightRootName);
        if (!Directory.Exists(stagedRoot))
        {
            throw new InvalidDataException("Driver package contained no Playwright driver entries.");
        }

        var targetRoot = Path.Combine(driverDirectory, PlaywrightRootName);
        if (Directory.Exists(targetRoot))
        {
            Directory.Delete(targetRoot, true);
        }

        Directory.Move(stagedRoot, targetRoot);
        Directory.Delete(stagingDirectory, true);
        File.Delete(packagePath);
        File.WriteAllText(Path.Combine(driverDirectory, MarkerFileName), ModDBConstants.PlaywrightDriverVersion);
    }
}
