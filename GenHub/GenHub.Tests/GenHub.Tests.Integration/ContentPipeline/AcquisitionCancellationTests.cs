using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Tests.Integration.Infrastructure;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Integration.ContentPipeline;

/// <summary>
/// Cancels a live acquisition while it downloads and checks nothing is left behind.
/// </summary>
[Trait("Category", "LiveNetwork")]
public sealed class AcquisitionCancellationTests : IDisposable
{
    private const long MinimumPackageBytes = 100L * 1024 * 1024;

    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(3);

    private readonly LiveContentHost _host = new();
    private readonly CancellationTokenSource _timeout = new(TestTimeout);

    /// <inheritdoc/>
    public void Dispose()
    {
        _timeout.Dispose();
        _host.Dispose();
    }

    /// <summary>
    /// Cancelling once the first bytes arrive leaves no CAS objects, staging files or acquired manifests.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task AcquireLargePackage_CancelledMidDownload_LeavesNoCasObjectsOrStagingFilesAsync()
    {
        var results = await _host.SearchAsync(PublisherTypeConstants.GenLauncher, cancellationToken: _timeout.Token);
        var item = results.OrderByDescending(r => r.DownloadSize).First();
        Assert.True(
            item.DownloadSize >= MinimumPackageBytes,
            $"Largest GenLauncher package '{item.Name}' is only {item.DownloadSize} bytes; the cancel point would not be mid-download.");

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_timeout.Token);
        var progress = new CancelOnFirstBytes(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _host.Orchestrator.AcquireContentAsync(item, progress, cancellation.Token));

        Assert.InRange(progress.BytesAtCancel, 1, item.DownloadSize - 1);
        Assert.Empty(FilesUnder(_host.CasPath));
        Assert.Empty(FilesUnder(_host.TempPath));

        var acquired = await _host.Orchestrator.GetAcquiredContentAsync(_timeout.Token);
        Assert.True(acquired.Success, acquired.FirstError);
        Assert.Empty(acquired.Data!);
    }

    private static string[] FilesUnder(string path) =>
        Directory.Exists(path) ? Directory.GetFiles(path, "*", SearchOption.AllDirectories) : [];

    private sealed class CancelOnFirstBytes(CancellationTokenSource cancellation) : IProgress<ContentAcquisitionProgress>
    {
        internal long BytesAtCancel { get; private set; }

        public void Report(ContentAcquisitionProgress value)
        {
            if (BytesAtCancel == 0 && value.Phase == ContentAcquisitionPhase.Downloading && value.BytesProcessed > 0)
            {
                BytesAtCancel = value.BytesProcessed;
                cancellation.Cancel();
            }
        }
    }
}
