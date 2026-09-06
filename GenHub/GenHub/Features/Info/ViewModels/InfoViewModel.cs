using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Messages;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the Info tab, managing multiple info sections.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel instance methods and properties bound to Avalonia XAML bindings.")]
public partial class InfoViewModel : ViewModelBase, IDisposable, IRecipient<OpenInfoSectionMessage>
{
    private bool _disposed;

    [ObservableProperty]
    private IInfoSectionViewModel? _selectedSection;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private double _openPaneLength = SidebarConstants.DefaultOpenPaneLength;

    [ObservableProperty]
    private string _selectedModule = InfoConstants.ModuleGuide;

    [ObservableProperty]
    private System.Collections.IEnumerable? _sidebarItems;

    [ObservableProperty]
    private object? _selectedSidebarItem;

    /// <summary>
    /// Initializes a new instance of the <see cref="InfoViewModel"/> class.
    /// </summary>
    /// <param name="sectionViewModels">The available info section view models.</param>
    public InfoViewModel(IEnumerable<IInfoSectionViewModel> sectionViewModels)
    {
        Sections = new ObservableCollection<IInfoSectionViewModel>(sectionViewModels.OrderBy(s => s.Order));

        // Default to GenHub Guide
        SelectedSection = Sections.OfType<GenHubInfoSectionViewModel>().FirstOrDefault()
            ?? Sections.FirstOrDefault();

        // Initialize sidebar items
        UpdateSidebarItems();

        // Register for navigation messages
        WeakReferenceMessenger.Default.Register<OpenInfoSectionMessage>(this);
    }

    /// <summary>
    /// Gets the list of available modules.
    /// </summary>
    public ObservableCollection<string> Modules { get; } =
    [
        InfoConstants.ModuleGuide,
        InfoConstants.ModuleZeroHour,
        InfoConstants.ModuleGeneralsOnline,
    ];

    /// <summary>
    /// Gets the available info sections.
    /// </summary>
    public ObservableCollection<IInfoSectionViewModel> Sections { get; }

    /// <summary>
    /// Resolves the module name corresponding to the specified section ID.
    /// </summary>
    /// <param name="sectionId">The section ID.</param>
    /// <returns>The resolved module name.</returns>
    public static string ResolveModuleForSection(string sectionId)
    {
        if (string.Equals(sectionId, InfoConstants.SectionFaq, StringComparison.OrdinalIgnoreCase))
        {
            return InfoConstants.ModuleZeroHour;
        }

        if (string.Equals(sectionId, InfoConstants.SectionGoChangelog, StringComparison.OrdinalIgnoreCase))
        {
            return InfoConstants.ModuleGeneralsOnline;
        }

        return InfoConstants.ModuleGuide;
    }

    /// <summary>
    /// Opens a specific section by ID, switching modules if necessary.
    /// </summary>
    /// <param name="sectionId">The ID of the section to open.</param>
    public void OpenSection(string sectionId)
    {
        SelectedModule = ResolveModuleForSection(sectionId);

        var targetSection = Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        if (targetSection != null)
        {
            SelectedSection = targetSection;
            return;
        }

        TryOpenSubSection(sectionId);
    }

