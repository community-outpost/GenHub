using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GenHub.Common.Controls;
using GenHub.Features.GameProfiles.ViewModels;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for general profile settings (Identity, Theme, etc.).
/// </summary>
public partial class GameProfileGeneralSettingsView : UserControl
{
    private static readonly (string Name, GeneralSettingsCategory Category)[] SectionDefinitions =
    [
        ("IdentitySection", GeneralSettingsCategory.Identity),
        ("AppearanceSection", GeneralSettingsCategory.Appearance),
        ("LaunchSection", GeneralSettingsCategory.Launch),
        ("ThemeSection", GeneralSettingsCategory.Theme),
    ];

    private SectionScrollSpy<GeneralSettingsCategory>? _scrollSpy;
    private GameProfileSettingsViewModel? _boundViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileGeneralSettingsView"/> class.
    /// </summary>
    public GameProfileGeneralSettingsView()
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

        EnsureScrollSpy();
        if (DataContext is GameProfileSettingsViewModel vm)
        {
            _boundViewModel = vm;
            AttachHandlers(vm);
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_boundViewModel != null)
        {
            DetachHandlers(_boundViewModel);
            _boundViewModel = null;
        }

        if (DataContext is GameProfileSettingsViewModel vm)
        {
            _boundViewModel = vm;
            AttachHandlers(vm);
        }
    }

    /// <summary>
    /// Handles the unloaded event to clean up subscriptions.
    /// </summary>
    /// <param name="e">The event args.</param>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        _scrollSpy?.Dispose();
        _scrollSpy = null;

        if (_boundViewModel != null)
        {
            DetachHandlers(_boundViewModel);
            _boundViewModel = null;
        }
    }

    private static GeneralSettingsCategory? GetCategory(string sectionName)
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

    private void EnsureScrollSpy()
    {
        if (_scrollSpy != null)
        {
            return;
        }

        var scrollViewer = this.FindControl<ScrollViewer>("GeneralSettingsScrollViewer");
        if (scrollViewer is null)
        {
            return;
        }

        var spy = new SectionScrollSpy<GeneralSettingsCategory>(scrollViewer, OnSpySectionActivated);
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
    }

    private void AttachHandlers(GameProfileSettingsViewModel vm)
    {
        vm.ScrollToSectionRequested -= OnScrollToSectionRequested;
        vm.ScrollToSectionRequested += OnScrollToSectionRequested;
    }

    private void DetachHandlers(GameProfileSettingsViewModel vm)
    {
        vm.ScrollToSectionRequested -= OnScrollToSectionRequested;
    }

    private void OnScrollToSectionRequested(string sectionName)
    {
        var category = GetCategory(sectionName);
        if (category.HasValue)
        {
            _scrollSpy?.ScrollToSection(category.Value);
        }
    }

    private void OnSpySectionActivated(GeneralSettingsCategory category)
    {
        if (DataContext is GameProfileSettingsViewModel vm && vm.SelectedGeneralCategory != category)
        {
            vm.UpdateGeneralCategoryFromScroll(category);
        }
    }
}
