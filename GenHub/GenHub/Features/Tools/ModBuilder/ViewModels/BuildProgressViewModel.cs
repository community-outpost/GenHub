using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for build progress overlay with stage-by-stage visualization.
/// </summary>
public sealed partial class BuildProgressViewModel : ObservableObject, IDisposable
{
    private const string StageStatusPending = "Pending";
    private readonly Stopwatch _stopwatch = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildProgressViewModel"/> class.
    /// </summary>
    public BuildProgressViewModel()
    {
        Stages = [];
    }

    /// <summary>
    /// Gets or sets a value indicating whether the overlay is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>
    /// Gets or sets the project name.
    /// </summary>
    [ObservableProperty]
    private string _projectName = string.Empty;

    /// <summary>
    /// Gets or sets the current build stage.
    /// </summary>
    [ObservableProperty]
    private string _currentStage = string.Empty;

    /// <summary>
    /// Gets or sets the overall progress (0-100).
    /// </summary>
    [ObservableProperty]
    private double _overallProgress;

    /// <summary>
    /// Gets or sets the files processed per second.
    /// </summary>
    [ObservableProperty]
    private double _filesPerSecond;

    /// <summary>
    /// Gets or sets the number of cache hits.
    /// </summary>
    [ObservableProperty]
    private int _cacheHits;

    /// <summary>
    /// Gets or sets the total number of files.
    /// </summary>
    [ObservableProperty]
    private int _totalFiles;

    /// <summary>
    /// Gets or sets the elapsed time.
    /// </summary>
    [ObservableProperty]
    private string _elapsedTime = "00:00";

    /// <summary>
    /// Gets or sets the estimated time remaining.
    /// </summary>
    [ObservableProperty]
    private string _estimatedTimeRemaining = "--:--";

    /// <summary>
    /// Gets the collection of build stages.
    /// </summary>
    public ObservableCollection<ProgressCardViewModel> Stages { get; }

    /// <summary>
    /// Starts the build progress tracking.
    /// </summary>
    /// <param name="projectName">The project name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public void StartBuild(string projectName, CancellationToken cancellationToken)
    {
        ProjectName = projectName;
        IsVisible = true;
        OverallProgress = 0;
        CacheHits = 0;
        TotalFiles = 0;
        FilesPerSecond = 0;

        _stopwatch.Restart();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Initialize stages
        Stages.Clear();
        Stages.Add(new ProgressCardViewModel
        {
            Title = "Scanning Files",
            Icon = "IconScanning",
            Status = StageStatusPending,
        });
        Stages.Add(new ProgressCardViewModel
        {
            Title = "Converting Assets",
            Icon = "IconConverting",
            Status = StageStatusPending,
        });
        Stages.Add(new ProgressCardViewModel
        {
            Title = "Caching Results",
            Icon = "IconCaching",
            Status = StageStatusPending,
        });
        Stages.Add(new ProgressCardViewModel
        {
            Title = "Creating Archives",
            Icon = "IconArchiving",
            Status = StageStatusPending,
        });

        // Start timer for elapsed time updates
        _ = UpdateElapsedTimeAsync(_cancellationTokenSource.Token);
    }

    /// <summary>
    /// Updates the progress for a specific stage.
    /// </summary>
    /// <param name="stageIndex">The stage index (0-3).</param>
    /// <param name="progress">The progress (0-100).</param>
    /// <param name="message">The status message.</param>
    public void UpdateStageProgress(int stageIndex, double progress, string message)
    {
        if (stageIndex >= 0 && stageIndex < Stages.Count)
        {
            var stage = Stages[stageIndex];
            stage.Progress = progress;
            stage.Message = message;
            stage.Status = progress >= 100 ? "Completed" : "InProgress";

            // Update current stage
            if (progress < 100)
            {
                CurrentStage = stage.Title;
            }

            // Calculate overall progress (weighted by stage)
            OverallProgress = (stageIndex * 25) + (progress * 0.25);
        }
    }

    /// <summary>
    /// Updates the build metrics.
    /// </summary>
    /// <param name="filesProcessed">The number of files processed.</param>
    /// <param name="totalFiles">The total number of files.</param>
    /// <param name="cacheHits">The number of cache hits.</param>
    public void UpdateMetrics(int filesProcessed, int totalFiles, int cacheHits)
    {
        TotalFiles = totalFiles;
        CacheHits = cacheHits;

        // Calculate files per second
        var elapsed = _stopwatch.Elapsed.TotalSeconds;
        if (elapsed > 0)
        {
            FilesPerSecond = filesProcessed / elapsed;
        }

        // Estimate time remaining
        if (FilesPerSecond > 0 && totalFiles > filesProcessed)
        {
            var remainingFiles = totalFiles - filesProcessed;
            var secondsRemaining = remainingFiles / FilesPerSecond;
            EstimatedTimeRemaining = TimeSpan.FromSeconds(secondsRemaining).ToString(@"mm\:ss");
        }
        else
        {
            EstimatedTimeRemaining = "--:--";
        }
    }

    /// <summary>
    /// Completes the build progress.
    /// </summary>
    public void CompleteBuild()
    {
        _stopwatch.Stop();
        OverallProgress = 100;
        CurrentStage = "Build Complete";

        // Mark all stages as completed
        foreach (var stage in Stages)
        {
            stage.Status = "Completed";
            stage.Progress = 100;
        }

        // Hide overlay after a short delay
        Task.Delay(2000, CancellationToken.None).ContinueWith(
            _ => Dispatcher.UIThread.Post(() => IsVisible = false),
            TaskScheduler.Default);
    }

    /// <summary>
    /// Cancels the build.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        IsVisible = false;
    }

    /// <summary>
    /// Updates the elapsed time display.
    /// </summary>
    private async Task UpdateElapsedTimeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsVisible)
        {
            ElapsedTime = _stopwatch.Elapsed.ToString(@"mm\:ss");
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        GC.SuppressFinalize(this);
    }
}
