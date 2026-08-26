using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GenHub.Core.Constants;
using GenHub.Features.Settings.Models;
using GenHub.Features.Settings.ViewModels;

namespace GenHub.Features.Settings.Views;

/// <summary>
/// Represents the view for application settings in the GenHub application.
/// </summary>
public partial class SettingsView : UserControl
{
    private SettingsViewModel? _boundViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsView"/> class.
    /// </summary>
    public SettingsView()
    {
        InitializeComponent();

        // Handle pointer press to unfocus text boxes when clicking elsewhere
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Called when the control is attached to the visual tree.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is SettingsViewModel vm)
        {
            vm.IsViewVisible = true;
            HookViewModel(vm);
            if (vm.SelectedSection != null)
            {
                ScrollToSection(vm.SelectedSection);
            }
        }
    }

    /// <summary>
    /// Called when the control is detached from the visual tree.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnhookViewModel();
        if (DataContext is SettingsViewModel vm)
        {
            vm.IsViewVisible = false;
            _ = vm.SaveSettingsCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Called when the DataContext changes.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UnhookViewModel();
        if (DataContext is SettingsViewModel vm)
        {
            // Sync visibility state with current visual tree state
            vm.IsViewVisible = VisualRoot != null;
            HookViewModel(vm);
        }
    }

    private void HookViewModel(SettingsViewModel vm)
    {
        if (ReferenceEquals(_boundViewModel, vm))
        {
            return;
        }

        UnhookViewModel();
        _boundViewModel = vm;
        _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void UnhookViewModel()
    {
        if (_boundViewModel != null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedSection) && _boundViewModel != null)
        {
            ScrollToSection(_boundViewModel.SelectedSection);
        }
    }

    private void ScrollToSection(SettingsSectionItem? section)
    {
        if (section is null)
        {
            return;
        }

        var expanderName = section.Id switch
        {
            SettingsConstants.SectionGameConfig => "Expander_GameConfig",
            SettingsConstants.SectionDownloads => "Expander_Downloads",
            SettingsConstants.SectionAppearance => "Expander_Appearance",
            SettingsConstants.SectionDataDirectories => "Expander_DataDirectories",
            SettingsConstants.SectionLogs => "Expander_Logs",
            SettingsConstants.SectionPerformance => "Expander_Performance",
            SettingsConstants.SectionCas => "Expander_Cas",
            SettingsConstants.SectionLocalContent => "Expander_LocalContent",
            SettingsConstants.SectionGitHubDiscovery => "Expander_GitHubDiscovery",
            SettingsConstants.SectionUpdates => "Expander_Updates",
            SettingsConstants.SectionCloudUploads => "Expander_CloudUploads",
            SettingsConstants.SectionDangerZone => "Expander_DangerZone",
            _ => null,
        };

        if (expanderName is null)
        {
            return;
        }

        var expander = this.FindControl<Expander>(expanderName);
        if (expander != null)
        {
            expander.IsExpanded = true;
            Dispatcher.UIThread.Post(() => expander.BringIntoView(), DispatcherPriority.Render);
        }
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // If clicking outside of a TextBox, clear focus from any focused TextBox
        if (e.Source is not TextBox)
        {
            Focus();
        }
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        // In Avalonia, we can't use GetBindingExpression like in WPF
        // The binding will automatically update when focus is lost if properly configured
        // This method exists for potential future enhancements
    }

    private void OnOpenPatCreationUrl(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Core.Constants.GitHubConstants.PatCreationUrl)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Silently fail if browser cannot be opened
        }
    }

    /// <summary>
    /// Loads and initializes the XAML components for this view.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