    /// <summary>
    /// Initializes the view model and the selected section.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        if (SelectedSection != null)
        {
            await SelectedSection.InitializeAsync();
        }
    }

    /// <inheritdoc/>
    public void Receive(OpenInfoSectionMessage message)
    {
        OpenSection(message.Value);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and optionally managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            var faqSection = Sections.OfType<FaqSectionViewModel>().FirstOrDefault();
            if (faqSection != null)
            {
                faqSection.PropertyChanged -= OnFaqSectionPropertyChanged;
            }
        }

        _disposed = true;
    }

    partial void OnSelectedModuleChanged(string value)
    {
        UpdateSidebarItems();
    }

    partial void OnSelectedSectionChanged(IInfoSectionViewModel? value)
    {
        if (value != null)
        {
            _ = value.InitializeAsync();
        }
    }

    partial void OnSelectedSidebarItemChanged(object? value)
    {
        if (string.Equals(SelectedModule, InfoConstants.ModuleGuide, StringComparison.Ordinal) ||
            string.Equals(SelectedModule, InfoConstants.ModuleGeneralsOnline, StringComparison.Ordinal))
        {
            var genHubSection = Sections.OfType<GenHubInfoSectionViewModel>().FirstOrDefault();
            if (genHubSection != null && value is InfoSectionViewModel infoSection)
            {
                genHubSection.SelectedSection = infoSection;
            }
        }
        else
        {
            var faqSection = Sections.OfType<FaqSectionViewModel>().FirstOrDefault();
            if (faqSection != null && value is FaqCategoryViewModel faqCategory)
            {
                faqSection.SelectedCategory = faqCategory;
            }
        }
    }

    private void TryOpenSubSection(string sectionId)
    {
        var genHubSection = Sections.OfType<GenHubInfoSectionViewModel>().FirstOrDefault();
        if (genHubSection == null)
        {
            return;
        }

        // 1. Try Guide Context
        genHubSection.SetModuleContext(GeneralsHubModule.Guide);
        var guideSubSection = genHubSection.Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        if (guideSubSection != null)
        {
            SelectedModule = InfoConstants.ModuleGuide;
            SelectedSection = genHubSection;
            genHubSection.SelectedSection = guideSubSection;
            SelectedSidebarItem = guideSubSection;
            return;
        }

        // 2. Try GeneralsOnline Context
        genHubSection.SetModuleContext(GeneralsHubModule.GeneralsOnline);
        var goSubSection = genHubSection.Sections.FirstOrDefault(s => string.Equals(s.Id, sectionId, StringComparison.OrdinalIgnoreCase));
        if (goSubSection != null)
        {
            SelectedModule = InfoConstants.ModuleGeneralsOnline;
            SelectedSection = genHubSection;
            genHubSection.SelectedSection = goSubSection;
            SelectedSidebarItem = goSubSection;
            return;
        }

        var previousModule = string.Equals(SelectedModule, InfoConstants.ModuleGeneralsOnline, StringComparison.Ordinal)
            ? GeneralsHubModule.GeneralsOnline
            : GeneralsHubModule.Guide;
        genHubSection.SetModuleContext(previousModule);
        UpdateSidebarItems();
    }

    private void UpdateSidebarItems()
    {
        // Unsubscribe from FAQ events to prevent leaks/double firing
        var faqSection = Sections.OfType<FaqSectionViewModel>().FirstOrDefault();
        if (faqSection != null)
        {
            faqSection.PropertyChanged -= OnFaqSectionPropertyChanged;
        }

        if (string.Equals(SelectedModule, InfoConstants.ModuleGuide, StringComparison.Ordinal))
        {
            var genHubSection = Sections.OfType<GenHubInfoSectionViewModel>().FirstOrDefault();
            if (genHubSection != null)
            {
                genHubSection.SetModuleContext(GeneralsHubModule.Guide);

                SelectedSection = genHubSection;
                SidebarItems = genHubSection.Sections;
                SelectedSidebarItem = genHubSection.SelectedSection;
            }
        }
        else if (string.Equals(SelectedModule, InfoConstants.ModuleGeneralsOnline, StringComparison.Ordinal))
        {
            var genHubSection = Sections.OfType<GenHubInfoSectionViewModel>().FirstOrDefault();
            if (genHubSection != null)
            {
                genHubSection.SetModuleContext(GeneralsHubModule.GeneralsOnline);

                SelectedSection = genHubSection;
                SidebarItems = genHubSection.Sections;
                SelectedSidebarItem = genHubSection.SelectedSection;
            }
        }
        else
        {
            if (faqSection != null)
            {
                // Subscribe to sync async selection changes (e.g. after load)
                faqSection.PropertyChanged += OnFaqSectionPropertyChanged;

                SelectedSection = faqSection;
                SidebarItems = faqSection.Categories;
                SelectedSidebarItem = faqSection.SelectedCategory;

                // Ensure initial load if empty
                if (!faqSection.Categories.Any() && !faqSection.IsLoading)
                {
                    _ = faqSection.InitializeAsync();
                }
            }
        }
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Observable property access on view model")]
    private void OnFaqSectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FaqSectionViewModel.SelectedCategory) && sender is FaqSectionViewModel faqSection)
        {
            SelectedSidebarItem = faqSection.SelectedCategory;
        }
    }
}
