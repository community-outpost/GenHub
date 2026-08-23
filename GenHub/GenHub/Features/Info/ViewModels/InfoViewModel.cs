using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    [ObservableProperty]
    private IInfoSectionViewModel? _selectedSection;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private double _openPaneLength = 220.0;

    /// <summary>
    /// Gets the list of available modules.
    /// </summary>
    public ObservableCollection<string> Modules { get; } = [InfoConstants.ModuleGuide, InfoConstants.ModuleZeroHour, InfoConstants.ModuleGeneralsOnline];

    /// <summary>
    /// Gets the available info sections.
    /// </summary>
    public ObservableCollection<IInfoSectionViewModel> Sections { get; }

    /// <summary>
    /// Opens a specific section by ID, switching modules if necessary.
    /// </summary>
    /// <param name="sectionId">The ID of the section to open.</param>
    public void OpenSection(string sectionId)
    {
        SelectedModule = (sectionId.Equals("faq", StringComparison.OrdinalIgnoreCase) ||
                          sectionId.Equals("go-changelog", StringComparison.OrdinalIgnoreCase))
            ? InfoConstants.ModuleGeneralsOnline
            : InfoConstants.ModuleGuide;

        // Find the section in the current (filtered) Sections list
        var targetSection = Sections.FirstOrDefault(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase));

        if (targetSection != null)
        {
            SelectedSection = targetSection;
        }
        else
        {
            // It might be a sub-section of the GenHubInfoSectionViewModel
            var genHubSection = Sections.OfType<GenHubInfoSectionViewModel>().FirstOrDefault();
            if (genHubSection != null)
            {
                // Heuristic search:
                // 1. Try Guide Context
                genHubSection.SetModuleContext(GeneralsHubModule.Guide);
                if (genHubSection.Sections.Any(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedModule = InfoConstants.ModuleGuide;
                    OpenSubSection(genHubSection, sectionId);
                    return;
                }

                // 2. Try GeneralsOnline Context
                genHubSection.SetModuleContext(GeneralsHubModule.GeneralsOnline);
                if (genHubSection.Sections.Any(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedModule = InfoConstants.ModuleGeneralsOnline;
                    OpenSubSection(genHubSection, sectionId);
                }
            }
        }
    }

    [ObservableProperty]
    private string _selectedModule = InfoConstants.ModuleGuide;

    /// <summary>
    /// Gets a value indicating whether the "GenHub Guide" module is selected.
    /// </summary>
    public bool IsGuideSelected => SelectedModule == InfoConstants.ModuleGuide;

    /// <summary>
    /// Gets a value indicating whether the "Zero Hour" module is selected.
    /// </summary>
    public bool IsZeroHourSelected => SelectedModule == InfoConstants.ModuleZeroHour;

    /// <summary>
    /// Gets a value indicating whether the "GeneralsOnline" module is selected.
    /// </summary>
    public bool IsGeneralsOnlineSelected => SelectedModule == InfoConstants.ModuleGeneralsOnline;

    /// <summary>
    /// Gets the items to display in the sidebar for the current module.
    /// </summary>
    [ObservableProperty]
    private System.Collections.IEnumerable? _sidebarItems;

    [ObservableProperty]
    private object? _selectedSidebarItem;

    partial void OnSelectedModuleChanged(string value)
    {
        OnPropertyChanged(nameof(IsGuideSelected));
        OnPropertyChanged(nameof(IsZeroHourSelected));
        OnPropertyChanged(nameof(IsGeneralsOnlineSelected));
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

        if (IsGuideSelected)
        {
            var genHubSection = Sections.OfType<GenHubInfoSectionViewModel>().FirstOrDefault();
            if (genHubSection != null)
            {
                 // Filter for Guide sections (exclude FAQ and Changelog identifiers if needed,
                 // but for now we'll filter them in the ViewModel or just reuse the section)
                 // Actually, we need to switch the context of the GenHubInfoSectionViewModel
                 genHubSection.SetModuleContext(GeneralsHubModule.Guide);

                 SelectedSection = genHubSection;
                 SidebarItems = genHubSection.Sections;
                 SelectedSidebarItem = genHubSection.SelectedSection;
            }
        }
        else if (IsGeneralsOnlineSelected)
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

    private void OnFaqSectionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FaqSectionViewModel.SelectedCategory) && sender is FaqSectionViewModel faqSection)
        {
            SelectedSidebarItem = faqSection.SelectedCategory;
        }
    }

    partial void OnSelectedSidebarItemChanged(object? value)
    {
        if (IsGuideSelected || IsGeneralsOnlineSelected)
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

    // Keep SelectedSection for content binding

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

    /// <inheritdoc/>
    public void Receive(OpenInfoSectionMessage message)
    {
        OpenSection(message.Value);
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
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="InfoViewModel"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            var faqSection = Sections.OfType<FaqSectionViewModel>().FirstOrDefault();
            if (faqSection != null)
            {
                faqSection.PropertyChanged -= OnFaqSectionPropertyChanged;
            }
        }
    }

    partial void OnSelectedSectionChanged(IInfoSectionViewModel? value)
    {
        if (value != null)
        {
            _ = value.InitializeAsync();
        }
    }

    private void OpenSubSection(GenHubInfoSectionViewModel parent, string sectionId)
    {
         var target = parent.Sections.FirstOrDefault(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase));
         if (target != null)
         {
             SelectedSection = parent;
             parent.SelectedSection = target;
             SelectedSidebarItem = target;
         }
    }
}
