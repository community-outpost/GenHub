using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Core.Constants;
using GenHub.Features.Settings.Models;
using GenHub.Features.Settings.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;

namespace GenHub.Features.Settings.Views;

/// <summary>
/// Represents the view for application settings in the GenHub application.
/// </summary>
public partial class SettingsView : UserControl
{
    private static readonly string[] SectionIdsInVisualOrder =
    [
        SettingsConstants.SectionGameConfig,
        SettingsConstants.SectionDownloads,
        SettingsConstants.SectionAppearance,
        SettingsConstants.SectionDataDirectories,
        SettingsConstants.SectionMigrateInstallation,
        SettingsConstants.SectionLogs,
        SettingsConstants.SectionPerformance,
        SettingsConstants.SectionCas,
        SettingsConstants.SectionLocalContent,
        SettingsConstants.SectionGitHubDiscovery,
        SettingsConstants.SectionUpdates,
        SettingsConstants.SectionSubscriptions,
        SettingsConstants.SectionDangerZone,
    ];

    private SettingsViewModel? _boundViewModel;
    private SectionScrollSpy<string>? _scrollSpy;
    private bool _syncingSelectionFromScroll;

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
            if (!vm.IsLoadingSubscriptions)
            {
                _ = vm.LoadSubscriptionsCommand.ExecuteAsync(null).ContinueWith(
                    t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to load subscriptions: {t.Exception.GetBaseException().Message}");
                        }
                    },
                    System.Threading.Tasks.TaskScheduler.Default);
            }

            HookViewModel(vm);
            EnsureScrollSpy();
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
        _scrollSpy?.Dispose();
        _scrollSpy = null;
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

    private static string? GetExpanderName(string sectionId)
    {
        return sectionId switch
        {
            SettingsConstants.SectionGameConfig => "Expander_GameConfig",
            SettingsConstants.SectionDownloads => "Expander_Downloads",
            SettingsConstants.SectionAppearance => "Expander_Appearance",
            SettingsConstants.SectionDataDirectories => "Expander_DataDirectories",
            SettingsConstants.SectionMigrateInstallation => "Expander_MigrateInstallation",
            SettingsConstants.SectionLogs => "Expander_Logs",
            SettingsConstants.SectionPerformance => "Expander_Performance",
            SettingsConstants.SectionCas => "Expander_Cas",
            SettingsConstants.SectionLocalContent => "Expander_LocalContent",
            SettingsConstants.SectionGitHubDiscovery => "Expander_GitHubDiscovery",
            SettingsConstants.SectionUpdates => "Expander_Updates",
            SettingsConstants.SectionSubscriptions => "Expander_Subscriptions",
            SettingsConstants.SectionCloudUploads => SettingsConstants.ExpanderCloudUploads,
            SettingsConstants.SectionDangerZone => "Expander_DangerZone",
            _ => null,
        };
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
        if (e.PropertyName == nameof(SettingsViewModel.SelectedSection) && _boundViewModel?.SelectedSection != null && !_syncingSelectionFromScroll)
        {
            ScrollToSection(_boundViewModel.SelectedSection);
        }
    }

    private void EnsureScrollSpy()
    {
        if (_scrollSpy != null)
        {
            return;
        }

        var scrollViewer = this.FindControl<ScrollViewer>("SettingsScrollViewer");
        if (scrollViewer is null)
        {
            return;
        }

        var spy = new SectionScrollSpy<string>(scrollViewer, OnSpySectionActivated);
        foreach (var sectionId in SectionIdsInVisualOrder)
        {
            var expander = FindSectionExpander(sectionId);
            if (expander != null)
            {
                spy.RegisterSection(sectionId, expander);
            }
        }

        spy.Attach();
        _scrollSpy = spy;
    }

    private Expander? FindSectionExpander(string sectionId)
    {
        var expanderName = GetExpanderName(sectionId);
        return expanderName is null ? null : this.FindControl<Expander>(expanderName);
    }

    private void OnSpySectionActivated(string sectionId)
    {
        if (_boundViewModel is null)
        {
            return;
        }

        var section = _boundViewModel.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null || section.Equals(_boundViewModel.SelectedSection))
        {
            return;
        }

        _syncingSelectionFromScroll = true;
        try
        {
            _boundViewModel.UpdateSectionFromScroll(section);
        }
        finally
        {
            _syncingSelectionFromScroll = false;
        }
    }

    private void ScrollToSection(SettingsSectionItem? section)
    {
        if (section is null)
        {
            return;
        }

        var expander = FindSectionExpander(section.Id);
        if (expander is null || _scrollSpy is null)
        {
            return;
        }

        expander.IsExpanded = true;
        if (expander.IsMeasureValid)
        {
            _scrollSpy.ScrollToSection(section.Id);
            return;
        }

        ScrollAfterLayout(expander, section.Id);
    }

    private void ScrollAfterLayout(Expander expander, string sectionId)
    {
        EventHandler? onLayoutUpdated = null;
        onLayoutUpdated = (_, _) =>
        {
            expander.LayoutUpdated -= onLayoutUpdated;
            _scrollSpy?.ScrollToSection(sectionId);
        };
        expander.LayoutUpdated += onLayoutUpdated;
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

    /// <summary>
    /// Loads and initializes the XAML components for this view.
    /// </summary>
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
