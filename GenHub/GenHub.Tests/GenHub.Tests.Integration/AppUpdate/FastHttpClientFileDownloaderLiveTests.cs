using GenHub.Core.Constants;
using GenHub.Features.AppUpdate.Services;
using GenHub.Tests.Integration.Infrastructure;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Integration.AppUpdate;

/// <summary>
/// Downloads a published GenHub release asset with the parallel range downloader.
/// </summary>
[Trait("Category", "LiveNetwork")]
public sealed class FastHttpClientFileDownloaderLiveTests : IDisposable
{
    // Pin the asset and digest together so this test verifies stable bytes, not the latest release.
    private const string ReleaseAssetUrl = "https://github.com/community-outpost/GenHub/releases/download/v0.0.3/GenHub-0.0.3-full.nupkg";

    private const string ReleaseAssetSha256 = "75981a34cbdecc29a2109ce62c2729c3249ea47e63764abbec1390038e933f7e";

    private const double RequestTimeoutSeconds = 120;

    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(5);

    private readonly LiveContentHost _host = new();
    private readonly CancellationTokenSource _timeout = new(TestTimeout);

    /// <inheritdoc/>
    public void Dispose()
    {
        _timeout.Dispose();
        _host.Dispose();
    }

    /// <summary>
    /// Every request is a ranged 206 response and the result matches a single-stream download byte for byte.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DownloadFile_ReleaseAsset_UsesRangedRequestsAndMatchesSingleStreamAsync()
    {
        using var recorder = new RecordingHandler();
        var downloader = new FastHttpClientFileDownloader(httpMessageHandler: recorder);
        var targetFile = Path.Combine(_host.TempPath, "parallel.nupkg");
        var progress = new ConcurrentQueue<int>();

        await downloader.DownloadFile(ReleaseAssetUrl, targetFile, progress.Enqueue, null, RequestTimeoutSeconds, _timeout.Token);

        var length = new FileInfo(targetFile).Length;
        var expectedChunks = (int)Math.Ceiling((double)length / AppUpdateConstants.DownloadChunkSizeBytes);
        var responses = recorder.Responses.ToList();
        Assert.True(length >= AppUpdateConstants.ParallelDownloadThresholdBytes, $"Asset is {length} bytes, below the parallel threshold.");
        Assert.Equal(expectedChunks + 1, responses.Count);
        Assert.All(responses, r => Assert.True(r.Ranged && r.Status == HttpStatusCode.PartialContent, $"Request was ranged={r.Ranged} status={r.Status}."));
        Assert.Equal(progress.OrderBy(p => p), progress);
        Assert.Equal(100, progress.Last());

        var parallelHash = await LiveContentHost.ComputeSha256Async(targetFile, _timeout.Token);
        var singleStreamHash = await DownloadSingleStreamSha256Async(_timeout.Token);
        Assert.Equal(singleStreamHash, parallelHash);
        Assert.Equal(ReleaseAssetSha256, parallelHash);
    }

    private static async Task<string> DownloadSingleStreamSha256Async(CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };
        using var response = await client.GetAsync(ReleaseAssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class RecordingHandler() : DelegatingHandler(new SocketsHttpHandler())
    {
        internal ConcurrentQueue<(bool Ranged, HttpStatusCode Status)> Responses { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            Responses.Enqueue((request.Headers.Range is not null, response.StatusCode));
            return response;
        }
    }
}
