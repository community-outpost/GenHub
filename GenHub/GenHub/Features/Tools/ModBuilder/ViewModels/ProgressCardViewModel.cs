using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for individual progress cards.
/// </summary>
public partial class ProgressCardViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the card title / stage name.
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Gets or sets the stage name.
    /// </summary>
    [ObservableProperty]
    private string _stageName = string.Empty;

    /// <summary>
    /// Gets or sets the stage description.
    /// </summary>
    [ObservableProperty]
    private string _stageDescription = string.Empty;

    /// <summary>
    /// Gets or sets the icon key.
    /// </summary>
    [ObservableProperty]
    private string _icon = string.Empty;

    /// <summary>
    /// Gets or sets the stage icon geometry.
    /// </summary>
    [ObservableProperty]
    private Geometry? _stageIcon;

    /// <summary>
    /// Gets or sets the stage background brush.
    /// </summary>
    [ObservableProperty]
    private IBrush? _stageColor;

    /// <summary>
    /// Gets or sets a value indicating whether the stage is currently active.
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// Gets or sets the status text.
    /// </summary>
    [ObservableProperty]
    private string _statusText = "Pending";

    /// <summary>
    /// Gets or sets the status badge background.
    /// </summary>
    [ObservableProperty]
    private IBrush? _statusBackground;

    /// <summary>
    /// Gets or sets the status badge foreground.
    /// </summary>
    [ObservableProperty]
    private IBrush? _statusForeground;

    /// <summary>
    /// Gets or sets the status (Pending, InProgress, Completed).
    /// </summary>
    [ObservableProperty]
    private string _status = "Pending";

    /// <summary>
    /// Gets or sets the progress (0-100).
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// Gets or sets the progress bar pixel width.
    /// </summary>
    [ObservableProperty]
    private double _progressWidth;

    /// <summary>
    /// Gets or sets the number of files processed.
    /// </summary>
    [ObservableProperty]
    private int _filesProcessed;

    /// <summary>
    /// Gets or sets the processing speed in items/sec.
    /// </summary>
    [ObservableProperty]
    private double _processingSpeed;

    /// <summary>
    /// Gets or sets estimated time remaining.
    /// </summary>
    [ObservableProperty]
    private TimeSpan _timeRemaining = TimeSpan.Zero;

    /// <summary>
    /// Gets or sets a value indicating whether there is an active current file.
    /// </summary>
    [ObservableProperty]
    private bool _hasCurrentFile;

    /// <summary>
    /// Gets or sets the current file name being processed.
    /// </summary>
    [ObservableProperty]
    private string _currentFile = string.Empty;

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    private string _message = string.Empty;
}
