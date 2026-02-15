using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Features.Tools.ReplayManager.ViewModels;

/// <summary>
/// ViewModel for the replay viewer window that displays parsed replay metadata.
/// </summary>
public sealed partial class ReplayViewerViewModel : ObservableObject
{
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F2} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration == null)
        {
            return "Unknown";
        }

        var ts = duration.Value;
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{ts.Minutes}m {ts.Seconds}s";
        }

        return $"{ts.Seconds}s";
    }

    /// <summary>
    /// Event raised when the window should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets the replay metadata to display.
    /// </summary>
    public ReplayMetadata Metadata { get; }

    /// <summary>
    /// Gets the file path of the replay.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the formatted file size.
    /// </summary>
    public string FormattedFileSize => FormatFileSize(Metadata.FileSizeBytes);

    /// <summary>
    /// Gets the formatted duration.
    /// </summary>
    public string FormattedDuration => FormatDuration(Metadata.Duration);

    /// <summary>
    /// Gets the formatted game date.
    /// </summary>
    public string FormattedGameDate => Metadata.GameDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown";

    /// <summary>
    /// Gets the player list.
    /// </summary>
    public IReadOnlyList<string> Players => Metadata.Players ?? Array.Empty<string>();

    /// <summary>
    /// Gets the player count.
    /// </summary>
    public int PlayerCount => Players.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayViewerViewModel"/> class.
    /// </summary>
    /// <param name="metadata">The parsed replay metadata.</param>
    /// <param name="filePath">The file path of the replay.</param>
    public ReplayViewerViewModel(ReplayMetadata metadata, string filePath)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>
    /// Command to close the window.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
