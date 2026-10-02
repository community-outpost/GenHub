using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using System;
using System.Windows.Input;

namespace GenHub.Common.Controls;

/// <summary>
/// Reusable in-app video player streaming a remote video URL through LibVLC.
/// Playback state is exposed as bindable properties so all user-facing text
/// stays in XAML localization bindings; only numeric progress is formatted here.
/// </summary>
public partial class VideoPlayerView : UserControl
{
    /// <summary>
    /// Defines the <see cref="SourceUrl"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<VideoPlayerView, string?>(nameof(SourceUrl));

    /// <summary>
    /// Defines the <see cref="FallbackCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> FallbackCommandProperty =
        AvaloniaProperty.Register<VideoPlayerView, ICommand?>(nameof(FallbackCommand));

    /// <summary>
    /// Defines the <see cref="IsLoading"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, bool>(nameof(IsLoading), o => o.IsLoading);

    /// <summary>
    /// Defines the <see cref="HasError"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, bool> HasErrorProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, bool>(nameof(HasError), o => o.HasError);

    /// <summary>
    /// Defines the <see cref="IsUnavailable"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, bool> IsUnavailableProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, bool>(nameof(IsUnavailable), o => o.IsUnavailable);

    /// <summary>
    /// Defines the <see cref="IsPlaying"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, bool> IsPlayingProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, bool>(nameof(IsPlaying), o => o.IsPlaying);

    /// <summary>
    /// Defines the <see cref="IsMuted"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, bool> IsMutedProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, bool>(nameof(IsMuted), o => o.IsMuted);

    /// <summary>
    /// Defines the <see cref="PositionText"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, string> PositionTextProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, string>(nameof(PositionText), o => o.PositionText);

    private static readonly int PositionScale = 1000;
    private static readonly int PositionPollIntervalMs = 500;
    private static readonly int ConnectionTimeoutSeconds = 20;
    private static readonly object SyncRoot = new();
    private static LibVLC? sharedLibVlc;
    private static bool initializationAttempted;
    private static bool initializationFailed;

