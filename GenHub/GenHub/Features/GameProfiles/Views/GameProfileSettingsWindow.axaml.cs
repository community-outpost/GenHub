using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Window for managing game profile settings.
/// </summary>
public partial class GameProfileSettingsWindow : GenHubWindow
{
    private static double? _savedWidth;
    private static double? _savedHeight;
    private static WindowState? _savedWindowState;
    private bool _isClosing;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsWindow"/> class.
    /// </summary>
    public GameProfileSettingsWindow()
    {
        InitializeComponent();

        // Restore saved window size
        RestoreWindowSize();

        // Subscribe to property changes to save window size
        PropertyChanged += OnPropertyChanged;

        // Subscribe to window events
        Activated += OnWindowActivated;
    }

    /// <summary>
    /// Gets a value indicating whether the window is fitted to the working area on open.
    /// Disabled because this window owns persisted placement logic that clamping would corrupt.
    /// </summary>
    protected override bool FitToScreenOnOpen => false;

    /// <summary>
    /// Handles pointer pressed on the header to enable window dragging and maximizing.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    public void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2 && CanResize)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    /// <summary>
    /// Handles the toggle fullscreen button click.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    public void OnToggleFullscreenClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_savedWindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <inheritdoc/>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        PersistWindowSettingsToDisk();
        _isClosing = true;
        base.OnClosing(e);
    }

    /// <summary>
    /// Override to unsubscribe from events when window is closed.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnClosed(EventArgs e)
    {
        Activated -= OnWindowActivated;
        if (DataContext is GameProfileSettingsViewModel viewModel)
        {
            viewModel.CancelPendingOperations();
        }

        base.OnClosed(e);
    }

    private static void SetInitialDimensions(double? width, double? height, bool? isMaximized)
    {
        _savedWidth ??= width;
        _savedHeight ??= height;
        if (!_savedWindowState.HasValue && isMaximized.HasValue)
        {
            _savedWindowState = isMaximized.Value ? WindowState.Maximized : WindowState.Normal;
        }
    }

    private static void RecordWindowDimensions(WindowState state, double width, double height)
    {
        if (state == WindowState.Maximized)
        {
            _savedWindowState = WindowState.Maximized;
        }
        else if (state == WindowState.Normal)
        {
            _savedWindowState = WindowState.Normal;
            if (width > 0 && height > 0)
            {
                _savedWidth = width;
                _savedHeight = height;
            }
        }
    }

    private async void OnWindowActivated(object? sender, EventArgs e)
    {
        if (DataContext is GameProfileSettingsViewModel viewModel)
        {
            await viewModel.RefreshHotswapStateAsync();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Handles property changes to track window size in memory without disk churn.
    /// </summary>
    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        if (e.Property == WidthProperty || e.Property == HeightProperty || e.Property == WindowStateProperty)
        {
            UpdateInMemoryWindowSize();
        }
    }

    /// <summary>
    /// Restores the window size and state from saved static fields or user settings.
    /// </summary>
    private void RestoreWindowSize()
    {
        try
        {
            var userSettingsService = App.Services?.GetService<IUserSettingsService>();
            var settings = userSettingsService?.Get();
            if (settings != null)
            {
                SetInitialDimensions(
                    settings.ProfileSettingsWindowWidth,
                    settings.ProfileSettingsWindowHeight,
                    settings.ProfileSettingsWindowIsMaximized);
            }
        }
        catch
        {
            // Fallback to defaults if settings service is unavailable
        }

        Width = _savedWidth ?? UiConstants.DefaultProfileSettingsWidth;
        Height = _savedHeight ?? UiConstants.DefaultProfileSettingsHeight;
        if (_savedWindowState.HasValue)
        {
            WindowState = _savedWindowState.Value;
        }
    }

    /// <summary>
    /// Updates the static fields tracking the current window state and dimensions in memory.
    /// </summary>
    private void UpdateInMemoryWindowSize()
    {
        RecordWindowDimensions(WindowState, Width, Height);
    }

    /// <summary>
    /// Persists the current window size and state to user settings on disk.
    /// </summary>
    private void PersistWindowSettingsToDisk()
    {
        UpdateInMemoryWindowSize();

        try
        {
            var userSettingsService = App.Services?.GetService<IUserSettingsService>();
            userSettingsService?.Update(s =>
            {
                s.ProfileSettingsWindowIsMaximized = _savedWindowState == WindowState.Maximized;
                if (_savedWidth.HasValue)
                {
                    s.ProfileSettingsWindowWidth = _savedWidth.Value;
                }

                if (_savedHeight.HasValue)
                {
                    s.ProfileSettingsWindowHeight = _savedHeight.Value;
                }
            });
            _ = userSettingsService?.SaveAsync();
        }
        catch
        {
            // Ignore settings save errors in design-time or unit-test environments
        }
    }
}
