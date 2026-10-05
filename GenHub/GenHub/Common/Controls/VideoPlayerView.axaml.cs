using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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
    /// Defines the <see cref="ToggleFullscreenCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ToggleFullscreenCommandProperty =
        AvaloniaProperty.Register<VideoPlayerView, ICommand?>(nameof(ToggleFullscreenCommand));

    /// <summary>
    /// Defines the <see cref="IsFullscreen"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsFullscreenProperty =
        AvaloniaProperty.Register<VideoPlayerView, bool>(nameof(IsFullscreen));

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
    /// Defines the <see cref="IsDownloadPromptVisible"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, bool> IsDownloadPromptVisibleProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, bool>(nameof(IsDownloadPromptVisible), o => o.IsDownloadPromptVisible);

    /// <summary>
    /// Defines the <see cref="IsDownloadingComponent"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, bool> IsDownloadingComponentProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, bool>(nameof(IsDownloadingComponent), o => o.IsDownloadingComponent);

    /// <summary>
    /// Defines the <see cref="DownloadProgress"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, double> DownloadProgressProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, double>(nameof(DownloadProgress), o => o.DownloadProgress);

    /// <summary>
    /// Defines the <see cref="DownloadStatusText"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, string> DownloadStatusTextProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, string>(nameof(DownloadStatusText), o => o.DownloadStatusText);

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

    /// <summary>
    /// Defines the <see cref="IsVideoSurfaceVisible"/> property.
    /// Hiding the native VideoView while loading/error prevents native airspace from obscuring UI overlays.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, bool> IsVideoSurfaceVisibleProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, bool>(nameof(IsVideoSurfaceVisible), o => o.IsVideoSurfaceVisible);

    /// <summary>
    /// Defines the <see cref="LoadingText"/> property.
    /// </summary>
    public static readonly DirectProperty<VideoPlayerView, string> LoadingTextProperty =
        AvaloniaProperty.RegisterDirect<VideoPlayerView, string>(nameof(LoadingText), o => o.LoadingText);

    private const int PositionScale = 1000;
    private const int PositionPollIntervalMs = 500;
    private const int ConnectionTimeoutSeconds = 20;
    private const long SkipStepMilliseconds = 10000;
    private const string DefaultLoadingVideoText = "Loading video...";
    private const string DefaultBufferingVideoText = "Buffering... {0}%";
    private const string LoadingVideoResourceKey = "Downloads.ContentDetail.VideoPlayer.LoadingVideo";
    private const string BufferingVideoResourceKey = "Downloads.ContentDetail.VideoPlayer.BufferingVideo";

    private static readonly object SyncRoot = new();
    private static LibVLC? sharedLibVlc;
    private static bool initializationAttempted;
    private static bool initializationFailed;

    private readonly DispatcherTimer positionTimer;
    private MediaPlayer? mediaPlayer;
    private Media? currentMedia;
    private DateTime playbackStartedUtc = DateTime.MinValue;
    private bool isScrubbing;
    private bool isUpdatingSliderFromTimer;
    private bool isLoading = true;
    private bool hasError;
    private bool isUnavailable;
    private bool isDownloadPromptVisible;
    private bool isDownloadingComponent;
    private CancellationTokenSource? downloadCts;
    private double downloadProgress;
    private string downloadStatusText = string.Empty;
    private bool isPlaying;
    private bool isMuted;
    private bool isVideoSurfaceVisible;
    private bool videoHostAttached;
    private string positionText = FormatTime(0) + " / " + FormatTime(0);
    private string loadingText = DefaultLoadingVideoText;

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
        RewindButton.Click += OnRewindClicked;
        FastForwardButton.Click += OnFastForwardClicked;
        MuteButton.Click += OnMuteClicked;
        FullscreenButton.Click += OnFullscreenClicked;
        RetryButton.Click += OnRetryClicked;
        DownloadCodecsButton.Click += OnDownloadCodecsClicked;
        StageBorder.DoubleTapped += OnStageDoubleTapped;

        PositionSlider.AddHandler(InputElement.PointerPressedEvent, OnScrubStarted, RoutingStrategies.Tunnel);
        PositionSlider.AddHandler(InputElement.PointerReleasedEvent, OnScrubFinished, RoutingStrategies.Bubble, handledEventsToo: true);
        PositionSlider.AddHandler(Thumb.DragCompletedEvent, (_, _) => FinishScrubbing(), RoutingStrategies.Bubble);
        PositionSlider.ValueChanged += OnPositionSliderValueChanged;

        PointerPressed += OnViewPointerPressed;
        UpdateSurfaceVisibility();
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
    /// Gets or sets the toggle full-screen command.
    /// </summary>
    public ICommand? ToggleFullscreenCommand
    {
        get => GetValue(ToggleFullscreenCommandProperty);
        set => SetValue(ToggleFullscreenCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the player is presented full screen.
    /// </summary>
    public bool IsFullscreen
    {
        get => GetValue(IsFullscreenProperty);
        set => SetValue(IsFullscreenProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the player is connecting or buffering.
    /// </summary>
    public bool IsLoading
    {
        get => isLoading;
        private set
        {
            SetAndRaise(IsLoadingProperty, ref isLoading, value);
            UpdateSurfaceVisibility();
        }
    }

    /// <summary>
    /// Gets a value indicating whether playback failed.
    /// </summary>
    public bool HasError
    {
        get => hasError;
        private set
        {
            SetAndRaise(HasErrorProperty, ref hasError, value);
            UpdateSurfaceVisibility();
        }
    }

    /// <summary>
    /// Gets a value indicating whether playback is unavailable on this device.
    /// </summary>
    public bool IsUnavailable
    {
        get => isUnavailable;
        private set
        {
            SetAndRaise(IsUnavailableProperty, ref isUnavailable, value);
            UpdateSurfaceVisibility();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the download codecs prompt is shown.
    /// </summary>
    public bool IsDownloadPromptVisible
    {
        get => isDownloadPromptVisible;
        private set
        {
            SetAndRaise(IsDownloadPromptVisibleProperty, ref isDownloadPromptVisible, value);
            UpdateSurfaceVisibility();
        }
    }

    /// <summary>
    /// Gets a value indicating whether codecs are actively being downloaded.
    /// </summary>
    public bool IsDownloadingComponent
    {
        get => isDownloadingComponent;
        private set
        {
            SetAndRaise(IsDownloadingComponentProperty, ref isDownloadingComponent, value);
            UpdateSurfaceVisibility();
        }
    }

    /// <summary>
    /// Gets the current download progress percentage (0 - 100).
    /// </summary>
    public double DownloadProgress
    {
        get => downloadProgress;
        private set => SetAndRaise(DownloadProgressProperty, ref downloadProgress, value);
    }

    /// <summary>
    /// Gets the status text during component download.
    /// </summary>
    public string DownloadStatusText
    {
        get => downloadStatusText;
        private set => SetAndRaise(DownloadStatusTextProperty, ref downloadStatusText, value);
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

    /// <summary>
    /// Gets a value indicating whether the native video surface is visible.
    /// </summary>
    public bool IsVideoSurfaceVisible
    {
        get => isVideoSurfaceVisible;
        private set => SetAndRaise(IsVideoSurfaceVisibleProperty, ref isVideoSurfaceVisible, value);
    }

    /// <summary>
    /// Gets the current loading / buffering display text.
    /// </summary>
    public string LoadingText
    {
        get => loadingText;
        private set => SetAndRaise(LoadingTextProperty, ref loadingText, value);
    }

    /// <summary>
    /// Seeks forward or backward by the specified number of milliseconds.
    /// </summary>
    /// <param name="deltaMilliseconds">Positive to skip forward, negative to go back.</param>
    public void SeekBy(long deltaMilliseconds)
    {
        var player = mediaPlayer;
        if (player == null || HasError)
        {
            return;
        }

        try
        {
            if (player.Length > 0)
            {
                var newTime = Math.Clamp(player.Time + deltaMilliseconds, 0, player.Length);
                player.Time = newTime;
                try
                {
                    isUpdatingSliderFromTimer = true;
                    PositionSlider.Value = (double)newTime / player.Length * PositionScale;
                }
                finally
                {
                    isUpdatingSliderFromTimer = false;
                }

                PositionText = FormatTime(newTime) + " / " + FormatTime(player.Length);
            }
            else if (player.Position >= 0)
            {
                var deltaPos = (float)deltaMilliseconds / 60000f;
                var newPos = Math.Clamp(player.Position + deltaPos, 0f, 1f);
                player.Position = newPos;
                try
                {
                    isUpdatingSliderFromTimer = true;
                    PositionSlider.Value = newPos * PositionScale;
                }
                finally
                {
                    isUpdatingSliderFromTimer = false;
                }
            }
        }
        catch (Exception ex) when (ex is VLCException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Video seek failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Toggles full screen presentation mode.
    /// </summary>
    public void ToggleFullscreen()
    {
        if (ToggleFullscreenCommand?.CanExecute(null) == true)
        {
            ToggleFullscreenCommand.Execute(null);
        }
        else
        {
            IsFullscreen = !IsFullscreen;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                SeekBy(-SkipStepMilliseconds);
                e.Handled = true;
                break;
            case Key.Right:
                SeekBy(SkipStepMilliseconds);
                e.Handled = true;
                break;
            case Key.Space:
                OnPlayPauseClicked(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.M:
                OnMuteClicked(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.F:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape:
                if (IsFullscreen)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                }

                break;
            default:
                // Non-transport keys are not handled by the video player.
                break;
        }
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
            else if (VisualRoot != null && videoHostAttached)
            {
                StartPlayback(url);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        VideoHost.AttachedToVisualTree += OnVideoHostAttached;
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        downloadCts?.Cancel();
        downloadCts = null;

        VideoHost.AttachedToVisualTree -= OnVideoHostAttached;
        videoHostAttached = false;
        StopPlayback();
        base.OnDetachedFromVisualTree(e);
    }

    private static LibVLC? EnsureLibVLC(string? customLibVlcPath = null)
    {
        lock (SyncRoot)
        {
            if (sharedLibVlc != null)
            {
                return sharedLibVlc;
            }

            if (initializationAttempted && initializationFailed && customLibVlcPath == null)
            {
                return null;
            }

            initializationAttempted = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(customLibVlcPath))
                {
                    LibVLCSharp.Shared.Core.Initialize(customLibVlcPath);
                }
                else
                {
                    LibVLCSharp.Shared.Core.Initialize();
                }

                sharedLibVlc = new LibVLC("--no-video-title-show");
                initializationFailed = false;
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

    private static void ResetInitializationState()
    {
        lock (SyncRoot)
        {
            initializationAttempted = false;
            initializationFailed = false;
        }
    }

    private static IVlcRuntimeService? ResolveVlcRuntimeService()
    {
        if (Application.Current?.TryGetResource("VlcRuntimeService", theme: null, out var resource) == true &&
            resource is IVlcRuntimeService service)
        {
            return service;
        }

        return App.Services?.GetService<IVlcRuntimeService>();
    }

    private static string GetLocalizedString(string key, string fallback, params object[] args)
    {
        var localizationService = Application.Current?.TryGetResource(LocalizationConstants.ResourceServiceKey, theme: null, out var resource) == true
            ? resource as ILocalizationService
            : null;

        return args.Length > 0
            ? localizationService.GetLocalizedString(key, fallback, args)
            : localizationService.GetLocalizedString(key, fallback);
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

    private void OnVideoHostAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        VideoHost.AttachedToVisualTree -= OnVideoHostAttached;
        videoHostAttached = true;
        if (!string.IsNullOrWhiteSpace(SourceUrl) && !Design.IsDesignMode)
        {
            StartPlayback(SourceUrl);
        }
    }

    private void UpdateSurfaceVisibility()
    {
        IsVideoSurfaceVisible = !isLoading && !hasError && !isUnavailable && !isDownloadPromptVisible && !isDownloadingComponent;
    }

    private void StartPlayback(string url)
    {
        ResetStage();
        var runtimeService = ResolveVlcRuntimeService();
        bool isAvailable = runtimeService?.IsAvailable() == true;
        string? runtimeDir = runtimeService?.RuntimeDirectory;

        if (runtimeService != null && !isAvailable)
        {
            IsLoading = false;
            if (runtimeService.IsInstallSupported)
            {
                IsDownloadPromptVisible = true;
                IsUnavailable = false;
                HasError = false;
            }
            else
            {
                IsDownloadPromptVisible = false;
                IsUnavailable = true;
                HasError = true;
            }

            return;
        }

        var libVlc = EnsureLibVLC(runtimeDir);
        if (libVlc == null)
        {
            IsLoading = false;
            IsDownloadPromptVisible = false;
            IsUnavailable = true;
            HasError = true;
            return;
        }

        IsDownloadPromptVisible = false;
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
        player.Opening += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(mediaPlayer, player) && !HasError)
            {
                IsLoading = true;
                LoadingText = GetLocalizedString(LoadingVideoResourceKey, DefaultLoadingVideoText);
            }
        });
        player.Buffering += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(mediaPlayer, player) || HasError)
            {
                return;
            }

            if (e.Cache < 100f)
            {
                IsLoading = true;
                LoadingText = GetLocalizedString(BufferingVideoResourceKey, DefaultBufferingVideoText, Math.Round(e.Cache));
            }
            else
            {
                IsLoading = false;
                LoadingText = GetLocalizedString(LoadingVideoResourceKey, DefaultLoadingVideoText);
            }
        });
        player.Playing += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayingState(player, isPlaying: true));
        player.Paused += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayingState(player, isPlaying: false));
        player.Stopped += (_, _) => Dispatcher.UIThread.Post(() => UpdatePlayingState(player, isPlaying: false));
        player.EndReached += (_, _) => Dispatcher.UIThread.Post(() => OnPlaybackEnded(player));
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
        IsDownloadPromptVisible = false;
        IsDownloadingComponent = false;
        IsPlaying = false;
        IsMuted = false;
        isScrubbing = false;
        IsLoading = true;
        LoadingText = GetLocalizedString(LoadingVideoResourceKey, DefaultLoadingVideoText);
        PositionSlider.Value = 0;
        PositionText = FormatTime(0) + " / " + FormatTime(0);
        UpdateSurfaceVisibility();
    }

    private void UpdatePlayingState(MediaPlayer player, bool isPlaying)
    {
        if (!ReferenceEquals(mediaPlayer, player) || HasError)
        {
            return;
        }

        IsPlaying = isPlaying;
        if (isPlaying && (player.Time > 0 || player.Length > 0))
        {
            IsLoading = false;
        }
    }

    private void OnPlaybackError(object? sender, EventArgs e)
    {
        var player = sender as MediaPlayer;
        Dispatcher.UIThread.Post(() =>
        {
            if (player is null || !ReferenceEquals(mediaPlayer, player) || HasError)
            {
                return;
            }

            IsLoading = false;
            HasError = true;
            IsPlaying = false;
        });
    }

    private void OnPlaybackEnded(MediaPlayer player)
    {
        if (!ReferenceEquals(mediaPlayer, player) || HasError)
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

        if (player.Time > 0 && IsLoading)
        {
            IsLoading = false;
        }

        if (player.Time > 0)
        {
            // Connection established; the timeout guards only the initial connect.
            playbackStartedUtc = DateTime.MinValue;
        }

        if (!isScrubbing && player.Length > 0)
        {
            try
            {
                isUpdatingSliderFromTimer = true;
                PositionSlider.Value = player.Position * PositionScale;
            }
            finally
            {
                isUpdatingSliderFromTimer = false;
            }
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

    private void OnRewindClicked(object? sender, RoutedEventArgs e)
    {
        SeekBy(-SkipStepMilliseconds);
    }

    private void OnFastForwardClicked(object? sender, RoutedEventArgs e)
    {
        SeekBy(SkipStepMilliseconds);
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

    private void OnFullscreenClicked(object? sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void OnStageDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleFullscreen();
        e.Handled = true;
    }

    private void OnViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
    }

    private void OnRetryClicked(object? sender, RoutedEventArgs e)
    {
        ResetInitializationState();
        if (!string.IsNullOrWhiteSpace(SourceUrl))
        {
            StartPlayback(SourceUrl);
        }
    }

    private async void OnDownloadCodecsClicked(object? sender, RoutedEventArgs e)
    {
        var runtimeService = ResolveVlcRuntimeService();
        if (runtimeService == null)
        {
            SetCodecDownloadFailedState();
            return;
        }

        await CancelActiveDownloadAsync().ConfigureAwait(true);

        var activeCts = new CancellationTokenSource();
        downloadCts = activeCts;

        InitializeDownloadUi();
        var progress = CreateDownloadProgress(activeCts);

        try
        {
            var result = await runtimeService.InstallRuntimeAsync(progress, activeCts.Token).ConfigureAwait(true);
            HandleInstallResult(result, activeCts);
        }
        catch (OperationCanceledException)
        {
            HandleInstallCancelled(activeCts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to install VLC runtime: {ex.Message}");
            HandleInstallError(activeCts);
        }
        finally
        {
            if (ReferenceEquals(downloadCts, activeCts))
            {
                downloadCts = null;
            }

            activeCts.Dispose();
        }
    }

    private void SetCodecDownloadFailedState()
    {
        IsDownloadPromptVisible = false;
        IsUnavailable = true;
        HasError = true;
    }

    private async Task CancelActiveDownloadAsync()
    {
        if (downloadCts != null)
        {
            var oldCts = downloadCts;
            downloadCts = null;
            try
            {
                await oldCts.CancelAsync().ConfigureAwait(true);
            }
            catch (ObjectDisposedException)
            {
                // Ignore CTS disposal race from previous install completion
            }
            finally
            {
                oldCts.Dispose();
            }
        }
    }

    private void InitializeDownloadUi()
    {
        IsDownloadPromptVisible = false;
        IsDownloadingComponent = true;
        DownloadProgress = 0;
        DownloadStatusText = GetLocalizedString(
            "Downloads.ContentDetail.VideoPlayer.DownloadingProgress",
            "Downloading media player components... {0}%",
            0);
    }

    private IProgress<double> CreateDownloadProgress(CancellationTokenSource activeCts)
    {
        var cancellationToken = activeCts.Token;
        return new Progress<double>(percent =>
        {
            if (!ReferenceEquals(downloadCts, activeCts) || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(downloadCts, activeCts) || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                DownloadProgress = percent * 100.0;
                DownloadStatusText = GetLocalizedString(
                    "Downloads.ContentDetail.VideoPlayer.DownloadingProgress",
                    "Downloading media player components... {0}%",
                    (int)Math.Round(percent * 100));
            });
        });
    }

    private void HandleInstallResult(OperationResult<bool> result, CancellationTokenSource activeCts)
    {
        if (!ReferenceEquals(downloadCts, activeCts))
        {
            return;
        }

        if (activeCts.Token.IsCancellationRequested)
        {
            HandleInstallCancelled(activeCts);
            return;
        }

        IsDownloadingComponent = false;
        if (result.Success)
        {
            ResetInitializationState();
            if (!string.IsNullOrWhiteSpace(SourceUrl))
            {
                StartPlayback(SourceUrl);
            }
        }
        else
        {
            IsUnavailable = true;
            HasError = true;
        }
    }

    private void HandleInstallCancelled(CancellationTokenSource activeCts)
    {
        if (ReferenceEquals(downloadCts, activeCts))
        {
            IsDownloadingComponent = false;
            IsDownloadPromptVisible = true;
        }
    }

    private void HandleInstallError(CancellationTokenSource activeCts)
    {
        if (ReferenceEquals(downloadCts, activeCts))
        {
            IsDownloadingComponent = false;
            IsUnavailable = true;
            HasError = true;
        }
    }

    private void OnScrubStarted(object? sender, PointerPressedEventArgs e)
    {
        isScrubbing = true;
        if (sender is Slider slider && slider.Bounds.Width > 0)
        {
            var pos = e.GetPosition(slider);
            var ratio = Math.Clamp(pos.X / slider.Bounds.Width, 0.0, 1.0);
            try
            {
                isUpdatingSliderFromTimer = true;
                slider.Value = ratio * PositionScale;
            }
            finally
            {
                isUpdatingSliderFromTimer = false;
            }

            SeekToSliderPosition(slider.Value);
        }
    }

    private void OnScrubFinished(object? sender, PointerReleasedEventArgs e)
    {
        FinishScrubbing();
    }

    private void FinishScrubbing()
    {
        if (!isScrubbing)
        {
            return;
        }

        isScrubbing = false;
        SeekToSliderPosition(PositionSlider.Value);
    }

    private void OnPositionSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (isUpdatingSliderFromTimer)
        {
            return;
        }

        SeekToSliderPosition(e.NewValue);
    }

    private void SeekToSliderPosition(double sliderValue)
    {
        var player = mediaPlayer;
        if (player == null || HasError)
        {
            return;
        }

        var targetRatio = (float)Math.Clamp(sliderValue / PositionScale, 0.0, 1.0);
        try
        {
            player.Position = targetRatio;
            if (player.Length > 0)
            {
                var targetTime = (long)(targetRatio * player.Length);
                PositionText = FormatTime(targetTime) + " / " + FormatTime(player.Length);
            }
        }
        catch (Exception ex) when (ex is VLCException or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine($"Slider seek failed: {ex.Message}");
        }
    }
}
