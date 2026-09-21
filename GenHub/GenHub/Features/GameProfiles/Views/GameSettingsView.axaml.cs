using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GenHub.Common.Controls;
using GenHub.Features.GameProfiles.ViewModels;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for game configuration settings (Renderer, Audio, etc.).
/// </summary>
public partial class GameSettingsView : UserControl
{
    private static readonly (string Name, SettingsCategory Category)[] SectionDefinitions =
    [
        ("VideoSection", SettingsCategory.Video),
        ("AudioSection", SettingsCategory.Audio),
        ("ControlsSection", SettingsCategory.Controls),
        ("TheSuperHackersSection", SettingsCategory.TheSuperHackers),
        ("GeneralsOnlineSection", SettingsCategory.GeneralsOnline),
    ];

    private SectionScrollSpy<SettingsCategory>? _scrollSpy;
    private SidebarWidthSynchronizer? _sidebarSynchronizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSettingsView"/> class.
    /// </summary>
    public GameSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles the loaded event to bind the ViewModel command to the View's scroll logic.
    /// </summary>
    /// <param name="e">The event args.</param>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _sidebarSynchronizer?.Dispose();
        _sidebarSynchronizer = SidebarWidthSynchronizer.Attach(this.FindControl<Grid>("RootGrid"));

        var scrollViewer = this.FindControl<ScrollViewer>("SettingsScrollViewer");
        if (scrollViewer == null)
        {
            return;
        }

        _scrollSpy?.Dispose();
        var spy = new SectionScrollSpy<SettingsCategory>(scrollViewer, OnSpySectionActivated);
        foreach (var (name, category) in SectionDefinitions)
        {
            var control = this.FindControl<Control>(name);
            if (control != null)
            {
                spy.RegisterSection(category, control);
            }
        }

        spy.Attach();
        _scrollSpy = spy;

        if (DataContext is GameSettingsViewModel vm)
        {
            vm.ScrollToSectionRequested = OnScrollToSectionRequested;
        }
    }

    /// <summary>
    /// Handles the unloaded event to clean up subscriptions.
    /// </summary>
    /// <param name="e">The event args.</param>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        _sidebarSynchronizer?.Dispose();
        _sidebarSynchronizer = null;

        _scrollSpy?.Dispose();
        _scrollSpy = null;

        if (DataContext is GameSettingsViewModel vm)
        {
            vm.ScrollToSectionRequested = null;
        }
    }

    private static SettingsCategory? GetCategory(string sectionName)
    {
        foreach (var (name, category) in SectionDefinitions)
        {
            if (string.Equals(name, sectionName, StringComparison.Ordinal))
            {
                return category;
            }
        }

        return null;
    }

    private void OnScrollToSectionRequested(string sectionName)
    {
        var category = GetCategory(sectionName);
        if (category.HasValue)
        {
            _scrollSpy?.ScrollToSection(category.Value);
        }
    }

    private void OnSpySectionActivated(SettingsCategory category)
    {
        if (DataContext is GameSettingsViewModel vm && vm.SelectedCategory != category)
        {
            vm.UpdateCategoryFromScroll(category);
        }
    }
}
