using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Messages;
using GenHub.Features.Info.ViewModels;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the Info tab, managing multiple info sections.
/// </summary>
public partial class InfoViewModel : ViewModelBase, IDisposable, IRecipient<OpenInfoSectionMessage>
{
    private readonly IEnumerable<IInfoSectionViewModel> _sectionViewModels;

    [ObservableProperty]
    private IInfoSectionViewModel? _selectedSection;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    /// <summary>
    /// Gets the list of available modules.
    /// </summary>
    public ObservableCollection<string> Modules { get; } = ["GenHub Guide", "Zero Hour", "GeneralsOnline"];

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
        // 1. Check if it's a known GeneralsOnline section
        if (sectionId.Equals("faq", StringComparison.OrdinalIgnoreCase) ||
            sectionId.Equals("go-changelog", StringComparison.OrdinalIgnoreCase))
        {
            SelectedModule = "GeneralsOnline";
        }
        else
        {
            // Default to Guide for everything else
            SelectedModule = "GenHub Guide";
        }

        // 2. Force update sections context to ensure the list is populated for the target module
        // (SelectedModule setter calls UpdateSidebarItems, but we need to be sure before searching)

        // 3. Find the section in the current (filtered) Sections list
        var targetSection = Sections.FirstOrDefault(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase));

        if (targetSection != null)
        {
            SelectedSection = targetSection;

            // Also ensure it's selected in the sidebar
            if (SelectedSection is GenHubInfoSectionViewModel genHubSection)
            {
                 // GenHubInfoSectionViewModel is a container, so we usually select a sub-section inside it?
                 // No, GenHubInfoSection IS the "Guide" container in the Main Tabs basically?
                 // Wait, InfoViewModel structure is:
                 // Sections = [GenHubInfoSectionViewModel (Guide), FaqSectionViewModel (FAQ), etc?]

                 // Let's re-read UpdateSidebarItems logic.
                 // If Guide is selected:
                 // GenHubInfoSectionViewModel is found.
                 // SelectedSection = genHubSection;
                 // SidebarItems = genHubSection.Sections;
                 // SelectedSidebarItem = genHubSection.SelectedSection;

                 // So "Quickstart" is actually a SUB-section of GenHubInfoSectionViewModel.

                 // CORRECTION: OpenSection logic needs to handle this hierarchy.
                 // Ideally, we find the GenHubInfoSectionViewModel, and tell IT to select "quickstart".

                 // Determine if the ID belongs to GenHubSection or is a top level section.
                 // The "Sections" property of InfoViewModel contains the TOP LEVEL providers (GuideContainer, FAQ, Changelogs).

                 // Users pass "quickstart". This is inside "GenHub Guide".

                 // Let's try to find it in the GenHubInfoSectionViewModel.
            }
        }
        else
        {
             // It might be a sub-section of the GenHubInfoSectionViewModel
             var genHubSection = Sections.OfType<GenHubInfoSectionViewModel>().FirstOrDefault();
             if (genHubSection != null)
             {
                 // We need to check all potential sub-sections.
                 // The GenHubInfoSectionViewModel might only show filtered sections in its public 'Sections' property based on context.
                 // However, we can try to switch context to find it.

                 // Heuristic search:
                 // 1. Try Guide Context
                 genHubSection.SetModuleContext(GeneralsHubModule.Guide);
                 if (genHubSection.Sections.Any(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase)))
                 {
                     SelectedModule = "GenHub Guide";
                     OpenSubSection(genHubSection, sectionId);
                     return;
                 }

                 // 2. Try GeneralsOnline Context
                 genHubSection.SetModuleContext(GeneralsHubModule.GeneralsOnline);
                 if (genHubSection.Sections.Any(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase)))
                 {
                     SelectedModule = "GeneralsOnline";
                     OpenSubSection(genHubSection, sectionId);
                     return;
                 }
             }
        }
    }

    [ObservableProperty]
    private string _selectedModule = "GenHub Guide";

    /// <summary>
    /// Gets a value indicating whether the "GenHub Guide" module is selected.
    /// </summary>
    public bool IsGuideSelected => SelectedModule == "GenHub Guide";

    /// <summary>
    /// Gets a value indicating whether the "Zero Hour" module is selected.
    /// </summary>
    public bool IsZeroHourSelected => SelectedModule == "Zero Hour";

    /// <summary>
    /// Gets a value indicating whether the "GeneralsOnline" module is selected.
    /// </summary>
    public bool IsGeneralsOnlineSelected => SelectedModule == "GeneralsOnline";

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
        if (e.PropertyName == nameof(FaqSectionViewModel.SelectedCategory))
        {
            if (sender is FaqSectionViewModel faqSection)
            {
                SelectedSidebarItem = faqSection.SelectedCategory;
            }
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
        _sectionViewModels = sectionViewModels;
        Sections = new ObservableCollection<IInfoSectionViewModel>(_sectionViewModels.OrderBy(s => s.Order));

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
        var faqSection = Sections.OfType<FaqSectionViewModel>().FirstOrDefault();
        if (faqSection != null)
        {
            faqSection.PropertyChanged -= OnFaqSectionPropertyChanged;
        }

        GC.SuppressFinalize(this);
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
