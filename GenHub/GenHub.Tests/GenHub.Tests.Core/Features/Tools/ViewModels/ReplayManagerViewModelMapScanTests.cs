using Avalonia.Headless.XUnit;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Tests that the Replay Manager shows replays before the map availability scan finishes and cancels stale scans.
/// </summary>
public sealed class ReplayManagerViewModelMapScanTests : IDisposable
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(10);

    private readonly Mock<IReplayDirectoryService> _directoryService = new();
    private readonly List<(CancellationToken Token, TaskCompletionSource<IReadOnlyList<ReplayMapAvailability>> Result)> _scans = [];
    private readonly ReplayManagerViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayManagerViewModelMapScanTests"/> class.
    /// </summary>
    public ReplayManagerViewModelMapScanTests()
    {
        _directoryService
            .Setup(d => d.GetReplaysAsync(GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => [CreateReplay()]);
        _directoryService
            .Setup(d => d.ResolveMapAvailabilityAsync(GameType.ZeroHour, It.IsAny<IReadOnlyList<ReplayFile>>(), It.IsAny<CancellationToken>()))
            .Returns<GameType, IReadOnlyList<ReplayFile>, CancellationToken>((_, _, token) =>
            {
                var result = new TaskCompletionSource<IReadOnlyList<ReplayMapAvailability>>(TaskCreationOptions.RunContinuationsAsynchronously);
                token.Register(() => result.TrySetCanceled(token));
                _scans.Add((token, result));
                return result.Task;
            });

        _viewModel = new ReplayManagerViewModel(
            _directoryService.Object,
            new Mock<IReplayCheckpointService>().Object,
            null,
            new Mock<IReplayImportService>().Object,
            new Mock<IReplayExportService>().Object,
            new Mock<IUploadHistoryService>().Object,
            new Mock<INotificationService>().Object,
            NullLogger<ReplayManagerViewModel>.Instance);
    }

    /// <inheritdoc />
    public void Dispose() => _viewModel.Dispose();

    /// <summary>
    /// Verifies that the replay list is shown while the map scan is still running, and that the map availability
    /// is filled in on the listed replays once the scan finishes.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task LoadReplaysAsync_ShowsReplaysBeforeMapScanCompletesAsync()
    {
        await _viewModel.LoadReplaysAsync().WaitAsync(LoadTimeout);

        var replay = Assert.Single(_viewModel.CurrentReplays);
        Assert.False(_viewModel.IsBusy);
        Assert.False(_viewModel.MapAvailabilityScan.IsCompleted);
        Assert.Equal(ReplayMapAvailability.Unknown, replay.MapAvailability);

        var changed = new List<string?>();
        replay.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Single(_scans).Result.SetResult([ReplayMapAvailability.Available]);
        await _viewModel.MapAvailabilityScan;

        Assert.Equal(ReplayMapAvailability.Available, replay.MapAvailability);
        Assert.Contains(nameof(ReplayFile.MapAvailability), changed);
    }

    /// <summary>
    /// Verifies that starting a new load cancels the map scan of the previous load, and that only the new scan
    /// fills in the replays.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task LoadReplaysAsync_NewLoad_CancelsPreviousMapScanAsync()
    {
        await _viewModel.LoadReplaysAsync().WaitAsync(LoadTimeout);
        var firstScan = _viewModel.MapAvailabilityScan;

        await _viewModel.LoadReplaysAsync().WaitAsync(LoadTimeout);

        Assert.Equal(2, _scans.Count);
        Assert.True(_scans[0].Token.IsCancellationRequested);
        Assert.False(_scans[1].Token.IsCancellationRequested);
        await firstScan;

        _scans[1].Result.SetResult([ReplayMapAvailability.NotFound]);
        await _viewModel.MapAvailabilityScan;

        Assert.Equal(ReplayMapAvailability.NotFound, Assert.Single(_viewModel.CurrentReplays).MapAvailability);
    }

    /// <summary>
    /// Verifies that closing the tool cancels a running map scan.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task Dispose_CancelsRunningMapScanAsync()
    {
        await _viewModel.LoadReplaysAsync().WaitAsync(LoadTimeout);
        var scan = _viewModel.MapAvailabilityScan;

        _viewModel.Dispose();

        Assert.True(Assert.Single(_scans).Token.IsCancellationRequested);
        await scan;
        Assert.Equal(ReplayMapAvailability.Unknown, Assert.Single(_viewModel.CurrentReplays).MapAvailability);
    }

    /// <summary>
    /// Verifies that a load superseded while it was still listing replays does not publish its stale list.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task LoadReplaysAsync_SupersededLoad_DoesNotPublishStaleListAsync()
    {
        var firstListing = new TaskCompletionSource<IReadOnlyList<ReplayFile>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listings = 0;
        _directoryService
            .Setup(d => d.GetReplaysAsync(GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .Returns(() => ++listings == 1 ? firstListing.Task : Task.FromResult<IReadOnlyList<ReplayFile>>([CreateReplay("new.rep")]));

        var firstLoad = _viewModel.LoadReplaysAsync();
        var secondLoad = _viewModel.LoadReplaysAsync();
        firstListing.SetResult([CreateReplay("stale.rep")]);
        await Task.WhenAll(firstLoad, secondLoad).WaitAsync(LoadTimeout);

        Assert.Equal(2, listings);
        Assert.Equal("new.rep", Assert.Single(_viewModel.CurrentReplays).FileName);
        Assert.Single(_scans);
    }

    /// <summary>
    /// Verifies that a reload queued behind a running load is dropped when the tool closes.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task Dispose_DropsQueuedReloadAsync()
    {
        var firstListing = new TaskCompletionSource<IReadOnlyList<ReplayFile>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listings = 0;
        _directoryService
            .Setup(d => d.GetReplaysAsync(GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                listings++;
                return firstListing.Task;
            });

        var firstLoad = _viewModel.LoadReplaysAsync();
        _ = _viewModel.LoadReplaysAsync();
        _viewModel.Dispose();
        firstListing.SetResult([CreateReplay()]);
        await firstLoad.WaitAsync(LoadTimeout);

        Assert.Equal(1, listings);
        Assert.Empty(_scans);
    }

    private static ReplayFile CreateReplay(string fileName = "match.rep") => new()
    {
        FullPath = "/replays/" + fileName,
        FileName = fileName,
        SizeInBytes = 2048,
        LastModified = DateTime.UtcNow,
        GameVersion = GameType.ZeroHour,
        Metadata = new ReplayMetadata { MapName = "tansooo", MapCrc = 0x6314F5FD, MapSize = 106110 },
    };
}
