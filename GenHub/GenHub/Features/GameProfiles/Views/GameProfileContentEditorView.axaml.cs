using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.GameProfiles.ViewModels;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for editing game profile content.
/// </summary>
public partial class GameProfileContentEditorView : UserControl
{
    private static readonly (string Name, ContentEditorCategory Category)[] SectionDefinitions =
    [
        ("EnabledContentSection", ContentEditorCategory.EnabledContent),
        ("AvailableContentSection", ContentEditorCategory.AvailableContent),
    ];

    private SectionScrollSpy<ContentEditorCategory>? _scrollSpy;
    private GameProfileSettingsViewModel? _subscribedViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileContentEditorView"/> class.
    /// </summary>
    public GameProfileContentEditorView()
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
        SubscribeViewModel();
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

        UnsubscribeViewModel();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UnsubscribeViewModel();
        if (_scrollSpy != null)
        {
            SubscribeViewModel();
        }
    }

    private static ContentEditorCategory? GetCategory(string sectionName)
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void EnsureScrollSpy()
    {
        if (_scrollSpy != null)
        {
            return;
        }

        var scrollViewer = this.FindControl<ScrollViewer>("ContentEditorScrollViewer");
        if (scrollViewer is null)
        {
            return;
        }

        var spy = new SectionScrollSpy<ContentEditorCategory>(scrollViewer, OnSpySectionActivated);
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

    private void SubscribeViewModel()
    {
        if (DataContext is not GameProfileSettingsViewModel vm)
        {
            return;
        }

        vm.ScrollToSectionRequested -= OnScrollToSectionRequested;
        vm.ScrollToSectionRequested += OnScrollToSectionRequested;
        _subscribedViewModel = vm;
    }

    private void UnsubscribeViewModel()
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ScrollToSectionRequested -= OnScrollToSectionRequested;
            _subscribedViewModel = null;
        }
    }

    private void OnScrollToSectionRequested(string sectionName)
    {
        var category = GetCategory(sectionName);
        if (category.HasValue)
        {
            _scrollSpy?.ScrollToSection(category.Value);
        }
    }

    private void OnSpySectionActivated(ContentEditorCategory category)
    {
        if (DataContext is GameProfileSettingsViewModel vm && vm.SelectedContentEditorCategory != category)
        {
            vm.UpdateContentEditorCategoryFromScroll(category);
        }
    }
}
