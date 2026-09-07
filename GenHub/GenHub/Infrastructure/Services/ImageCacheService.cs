using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenHub;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;

namespace GenHub.Infrastructure.Services;

/// <summary>
/// Thread-safe service for downloading and caching web and local images in memory and on disk.
/// </summary>
public sealed class ImageCacheService : IImageCacheService
{
    private static readonly object InstanceLock = new();
    private static readonly Uri ModDbReferrerUri = new(ImageCacheConstants.ModDbReferrerUrl);
    private static volatile IImageCacheService? instance;

    private readonly LruMemoryCache memoryCache = new(ImageCacheConstants.MaxMemoryCacheEntries, ImageCacheConstants.MaxMemoryCacheSizeBytes);
    private readonly ConcurrentDictionary<string, Lazy<CoalescedDownloadOperation>> pendingDownloads = new(StringComparer.Ordinal);
    private readonly HttpClient httpClient;
    private readonly string cacheDirectory;
    private readonly ILogger<ImageCacheService>? logger;
    private readonly object diskCleanupLock = new();
    private DateTime lastDiskCleanup = DateTime.MinValue;

    /// <summary>
    /// Gets or sets the singleton instance of <see cref="IImageCacheService"/>.
    /// </summary>
    public static IImageCacheService Instance
    {
        get
        {
            if (instance == null)
            {
                lock (InstanceLock)
                {
                    if (instance == null)
                    {
                        var resolved = AppLocator.GetServiceOrDefault<IImageCacheService>();
                        instance = resolved ?? new ImageCacheService();
                    }
                }
            }

            return instance;
        }

        set
        {
            lock (InstanceLock)
            {
                instance = value;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCacheService"/> class using the configured application-data directory.
    /// </summary>
    public ImageCacheService()
        : this(AppLocator.GetServiceOrDefault<IConfigurationProviderService>(), null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCacheService"/> class with an explicit configuration provider.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider to supply the root cache directory.</param>
    public ImageCacheService(IConfigurationProviderService? configurationProvider)
        : this(configurationProvider, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCacheService"/> class with configuration and logger.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider to supply the root cache directory.</param>
    /// <param name="logger">Optional logger for recording diagnostic and error events.</param>
    public ImageCacheService(
        IConfigurationProviderService? configurationProvider,
        ILogger<ImageCacheService>? logger)
        : this(configurationProvider, null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCacheService"/> class with an explicit configuration provider and HTTP client.
    /// Internal constructor for test isolation. Public callers are prevented from providing an unprotected client override.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider to supply the root cache directory.</param>
    /// <param name="httpClient">Optional custom <see cref="HttpClient"/> instance.</param>
    internal ImageCacheService(IConfigurationProviderService? configurationProvider, HttpClient? httpClient)
        : this(configurationProvider, httpClient, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageCacheService"/> class with configuration, HTTP client, and logger.
    /// Internal constructor for test isolation. Public callers are prevented from providing an unprotected client override.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider to supply the root cache directory.</param>
    /// <param name="httpClient">Optional custom <see cref="HttpClient"/> instance.</param>
    /// <param name="logger">Optional logger for recording diagnostic and error events.</param>
    internal ImageCacheService(
        IConfigurationProviderService? configurationProvider,
        HttpClient? httpClient,
        ILogger<ImageCacheService>? logger)
    {
        this.logger = logger;
        this.httpClient = httpClient ?? CreateDefaultHttpClient();

        try
        {
            var appDataPath = configurationProvider?.GetApplicationDataPath();
            if (string.IsNullOrWhiteSpace(appDataPath))
            {
                appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GenHub");
            }

            cacheDirectory = Path.Combine(appDataPath, "Images");
            Directory.CreateDirectory(cacheDirectory);
        }
        catch (Exception ex)
        {
            this.logger?.LogWarning(ex, "Failed to initialize image disk cache directory. Operating in memory-only mode.");
            cacheDirectory = string.Empty;
        }
    }

    /// <summary>
    /// Synchronously retrieves a bitmap from the in-memory LRU cache, returning null if not cached.
    /// </summary>
    /// <param name="url">The image URI or file path.</param>
    /// <returns>The cached <see cref="Bitmap"/>, or null if not found in memory.</returns>
    public Bitmap? GetBitmapFromMemory(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return memoryCache.TryGet(url);
    }

    /// <summary>
    /// Asynchronously retrieves an image as a <see cref="Bitmap"/>, checking the memory cache,
    /// local disk cache, and finally downloading from the network if necessary.
    /// </summary>
    /// <param name="url">The image URI (http/https, avares, or file path).</param>
    /// <param name="cancellationToken">Optional token to cancel the asynchronous operation.</param>
    /// <returns>A task returning the loaded <see cref="Bitmap"/>, or null if loading/download fails.</returns>
    public async Task<Bitmap?> GetBitmapAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var cached = memoryCache.TryGet(url);
        if (cached != null)
        {
            return cached;
        }

        if (url.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            return await LoadBitmapFromAvaloniaAssetAsync(url).ConfigureAwait(false);
        }

        if (url.StartsWith("/Assets/", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            var cleanPath = "Assets/" + url.TrimStart('/')["Assets/".Length..];
            return await LoadBitmapFromAvaloniaAssetAsync(
                $"avares://{typeof(ImageCacheService).Assembly.GetName().Name}/{cleanPath}",
                url).ConfigureAwait(false);
        }

        if (TryResolveSafeLocalFilePath(url, out var localPath))
        {
            return await TryLoadLocalFileImageAsync(url, localPath, cancellationToken).ConfigureAwait(false);
        }

        if (!IsSafeRemoteUrl(url, out _))
        {
            logger?.LogWarning("Refusing to download image from disallowed or unsafe URL: {Url}", url);
            return null;
        }

        return await GetOrDownloadRemoteImageAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Clears the in-memory cache without disposing bitmaps that may still be referenced by UI controls.
    /// </summary>
    public void ClearMemoryCache() => memoryCache.Clear();

    /// <summary>
    /// Validates whether a given URL string is a safe remote HTTP or HTTPS URI.
    /// </summary>
    /// <param name="url">The URL string to evaluate.</param>
    /// <param name="uri">When this method returns, contains the parsed URI if valid; otherwise, null.</param>
    /// <returns><c>true</c> if the URL is a safe remote address; otherwise, <c>false</c>.</returns>
    internal static bool IsSafeRemoteUrl(string url, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.HostNameType != UriHostNameType.Dns &&
            uri.HostNameType != UriHostNameType.IPv4 &&
            uri.HostNameType != UriHostNameType.IPv6)
        {
            return false;
        }

        if (uri.IsLoopback ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IPAddress.TryParse(uri.DnsSafeHost, out var ip) || IPAddress.TryParse(uri.Host, out ip))
        {
            return IsSafeIpAddress(ip);
        }

        return true;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (Uri.CheckHostName(context.DnsEndPoint.Host) == UriHostNameType.Unknown)
                {
                    throw new HttpRequestException($"Invalid host name: '{context.DnsEndPoint.Host}'.");
                }

                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
                if (addresses.Length == 0 || !addresses.All(IsSafeIpAddress))
                {
                    throw new HttpRequestException($"Host '{context.DnsEndPoint.Host}' resolved to an unsafe or invalid IP address.");
                }

                var safeIp = addresses[0];
                var socket = new System.Net.Sockets.Socket(safeIp.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(safeIp, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                    return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(ImageCacheConstants.DefaultTimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        return client;
    }

    private static bool IsSafeIPv4(byte[] b)
    {
        // 0.0.0.0/8, 10.0.0.0/8, 127.0.0.0/8, Multicast (224-239), Reserved (240+)
        // 100.64.0.0/10 Carrier-grade NAT
        // 169.254.0.0/16 Link-local / Cloud metadata (AWS/Azure/GCP)
        // 172.16.0.0/12 Private-use networks
        // 192.0.0.0/24 IETF Protocol Assignments
        // 192.0.2.0/24 TEST-NET-1
        // 192.168.0.0/16 Private-use networks
        // 198.18.0.0/15 Benchmarking
        // 198.51.100.0/24 TEST-NET-2
        // 203.0.113.0/24 TEST-NET-3
        return (b[0], b[1], b[2]) switch
        {
            (0 or 10 or 127, _, _) => false,
            (>= 224, _, _) => false,
            (100, >= 64 and <= 127, _) => false,
            (169, 254, _) => false,
            (172, >= 16 and <= 31, _) => false,
            (192, 0, 0 or 2) => false,
            (192, 168, _) => false,
            (198, 18 or 19, _) => false,
            (198, 51, 100) => false,
            (203, 0, 113) => false,
            _ => true,
        };
    }

    private static bool IsSafeIPv6(byte[] b)
    {
        // ::1 Loopback
        if (b.Take(15).All(x => x == 0) && b[15] == 1)
        {
            return false;
        }

        // :: Unspecified
        if (b.All(x => x == 0))
        {
            return false;
        }

        // fc00::/7 Unique local addresses
        if ((b[0] & 0xfe) == 0xfc)
        {
            return false;
        }

        // fe80::/10 Link-local unicast
        if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80)
        {
            return false;
        }

        // ff00::/8 Multicast
        if (b[0] == 0xff)
        {
            return false;
        }

        // ::ffff:0:0/96 IPv4-mapped IPv6
        if (b.Take(10).All(x => x == 0) && b[10] == 0xff && b[11] == 0xff)
        {
            return IsSafeIPv4(b.Skip(12).ToArray());
        }

        return true;
    }

    private static bool IsSafeIpAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length switch
        {
            4 => IsSafeIPv4(bytes),
            16 => IsSafeIPv6(bytes),
            _ => false,
        };
    }

    private static List<FileInfo> DeleteExpiredCacheFiles(FileInfo[] files)
    {
        var cutoff = DateTime.UtcNow.AddDays(-ImageCacheConstants.DiskCacheTtlDays);
        var tmpCutoff = DateTime.UtcNow.AddHours(-1);
        var activeFiles = new List<FileInfo>(files.Length);

        foreach (var file in files)
        {
            var isTmp = file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase);
            if (file.LastWriteTimeUtc < (isTmp ? tmpCutoff : cutoff))
            {
                TryDeleteFile(file.FullName);
            }
            else
            {
                activeFiles.Add(file);
            }
        }

        return activeFiles;
    }

    private static void EnforceMaxDiskCacheSize(List<FileInfo> files)
    {
        long totalSize = files.Sum(f => f.Length);
        if (totalSize <= ImageCacheConstants.MaxDiskCacheSizeBytes)
        {
            return;
        }

        var sorted = files.OrderBy(f => f.LastWriteTimeUtc);
        var targetSize = (long)(ImageCacheConstants.MaxDiskCacheSizeBytes * 0.8);

        foreach (var file in sorted)
        {
            if (totalSize <= targetSize)
            {
                break;
            }

            try
            {
                var len = file.Length;
                file.Delete();
                totalSize -= len;
            }
            catch
            {
                // File may be locked by concurrent read; continue with next
            }
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort deletion
        }
    }

    private static long CalculateBitmapBytes(Bitmap bitmap)
    {
        var width = (long)bitmap.PixelSize.Width;
        var height = (long)bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        return width * height * 4;
    }

    private static bool TryResolveSafeLocalFilePath(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? localPath)
    {
        localPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // Explicitly reject UNC paths and network shares (e.g. \attacker\share or //attacker/share)
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var fileUri) && fileUri.IsFile && !fileUri.IsUnc)
            {
                localPath = fileUri.LocalPath;
                return !localPath.StartsWith(@"\\", StringComparison.Ordinal) && !localPath.StartsWith("//", StringComparison.Ordinal);
            }

            return false;
        }

        if (Path.IsPathRooted(path))
        {
            localPath = path;
            return true;
        }

        return false;
    }

    private Bitmap? CreateAndValidateBitmap(Stream stream)
    {
        try
        {
            if (stream.CanSeek)
            {
                var initialPosition = stream.Position;
                try
                {
                    var imageInfo = Image.Identify(stream);
                    if (imageInfo != null)
                    {
                        var estimatedBytes = (long)imageInfo.Width * imageInfo.Height * 4;
                        if (estimatedBytes > ImageCacheConstants.MaxDecodedImageSizeBytes)
                        {
                            logger?.LogWarning(
                                "Image dimensions ({Width}x{Height}) exceed maximum decoded size limit ({EstimatedBytes} bytes > {Max} bytes)",
                                imageInfo.Width,
                                imageInfo.Height,
                                estimatedBytes,
                                ImageCacheConstants.MaxDecodedImageSizeBytes);
                            return null;
                        }
                    }
                }
                catch
                {
                    // Fall back to Avalonia decode if ImageSharp cannot identify the image format.
                }
                finally
                {
                    stream.Position = initialPosition;
                }
            }

            var bitmap = new Bitmap(stream);
            var byteCost = CalculateBitmapBytes(bitmap);
            if (byteCost > ImageCacheConstants.MaxDecodedImageSizeBytes)
            {
                logger?.LogWarning(
                    "Decoded image exceeds maximum size limit ({Size} bytes > {Max} bytes)",
                    byteCost,
                    ImageCacheConstants.MaxDecodedImageSizeBytes);
                bitmap.Dispose();
                return null;
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to decode image");
            return null;
        }
    }

    private Task<Bitmap?> LoadBitmapFromAvaloniaAssetAsync(string url, string? cacheKey = null)
    {
        try
        {
            var uri = new Uri(url);
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                var bitmap = CreateAndValidateBitmap(stream);
                if (bitmap != null)
                {
                    memoryCache.AddOrUpdate(cacheKey ?? url, bitmap);
                }

                return Task.FromResult<Bitmap?>(bitmap);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load asset image '{Uri}'", url);
        }

        return Task.FromResult<Bitmap?>(null);
    }

    private async Task<Bitmap?> TryLoadLocalFileImageAsync(string cacheKey, string localPath, CancellationToken cancellationToken)
    {
        if (localPath.StartsWith(@"\\", StringComparison.Ordinal) || localPath.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        if (!File.Exists(localPath))
        {
            return null;
        }

        var fileInfo = new FileInfo(localPath);
        if (fileInfo.Length > ImageCacheConstants.MaxImageDownloadSizeBytes)
        {
            logger?.LogWarning("Local image file '{Path}' exceeds maximum allowed size ({Size} bytes)", localPath, fileInfo.Length);
            return null;
        }

        try
        {
            using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            await fs.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            ms.Position = 0;
            var localBitmap = CreateAndValidateBitmap(ms);
            if (localBitmap != null)
            {
                memoryCache.AddOrUpdate(cacheKey, localBitmap);
            }

            return localBitmap;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load local file image '{Path}'", localPath);
            return null;
        }
    }

    private async Task<Bitmap?> GetOrDownloadRemoteImageAsync(string url, CancellationToken cancellationToken)
    {
        var diskPath = GetDiskCachePath(url);
        var diskBitmap = await TryLoadFromDiskCacheAsync(diskPath, cancellationToken).ConfigureAwait(false);
        if (diskBitmap != null)
        {
            memoryCache.AddOrUpdate(url, diskBitmap);
            return diskBitmap;
        }

        return await CoalesceDownloadAsync(url, diskPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Bitmap?> TryLoadFromDiskCacheAsync(string diskPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
        {
            return null;
        }

        var fileInfo = new FileInfo(diskPath);
        if (fileInfo.Length > ImageCacheConstants.MaxImageDownloadSizeBytes)
        {
            logger?.LogWarning("Cached disk image '{Path}' exceeds maximum allowed size ({Size} bytes)", diskPath, fileInfo.Length);
            TryDeleteFile(diskPath);
            return null;
        }

        var cutoff = DateTime.UtcNow.AddDays(-ImageCacheConstants.DiskCacheTtlDays);
        if (fileInfo.LastWriteTimeUtc < cutoff)
        {
            TryDeleteFile(diskPath);
            return null;
        }

        try
        {
            fileInfo.LastWriteTimeUtc = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(ex, "Failed to update disk cache timestamp for '{Path}'", diskPath);
        }

        try
        {
            using var fs = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream();
            await fs.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            ms.Position = 0;
            return CreateAndValidateBitmap(ms);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to read cached image from disk '{Path}'", diskPath);
            return null;
        }
    }

    private async Task<Bitmap?> CoalesceDownloadAsync(string url, string diskPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IDisposable? waiterRegistration = null;
        CoalescedDownloadOperation? operation = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pendingDownloads.TryGetValue(url, out var existing))
            {
                if (!existing.Value.Task.IsCompleted &&
                    existing.Value.TryAddWaiter(cancellationToken, out waiterRegistration))
                {
                    operation = existing.Value;
                    break;
                }

                pendingDownloads.TryRemove(KeyValuePair.Create(url, existing));
            }

            Lazy<CoalescedDownloadOperation>? created = null;
            created = new Lazy<CoalescedDownloadOperation>(
                () => new CoalescedDownloadOperation(token => DownloadAndCacheAsync(url, diskPath, created!, token)),
                LazyThreadSafetyMode.ExecutionAndPublication);

            if (pendingDownloads.TryAdd(url, created))
            {
                if (created.Value.TryAddWaiter(cancellationToken, out waiterRegistration))
                {
                    operation = created.Value;
                    break;
                }

                pendingDownloads.TryRemove(KeyValuePair.Create(url, created));
            }
        }

        using (waiterRegistration)
        {
            try
            {
                return await operation.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }
    }

    private async Task<Bitmap?> DownloadAndCacheAsync(
        string initialUrl,
        string diskPath,
        Lazy<CoalescedDownloadOperation> lazyOperation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await ExecuteRequestWithRedirectsAsync(initialUrl, cancellationToken).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode)
            {
                return null;
            }

            var imageBytes = await ReadValidatedImageBytesAsync(response, cancellationToken).ConfigureAwait(false);
            if (imageBytes == null)
            {
                return null;
            }

            using var decodeStream = new MemoryStream(imageBytes);
            var bitmap = CreateAndValidateBitmap(decodeStream);
            if (bitmap == null)
            {
                return null;
            }

            memoryCache.AddOrUpdate(initialUrl, bitmap);
            await SaveImageBytesToDiskAtomicAsync(diskPath, imageBytes, cancellationToken).ConfigureAwait(false);
            TriggerDiskCleanupIfNeeded();

            return bitmap;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Error downloading or decoding image '{Url}'", initialUrl);
            return null;
        }
        finally
        {
            pendingDownloads.TryRemove(KeyValuePair.Create(initialUrl, lazyOperation));
        }
    }

    private async Task<HttpResponseMessage?> ExecuteRequestWithRedirectsAsync(string initialUrl, CancellationToken cancellationToken)
    {
        var currentUrl = initialUrl;
        for (var redirectCount = 0; redirectCount <= ImageCacheConstants.MaxRedirects; redirectCount++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsSafeRemoteUrl(currentUrl, out var uri))
            {
                logger?.LogWarning("Redirect blocked to unsafe or invalid URL '{Url}'", currentUrl);
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (uri.Host.Contains("moddb.com", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Referrer = ModDbReferrerUri;
            }

            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (TryGetRedirectTarget(response, uri, out var nextUrl, out var isBlocked))
            {
                response.Dispose();
                currentUrl = nextUrl;
                continue;
            }

            if (isBlocked)
            {
                response.Dispose();
                return null;
            }

            return response;
        }

        logger?.LogWarning("Too many redirects for image '{Url}'", initialUrl);
        return null;
    }

    private bool TryGetRedirectTarget(
        HttpResponseMessage response,
        Uri currentUri,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? nextUrl,
        out bool isBlocked)
    {
        nextUrl = null;
        isBlocked = false;

        if ((int)response.StatusCode is not (>= 300 and <= 399) || response.Headers.Location == null)
        {
            return false;
        }

        var nextUri = response.Headers.Location.IsAbsoluteUri
            ? response.Headers.Location
            : new Uri(currentUri, response.Headers.Location);

        if (currentUri.Scheme == Uri.UriSchemeHttps && nextUri.Scheme == Uri.UriSchemeHttp)
        {
            logger?.LogWarning("Redirect from HTTPS to HTTP blocked: '{Url}' -> '{NextUrl}'", currentUri, nextUri);
            isBlocked = true;
            return false;
        }

        nextUrl = nextUri.AbsoluteUri;
        return true;
    }

    private async Task<byte[]?> ReadValidatedImageBytesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > ImageCacheConstants.MaxImageDownloadSizeBytes)
        {
            logger?.LogWarning("Image at '{Url}' exceeds maximum allowed size ({Size} bytes)", response.RequestMessage?.RequestUri, contentLength.Value);
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType != null && !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            logger?.LogWarning("Response at '{Url}' is not an image (Content-Type: '{Type}')", response.RequestMessage?.RequestUri, contentType);
            return null;
        }

        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytesRead = 0;
        int read;

        while ((read = await responseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalBytesRead += read;
            if (totalBytesRead > ImageCacheConstants.MaxImageDownloadSizeBytes)
            {
                logger?.LogWarning("Image download aborted: payload exceeded maximum limit of {Max} bytes", ImageCacheConstants.MaxImageDownloadSizeBytes);
                return null;
            }

            await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return ms.ToArray();
    }

    private async Task SaveImageBytesToDiskAtomicAsync(string diskPath, byte[] imageBytes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(diskPath))
        {
            return;
        }

        var dir = Path.GetDirectoryName(diskPath);
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(dir);
            tempPath = Path.Combine(dir, $"{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(tempPath, imageBytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, diskPath, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDeleteFile(tempPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempPath);
            logger?.LogWarning(ex, "Failed to write disk cache file '{Path}'", diskPath);
        }
    }

    private string GetDiskCachePath(string url)
    {
        if (string.IsNullOrEmpty(cacheDirectory))
        {
            return string.Empty;
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        var sb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return Path.Combine(cacheDirectory, sb.ToString() + ".img");
    }

    private void TriggerDiskCleanupIfNeeded()
    {
        if (string.IsNullOrEmpty(cacheDirectory) || DateTime.UtcNow - lastDiskCleanup < TimeSpan.FromHours(1))
        {
            return;
        }

        _ = Task.Run(PerformDiskCleanup);
    }

    private void PerformDiskCleanup()
    {
        lock (diskCleanupLock)
        {
            if (DateTime.UtcNow - lastDiskCleanup < TimeSpan.FromHours(1))
            {
                return;
            }

            lastDiskCleanup = DateTime.UtcNow;

            try
            {
                if (!Directory.Exists(cacheDirectory))
                {
                    return;
                }

                var di = new DirectoryInfo(cacheDirectory);
                var cacheFiles = di.GetFiles("*.img")
                    .Concat(di.GetFiles("*.tmp"))
                    .ToArray();
                var activeFiles = DeleteExpiredCacheFiles(cacheFiles);
                EnforceMaxDiskCacheSize(activeFiles);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Disk cache cleanup failed");
            }
        }
    }

    /// <summary>
    /// Thread-safe bounded LRU memory cache for bitmaps.
    /// Eviction removes the reference from cache without calling Dispose,
    /// so any views or view models currently holding a reference to the Bitmap can continue safely.
    /// Avalonia's native bitmap memory is released by GC finalization when no longer referenced.
    /// </summary>
    private sealed class LruMemoryCache(int maxCapacity, long maxBytes = ImageCacheConstants.MaxMemoryCacheSizeBytes)
    {
        private readonly Dictionary<string, LinkedListNode<CacheItem>> cache = new(StringComparer.Ordinal);
        private readonly LinkedList<CacheItem> lruList = new();
        private readonly object syncLock = new();
        private long currentBytes;

        public Bitmap? TryGet(string key)
        {
            lock (syncLock)
            {
                if (cache.TryGetValue(key, out var node))
                {
                    lruList.Remove(node);
                    lruList.AddFirst(node);
                    return node.Value.Bitmap;
                }

                return null;
            }
        }

        public void AddOrUpdate(string key, Bitmap bitmap)
        {
            var byteCost = CalculateBitmapBytes(bitmap);
            if (byteCost > maxBytes)
            {
                return;
            }

            lock (syncLock)
            {
                if (cache.TryGetValue(key, out var existingNode))
                {
                    lruList.Remove(existingNode);
                    cache.Remove(key);
                    currentBytes -= existingNode.Value.ByteCost;
                }

                while ((cache.Count >= maxCapacity || currentBytes + byteCost > maxBytes) && lruList.Count > 0)
                {
                    var last = lruList.Last;
                    if (last == null)
                    {
                        break;
                    }

                    lruList.RemoveLast();
                    cache.Remove(last.Value.Key);
                    currentBytes -= last.Value.ByteCost;

                    // Do NOT dispose bitmap. Active controls may still render it.
                }

                var node = new LinkedListNode<CacheItem>(new CacheItem(key, bitmap, byteCost));
                lruList.AddFirst(node);
                cache[key] = node;
                currentBytes += byteCost;
            }
        }

        public void Clear()
        {
            lock (syncLock)
            {
                // Do NOT dispose bitmaps. Active controls may still render them.
                cache.Clear();
                lruList.Clear();
                currentBytes = 0;
            }
        }

        private readonly record struct CacheItem(string Key, Bitmap Bitmap, long ByteCost);
    }

    /// <summary>
    /// Represents an in-flight coalesced image download with waiter count tracking and linked cancellation.
    /// </summary>
    private sealed class CoalescedDownloadOperation : IDisposable
    {
        private readonly object syncLock = new();
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private int waiterCount;
        private bool cancelQueued;

        public CoalescedDownloadOperation(Func<CancellationToken, Task<Bitmap?>> downloadFactory)
        {
            Task = downloadFactory(cancellationTokenSource.Token);
            _ = Task.ContinueWith(
                _ => cancellationTokenSource.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }

        public Task<Bitmap?> Task { get; }

        public void Dispose()
        {
            cancellationTokenSource.Dispose();
        }

        public bool TryAddWaiter(CancellationToken callerToken, out IDisposable? registration)
        {
            if (callerToken.IsCancellationRequested)
            {
                registration = null;
                return false;
            }

            lock (syncLock)
            {
                if (cancelQueued || callerToken.IsCancellationRequested)
                {
                    registration = null;
                    return false;
                }

                waiterCount++;
                registration = new WaiterRegistration(this, callerToken);
                return true;
            }
        }

        private void RemoveWaiter()
        {
            var shouldCancel = false;
            lock (syncLock)
            {
                waiterCount--;
                if (waiterCount <= 0 && !Task.IsCompleted)
                {
                    cancelQueued = true;
                    shouldCancel = true;
                }
            }

            if (shouldCancel)
            {
                try
                {
                    cancellationTokenSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if CancellationTokenSource was already disposed concurrently.
                }
            }
        }

        private sealed class WaiterRegistration : IDisposable
        {
            private readonly CoalescedDownloadOperation operation;
            private readonly CancellationTokenRegistration registration;
            private int removed;

            public WaiterRegistration(CoalescedDownloadOperation owner, CancellationToken callerToken)
            {
                operation = owner;
                if (callerToken.CanBeCanceled)
                {
                    registration = callerToken.Register(OnCancelled);
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref removed, 1) == 0)
                {
                    registration.Dispose();
                    operation.RemoveWaiter();
                }
            }

            private void OnCancelled()
            {
                Dispose();
            }
        }
    }
}