    private readonly DispatcherTimer positionTimer;
    private MediaPlayer? mediaPlayer;
    private Media? currentMedia;
    private DateTime playbackStartedUtc = DateTime.MinValue;
    private bool isScrubbing;
    private bool isLoading = true;
    private bool hasError;
    private bool isUnavailable;
    private bool isPlaying;
    private bool isMuted;
    private string positionText = FormatTime(0) + " / " + FormatTime(0);

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoPlayerView"/> class.
    /// </summary>
    public VideoPlayerView()
    {
        InitializeComponent();
        positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PositionPollIntervalMs),
        };
        positionTimer.Tick += OnPositionTimerTick;
        PlayPauseButton.Click += OnPlayPauseClicked;
        MuteButton.Click += OnMuteClicked;
        RetryButton.Click += OnRetryClicked;
        PositionSlider.AddHandler(InputElement.PointerPressedEvent, OnScrubStarted, RoutingStrategies.Tunnel);
        PositionSlider.AddHandler(InputElement.PointerReleasedEvent, OnScrubFinished, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Gets or sets the remote video URL to stream.
    /// </summary>
    public string? SourceUrl
    {
        get => GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    /// <summary>
    /// Gets or sets the fallback command opened in the system browser when playback fails.
    /// </summary>
    public ICommand? FallbackCommand
    {
        get => GetValue(FallbackCommandProperty);
        set => SetValue(FallbackCommandProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the player is connecting or buffering.
    /// </summary>
    public bool IsLoading
    {
        get => isLoading;
        private set => SetAndRaise(IsLoadingProperty, ref isLoading, value);
    }

    /// <summary>
    /// Gets a value indicating whether playback failed.
    /// </summary>
    public bool HasError
    {
        get => hasError;
        private set => SetAndRaise(HasErrorProperty, ref hasError, value);
    }

    /// <summary>
    /// Gets a value indicating whether playback is unavailable on this device.
    /// </summary>
    public bool IsUnavailable
    {
        get => isUnavailable;
        private set => SetAndRaise(IsUnavailableProperty, ref isUnavailable, value);
    }

    /// <summary>
    /// Gets a value indicating whether video is currently playing.
    /// </summary>
    public bool IsPlaying
    {
        get => isPlaying;
        private set => SetAndRaise(IsPlayingProperty, ref isPlaying, value);
    }

    /// <summary>
    /// Gets a value indicating whether audio is muted.
    /// </summary>
    public bool IsMuted
    {
        get => isMuted;
        private set => SetAndRaise(IsMutedProperty, ref isMuted, value);
    }

    /// <summary>
    /// Gets the current position and duration text.
    /// </summary>
    public string PositionText
    {
        get => positionText;
        private set => SetAndRaise(PositionTextProperty, ref positionText, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceUrlProperty)
        {
            var url = change.GetNewValue<string?>();
            if (string.IsNullOrWhiteSpace(url))
            {
                StopPlayback();
            }
            else if (VisualRoot != null)
            {
                StartPlayback(url);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!string.IsNullOrWhiteSpace(SourceUrl) && !Design.IsDesignMode)
        {
            StartPlayback(SourceUrl);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopPlayback();
        base.OnDetachedFromVisualTree(e);
    }

    private static LibVLC? EnsureLibVlc()
    {
        lock (SyncRoot)
        {
            if (sharedLibVlc != null)
            {
                return sharedLibVlc;
            }

            if (initializationAttempted && initializationFailed)
            {
                return null;
            }

            initializationAttempted = true;
            try
            {
                LibVLCSharp.Shared.Core.Initialize();
                sharedLibVlc = new LibVLC("--no-video-title-show");
                return sharedLibVlc;
            }
            catch (Exception ex) when (ex is DllNotFoundException
                or BadImageFormatException
                or TypeInitializationException
                or InvalidOperationException
                or VLCException)
            {
                initializationFailed = true;
                System.Diagnostics.Debug.WriteLine($"Video playback unavailable: {ex.Message}");
                return null;
            }
        }
    }

    private static string FormatTime(long milliseconds)
    {
        if (milliseconds < 0)
        {
            milliseconds = 0;
        }

        var totalSeconds = milliseconds / 1000;
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}"
            : $"{minutes}:{seconds:D2}";
    }

    private void StartPlayback(string url)
    {
        ResetStage();
        var libVlc = EnsureLibVlc();
        if (libVlc == null)
        {
            IsLoading = false;
            IsUnavailable = true;
            HasError = true;
            return;
        }

        try
        {
            var player = new MediaPlayer(libVlc);
            AttachPlayerEvents(player);
            VideoHost.MediaPlayer = player;
            mediaPlayer = player;
            currentMedia = new Media(libVlc, url, FromType.FromLocation);
            playbackStartedUtc = DateTime.UtcNow;
            player.Play(currentMedia);
            positionTimer.Start();
        }
        catch (Exception ex) when (ex is VLCException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start video playback: {ex.Message}");
            IsLoading = false;
            HasError = true;
        }
    }

    private void AttachPlayerEvents(MediaPlayer player)
    {
        player.EncounteredError += OnPlaybackError;
        player.Playing += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayingState(isPlaying: true));
        player.Paused += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayingState(isPlaying: false));
        player.Stopped += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayingState(isPlaying: false));
        player.EndReached += (_, _) => Dispatcher.UIThread.Post(OnPlaybackEnded);
    }

    private void StopPlayback()
    {
        positionTimer.Stop();
        VideoHost.MediaPlayer = null;

        var player = mediaPlayer;
        mediaPlayer = null;
        if (player != null)
        {
            player.EncounteredError -= OnPlaybackError;
            player.Stop();
            player.Dispose();
        }

        currentMedia?.Dispose();
        currentMedia = null;
        playbackStartedUtc = DateTime.MinValue;
        isScrubbing = false;
    }

    private void ResetStage()
    {
        StopPlayback();
        HasError = false;
        IsUnavailable = false;
        IsPlaying = false;
        IsLoading = true;
        PositionSlider.Value = 0;
        PositionText = FormatTime(0) + " / " + FormatTime(0);
    }

    private void UpdatePlayingState(bool isPlaying)
    {
        if (HasError)
        {
            return;
        }

        IsLoading = false;
        IsPlaying = isPlaying;
    }

    private void OnPlaybackError(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!HasError)
            {
                IsLoading = false;
                HasError = true;
                IsPlaying = false;
            }
        });
    }

    private void OnPlaybackEnded()
    {
        var player = mediaPlayer;
        if (player == null || HasError)
        {
            return;
        }

        if (player.Time <= 0)
        {
            IsLoading = false;
            IsPlaying = false;
            HasError = true;
            return;
        }

        player.Stop();
        IsPlaying = false;
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        var player = mediaPlayer;
        if (player == null || HasError)
        {
            return;
        }

        if (HasConnectionTimedOut(player))
        {
            IsLoading = false;
            IsPlaying = false;
            HasError = true;
            return;
        }

        if (!isScrubbing && player.Length > 0)
        {
            PositionSlider.Value = player.Position * PositionScale;
        }

        PositionText = FormatTime(player.Time) + " / " + FormatTime(player.Length);
    }

    private bool HasConnectionTimedOut(MediaPlayer player)
    {
        return playbackStartedUtc != DateTime.MinValue
            && player.Time <= 0
            && (DateTime.UtcNow - playbackStartedUtc).TotalSeconds > ConnectionTimeoutSeconds;
    }

    private void OnPlayPauseClicked(object? sender, RoutedEventArgs e)
    {
        var player = mediaPlayer;
        if (player == null || HasError)
        {
            return;
        }

        if (player.IsPlaying)
        {
            player.Pause();
        }
        else
        {
            player.Play();
        }
    }

    private void OnMuteClicked(object? sender, RoutedEventArgs e)
    {
        var player = mediaPlayer;
        if (player == null)
        {
            return;
        }

        player.Mute = !player.Mute;
        IsMuted = player.Mute;
    }

    private void OnRetryClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SourceUrl))
        {
            StartPlayback(SourceUrl);
        }
    }

    private void OnScrubStarted(object? sender, PointerPressedEventArgs e)
    {
        isScrubbing = true;
    }

    private void OnScrubFinished(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (mediaPlayer != null && !HasError)
            {
                mediaPlayer.Position = (float)(PositionSlider.Value / PositionScale);
            }
        }
        finally
        {
            isScrubbing = false;
        }
    }
}
