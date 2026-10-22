using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Steam;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;
using SteamKit2.Internal;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Downloads Steam Workshop items directly from Steam via HTTP or SteamKit2 CDN client.
/// </summary>
public sealed class SteamWorkshopClientDownloader : ISteamWorkshopClientDownloader
{
    private static readonly TimeSpan SteamConnectTimeout = TimeSpan.FromSeconds(20);

    private readonly SteamWorkshopApiClient _apiClient;
    private readonly ISteamWorkshopAccountAuthService _authService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SteamWorkshopClientDownloader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamWorkshopClientDownloader"/> class.
    /// </summary>
    /// <param name="apiClient">The Steam Workshop API client.</param>
    /// <param name="authService">The Steam account authentication service.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public SteamWorkshopClientDownloader(
        SteamWorkshopApiClient apiClient,
        ISteamWorkshopAccountAuthService authService,
        IHttpClientFactory httpClientFactory,
        ILogger<SteamWorkshopClientDownloader> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> DownloadWorkshopItemAsync(
        int appId,
        string publishedFileId,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        try
        {
            _logger.LogInformation("Querying Steam file details for Workshop item {PublishedFileId}.", publishedFileId);
            var detailsResult = await _apiClient.GetPublishedFileDetailsAsync(
                publishedFileId,
                cancellationToken: cancellationToken);

            if (!detailsResult.Success || detailsResult.Data == null)
            {
                var error = detailsResult.FirstError ?? "Failed to fetch Workshop item metadata from Steam.";
                _logger.LogWarning("Failed to query file details for Workshop item {PublishedFileId}: {Error}", publishedFileId, error);
                return OperationResult<bool>.CreateFailure(error);
            }

            var item = detailsResult.Data;
            Directory.CreateDirectory(targetDirectory);

            // Case 1: Direct HTTP Cloud File URL
            if (!string.IsNullOrWhiteSpace(item.FileUrl))
            {
                _logger.LogInformation("Downloading Workshop item {PublishedFileId} via direct HTTP URL.", publishedFileId);
                return await DownloadHttpFileAsync(item.FileUrl, targetDirectory, item.Title ?? publishedFileId, progress, cancellationToken);
            }

            // Case 2: SteamPipe UGC Depot Manifest
            if (!string.IsNullOrWhiteSpace(item.HContentFile))
            {
                if (!ulong.TryParse(item.HContentFile, out var manifestGid))
                {
                    return OperationResult<bool>.CreateFailure($"Invalid Workshop manifest GID '{item.HContentFile}'.");
                }

                var consumerAppId = item.ConsumerAppId != 0 ? (uint)item.ConsumerAppId : (uint)appId;
                _logger.LogInformation(
                    "Downloading Workshop item {PublishedFileId} via SteamPipe CDN (App: {AppId}, Manifest: {ManifestGid}).",
                    publishedFileId,
                    consumerAppId,
                    manifestGid);

                return await DownloadSteamPipeDepotAsync(
                    consumerAppId,
                    manifestGid,
                    targetDirectory,
                    progress,
                    cancellationToken);
            }

            return OperationResult<bool>.CreateFailure(
                $"Steam Workshop item {publishedFileId} does not contain downloadable file content (no direct URL or SteamPipe manifest).");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Workshop download for item {PublishedFileId} was canceled.", publishedFileId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error downloading Steam Workshop item {PublishedFileId}.", publishedFileId);
            return OperationResult<bool>.CreateFailure($"Download failed: {ex.Message}");
        }
    }

    private static bool HasZipHeader(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[4];
            var read = stream.Read(header);
            if (read >= 4 && header[0] == 0x50 && header[1] == 0x4B)
            {
                return (header[2] == 0x03 && header[3] == 0x04) ||
                       (header[2] == 0x05 && header[3] == 0x06) ||
                       (header[2] == 0x07 && header[3] == 0x08);
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsValidZipArchive(string filePath, out int entryCount)
    {
        entryCount = 0;
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            entryCount = archive.Entries.Count;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static uint ExtractWorkshopDepotId(
        AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet? resultSet,
        uint appId)
    {
        if (resultSet?.Results == null)
        {
            return 0;
        }

        foreach (var result in resultSet.Results)
        {
            if (result?.Apps == null || !result.Apps.TryGetValue(appId, out var appInfo) || appInfo?.KeyValues == null)
            {
                continue;
            }

            var depots = appInfo.KeyValues["depots"];
            if (depots == null || depots == KeyValue.Invalid)
            {
                continue;
            }

            var workshopDepot = depots["workshopdepot"].AsUnsignedInteger();
            if (workshopDepot != 0)
            {
                return workshopDepot;
            }
        }

        return 0;
    }

    private static async Task WaitForConditionAsync(
        Task task,
        CallbackManager manager,
        CancellationToken cancellationToken)
    {
        while (!task.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(50));
            await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string ValidateAndPrepareTargetPath(string fullTargetDir, string relativeFileName)
    {
        var cleanFileName = relativeFileName.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        var fullPath = Path.GetFullPath(Path.Combine(fullTargetDir, cleanFileName));
        if (!fullPath.StartsWith(fullTargetDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(fullPath, fullTargetDir, StringComparison.Ordinal))
        {
            throw new IOException($"Manifest entry escapes target directory: {relativeFileName}");
        }

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return fullPath;
    }

    private static async Task<int> DownloadSingleChunkAsync(
        CdnDownloadSession session,
        DepotManifest.ChunkData chunk,
        byte[] chunkBuffer,
        CancellationToken cancellationToken)
    {
        try
        {
            return await session.CdnClient
                .DownloadDepotChunkAsync(session.DepotId, chunk, session.Server, chunkBuffer, session.DepotKey)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static void CommitTempFile(string tempFilePath, string fullPath)
    {
        if (File.Exists(fullPath))
        {
            File.Replace(tempFilePath, fullPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempFilePath, fullPath);
        }
    }

    private static void CleanupTempFile(string tempFilePath)
    {
        if (File.Exists(tempFilePath))
        {
            try
            {
                File.Delete(tempFilePath);
            }
            catch (IOException)
            {
                // Best effort cleanup of temp staging file
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort cleanup of temp staging file
            }
        }
    }

    private async Task<OperationResult<bool>> DownloadHttpFileAsync(
        string fileUrl,
        string targetDirectory,
        string itemTitle,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("SteamWorkshop");
        using var response = await httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var tempFile = Path.Combine(targetDirectory, $"download_{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = File.Create(tempFile))
            {
                await StreamHttpPayloadWithProgressAsync(contentStream, fileStream, totalBytes, itemTitle, progress, cancellationToken).ConfigureAwait(false);
            }

            await StageDownloadedPayloadAsync(tempFile, targetDirectory, itemTitle, cancellationToken).ConfigureAwait(false);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to delete temporary download file {TempFile}.", tempFile);
                }
            }
        }
    }

    private async Task StreamHttpPayloadWithProgressAsync(
        Stream contentStream,
        Stream fileStream,
        long totalBytes,
        string itemTitle,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalRead = 0;
        while (true)
        {
            var read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            totalRead += read;
            progress?.Report(new ContentAcquisitionProgress
            {
                BytesProcessed = totalRead,
                TotalBytes = totalBytes,
                CurrentOperation = $"Downloading {itemTitle}...",
            });
        }
    }

    private async Task StageDownloadedPayloadAsync(
        string tempFile,
        string targetDirectory,
        string itemTitle,
        CancellationToken cancellationToken)
    {
        if (IsValidZipArchive(tempFile, out var entryCount))
        {
            if (entryCount == 0)
            {
                throw new InvalidDataException($"Downloaded archive for '{itemTitle}' is an empty ZIP file with no entries.");
            }

            _logger.LogInformation("Extracting downloaded zip archive for {Title} into {TargetDir}.", itemTitle, targetDirectory);
            await Task.Run(() => ZipArchiveGuard.ExtractToDirectory(tempFile, targetDirectory, cancellationToken), cancellationToken).ConfigureAwait(false);
            File.Delete(tempFile);
            return;
        }

        if (HasZipHeader(tempFile))
        {
            throw new InvalidDataException($"Downloaded archive for '{itemTitle}' is a corrupted or incomplete ZIP file.");
        }

        var safeFileName = PathHelper.SanitizeFileName($"{itemTitle}.map", replaceSpaces: false);
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Equals(".map", StringComparison.OrdinalIgnoreCase))
        {
            safeFileName = "workshop_item.map";
        }

        var fullTargetDir = Path.GetFullPath(targetDirectory);
        var finalDest = Path.GetFullPath(Path.Combine(fullTargetDir, safeFileName));
        if (!finalDest.StartsWith(fullTargetDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(finalDest, fullTargetDir, StringComparison.Ordinal))
        {
            throw new IOException($"Target file path escapes target directory: {safeFileName}");
        }

        if (File.Exists(finalDest))
        {
            File.Replace(tempFile, finalDest, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempFile, finalDest);
        }
    }

    private async Task<OperationResult<bool>> DownloadSteamPipeDepotAsync(
        uint appId,
        ulong manifestGid,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var accountInfo = await _authService.GetAccountInfoAsync(cancellationToken);
        if (accountInfo == null || string.IsNullOrWhiteSpace(accountInfo.RefreshToken))
        {
            return OperationResult<bool>.CreateFailure(
                "Steam account sign-in is required to download Workshop items directly in GenHub. Please sign in with your Steam account in Settings -> Downloads -> Steam Account.");
        }

        var steamClient = new SteamClient();
        var manager = new CallbackManager(steamClient);
        var steamUser = steamClient.GetHandler<SteamUser>()!;
        var steamApps = steamClient.GetHandler<SteamApps>();
        var steamUnifiedMessages = steamClient.GetHandler<SteamUnifiedMessages>()!;

        try
        {
            var logonResult = await LogonSteamClientAsync(steamClient, manager, steamUser, accountInfo, cancellationToken);
            if (!logonResult.Success)
            {
                return logonResult;
            }

            var depotId = await ResolveWorkshopDepotIdAsync(steamApps, manager, appId, cancellationToken).ConfigureAwait(false);

            var requestCodeResult = await RequestManifestCodeAsync(steamUnifiedMessages, manager, appId, depotId, manifestGid, cancellationToken);
            if (!requestCodeResult.Success)
            {
                return OperationResult<bool>.CreateFailure(requestCodeResult.FirstError ?? "Failed to acquire manifest request code.");
            }

            var cdnClient = new Client(steamClient);
            Server server = new DnsEndPoint(SteamWorkshopConstants.SteamPipeCdnHost, SteamWorkshopConstants.SteamPipeCdnPort);
            var depotKey = await GetDepotKeyAsync(steamClient, manager, depotId, appId, cancellationToken).ConfigureAwait(false);
            if (depotKey == null || depotKey.Length == 0)
            {
                _logger.LogWarning("Failed to acquire depot decryption key for depot {DepotId} (app {AppId}).", depotId, appId);
                return OperationResult<bool>.CreateFailure($"Failed to acquire depot decryption key for depot {depotId} (app {appId}). Please verify your Steam account credentials.");
            }

            var manifest = await cdnClient.DownloadManifestAsync(depotId, manifestGid, requestCodeResult.Data, server, depotKey);
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                return OperationResult<bool>.CreateFailure("SteamPipe manifest is empty or could not be downloaded.");
            }

            _logger.LogInformation("Manifest contains {Count} files. Starting chunk downloads...", manifest.Files.Count);
            var cdnSession = new CdnDownloadSession(cdnClient, server, depotId, depotKey, targetDirectory);
            await DownloadManifestFilesAsync(cdnSession, manifest, progress, cancellationToken);

            _logger.LogInformation("Successfully downloaded all Workshop files to {TargetDirectory}.", targetDirectory);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            steamClient.Disconnect();
        }
    }

    private async Task<OperationResult<bool>> LogonSteamClientAsync(
        SteamClient steamClient,
        CallbackManager manager,
        SteamUser steamUser,
        SteamAccountInfo accountInfo,
        CancellationToken cancellationToken)
    {
        var tcsConnect = new TaskCompletionSource<bool>();
        var tcsLogin = new TaskCompletionSource<bool>();

        manager.Subscribe<SteamClient.ConnectedCallback>(_ => tcsConnect.TrySetResult(true));

        manager.Subscribe<SteamUser.LoggedOnCallback>(cb =>
        {
            if (cb.Result == EResult.OK)
            {
                _logger.LogDebug("Successfully logged on to Steam as '{AccountName}'.", accountInfo.AccountName);
                tcsLogin.TrySetResult(true);
            }
            else
            {
                _logger.LogWarning("Steam logon failed with result: {Result}", cb.Result);
                tcsLogin.TrySetException(new InvalidOperationException($"Steam logon failed: {cb.Result}. Try signing in again in Settings."));
            }
        });

        manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
        {
            if (!tcsConnect.Task.IsCompleted)
            {
                tcsConnect.TrySetException(new InvalidOperationException("Disconnected from Steam network before connecting."));
            }

            if (!tcsLogin.Task.IsCompleted)
            {
                tcsLogin.TrySetException(new InvalidOperationException("Disconnected from Steam network before logon completed."));
            }
        });

        _logger.LogDebug("Connecting to Steam network...");
        steamClient.Connect();

        using var ctsTimeout = new CancellationTokenSource(SteamConnectTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctsTimeout.Token);

        await WaitForConditionAsync(tcsConnect.Task, manager, linked.Token).ConfigureAwait(false);
        if (ctsTimeout.IsCancellationRequested)
        {
            return OperationResult<bool>.CreateFailure("Timed out while connecting to the Steam network.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!tcsConnect.Task.IsCompletedSuccessfully)
        {
            var ex = tcsConnect.Task.Exception?.GetBaseException();
            return OperationResult<bool>.CreateFailure(ex?.Message ?? "Disconnected from Steam network before connecting.");
        }

        var steamId = accountInfo.SteamId != 0UL
            ? accountInfo.SteamId
            : SteamWorkshopHelper.ExtractSteamIdFromToken(accountInfo.RefreshToken);

        _logger.LogDebug("Connected to Steam CM. Logging on as '{AccountName}' (SteamID: {SteamId})...", accountInfo.AccountName, steamId);
        steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = accountInfo.AccountName,
            AccessToken = accountInfo.RefreshToken,
            ShouldRememberPassword = true,
        });

        await WaitForConditionAsync(tcsLogin.Task, manager, linked.Token).ConfigureAwait(false);
        if (ctsTimeout.IsCancellationRequested)
        {
            return OperationResult<bool>.CreateFailure("Timed out while logging on to the Steam network.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!tcsLogin.Task.IsCompletedSuccessfully)
        {
            var ex = tcsLogin.Task.Exception?.GetBaseException();
            return OperationResult<bool>.CreateFailure(ex?.Message ?? "Steam logon failed. Try signing in again in Settings.");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task<uint> ResolveWorkshopDepotIdAsync(
        SteamApps? steamApps,
        CallbackManager manager,
        uint appId,
        CancellationToken cancellationToken)
    {
        if (steamApps == null)
        {
            return appId;
        }

        try
        {
            var job = steamApps.PICSGetProductInfo([new SteamApps.PICSRequest(appId)], []);
            using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctsTimeout.Token);

            var task = job.ToTask();
            await WaitForConditionAsync(task, manager, linked.Token).ConfigureAwait(false);

            if (task.IsCompletedSuccessfully)
            {
                var resultSet = await task.ConfigureAwait(false);
                var workshopDepot = ExtractWorkshopDepotId(resultSet, appId);
                if (workshopDepot != 0)
                {
                    _logger.LogInformation("Resolved Workshop depot {WorkshopDepot} for App {AppId}.", workshopDepot, appId);
                    return workshopDepot;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to resolve workshop depot ID for app {AppId}, defaulting to appId.", appId);
        }

        return appId;
    }

    private async Task<OperationResult<ulong>> RequestManifestCodeAsync(
        SteamUnifiedMessages steamUnifiedMessages,
        CallbackManager manager,
        uint appId,
        uint depotId,
        ulong manifestGid,
        CancellationToken cancellationToken)
    {
        var csdService = steamUnifiedMessages.CreateService<ContentServerDirectory>();
        var req = new CContentServerDirectory_GetManifestRequestCode_Request
        {
            app_id = appId,
            depot_id = depotId,
            manifest_id = manifestGid,
        };

        var job = csdService.GetManifestRequestCode(req);
        using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctsTimeout.Token);

        var task = job.ToTask();
        await WaitForConditionAsync(task, manager, linked.Token).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (!task.IsCompletedSuccessfully)
        {
            return OperationResult<ulong>.CreateFailure("Timed out waiting for Steam Content Server Directory response.");
        }

        var csdCallback = await task.ConfigureAwait(false);
        if (csdCallback.Result != EResult.OK || csdCallback.Body == null || csdCallback.Body.manifest_request_code == 0)
        {
            return OperationResult<ulong>.CreateFailure(
                $"Failed to acquire manifest request code from Steam Content Server Directory: {csdCallback.Result}");
        }

        return OperationResult<ulong>.CreateSuccess(csdCallback.Body.manifest_request_code);
    }

    private async Task<byte[]?> GetDepotKeyAsync(
        SteamClient steamClient,
        CallbackManager manager,
        uint depotId,
        uint appId,
        CancellationToken cancellationToken)
    {
        var steamApps = steamClient.GetHandler<SteamApps>();
        if (steamApps == null)
        {
            return null;
        }

        try
        {
            var tcsKey = new TaskCompletionSource<byte[]?>();
            using (manager.Subscribe<SteamApps.DepotKeyCallback>(cb =>
            {
                if (cb.DepotID == depotId)
                {
                    if (cb.Result == EResult.OK)
                    {
                        tcsKey.TrySetResult(cb.DepotKey);
                    }
                    else
                    {
                        _logger.LogDebug("Steam depot decryption key request returned result: {Result}", cb.Result);
                        tcsKey.TrySetResult(null);
                    }
                }
            }))
            {
                _ = steamApps.GetDepotDecryptionKey(depotId, appId);

                using var ctsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctsTimeout.Token);

                while (!tcsKey.Task.IsCompleted && !linked.IsCancellationRequested)
                {
                    manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
                    await Task.Delay(20, linked.Token).ConfigureAwait(false);
                }

                return tcsKey.Task.IsCompleted ? await tcsKey.Task.ConfigureAwait(false) : null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not acquire depot decryption key for depot {DepotId} (app {AppId})", depotId, appId);
            return null;
        }
    }

    private async Task DownloadManifestFilesAsync(
        CdnDownloadSession session,
        DepotManifest manifest,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (manifest.Files == null)
        {
            return;
        }

        using (cancellationToken.Register(static client => ((Client)client!).Dispose(), session.CdnClient))
        {
            long totalBytes = manifest.Files
                .Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory))
                .Sum(f => (long)f.TotalSize);

            long downloadedBytes = 0;
            var fullTargetDir = Path.GetFullPath(session.TargetDirectory);

            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Flags.HasFlag(EDepotFileFlag.Directory))
                {
                    continue;
                }

                downloadedBytes += await DownloadManifestEntryAsync(
                    session,
                    file,
                    fullTargetDir,
                    totalBytes,
                    downloadedBytes,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<long> DownloadManifestEntryAsync(
        CdnDownloadSession session,
        DepotManifest.FileData file,
        string fullTargetDir,
        long totalBytes,
        long downloadedBytesSoFar,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fullPath = ValidateAndPrepareTargetPath(fullTargetDir, file.FileName);
        var tempFilePath = Path.Combine(
            Path.GetDirectoryName(fullPath) ?? fullTargetDir,
            $"{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var fileBytesRead = await WriteManifestFileChunksAsync(
                session,
                file,
                tempFilePath,
                totalBytes,
                downloadedBytesSoFar,
                progress,
                cancellationToken).ConfigureAwait(false);

            CommitTempFile(tempFilePath, fullPath);
            return fileBytesRead;
        }
        finally
        {
            CleanupTempFile(tempFilePath);
        }
    }

    private async Task<long> WriteManifestFileChunksAsync(
        CdnDownloadSession session,
        DepotManifest.FileData file,
        string tempFilePath,
        long totalBytes,
        long downloadedBytesSoFar,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        long fileBytesRead = 0;
        using (var fileStream = File.Create(tempFilePath))
        {
            foreach (var chunk in file.Chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkBuffer = new byte[chunk.UncompressedLength];
                var bytesWritten = await DownloadSingleChunkAsync(session, chunk, chunkBuffer, cancellationToken).ConfigureAwait(false);

                if (bytesWritten != chunk.UncompressedLength)
                {
                    throw new IOException($"Failed to download or decrypt the complete chunk {chunk.ChunkID} for file {file.FileName}.");
                }

                fileStream.Seek((long)chunk.Offset, SeekOrigin.Begin);
                await fileStream.WriteAsync(chunkBuffer.AsMemory(0, bytesWritten), cancellationToken);

                fileBytesRead += bytesWritten;
                progress?.Report(new ContentAcquisitionProgress
                {
                    BytesProcessed = downloadedBytesSoFar + fileBytesRead,
                    TotalBytes = totalBytes,
                    CurrentOperation = $"Downloading {Path.GetFileName(file.FileName)}...",
                });
            }
        }

        return fileBytesRead;
    }

    private sealed record CdnDownloadSession(
        Client CdnClient,
        Server Server,
        uint DepotId,
        byte[] DepotKey,
        string TargetDirectory);
}
