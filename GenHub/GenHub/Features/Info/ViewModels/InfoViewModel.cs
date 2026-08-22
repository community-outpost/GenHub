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
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Messages;
using GenHub.Features.Info.ViewModels;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the Info tab, managing multiple info sections.
/// </summary>
[SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Observable property access on view model")]
public sealed partial class InfoViewModel : ViewModelBase, IDisposable, IRecipient<OpenInfoSectionMessage>
{
    /// <summary>Gets the GenHub Guide module name.</summary>
    public const string ModuleGuide = "GenHub Guide";

    /// <summary>Gets the Zero Hour module name.</summary>
    public const string ModuleZeroHour = "Zero Hour";

    /// <summary>Gets the GeneralsOnline module name.</summary>
    public const string ModuleGeneralsOnline = "GeneralsOnline";

    private readonly IEnumerable<IInfoSectionViewModel> _sectionViewModels;

    [ObservableProperty]
    private IInfoSectionViewModel? _selectedSection;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    /// <summary>
    /// Gets the list of available modules.
    /// </summary>
    public ObservableCollection<string> Modules { get; } = [ModuleGuide, ModuleZeroHour, ModuleGeneralsOnline];

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
            ? ModuleGeneralsOnline
            : ModuleGuide;

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
                    SelectedModule = ModuleGuide;
                    OpenSubSection(genHubSection, sectionId);
                }
                else
                {
                    // 2. Try GeneralsOnline Context
                    genHubSection.SetModuleContext(GeneralsHubModule.GeneralsOnline);
                    if (genHubSection.Sections.Any(s => s.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase)))
                    {
                        SelectedModule = ModuleGeneralsOnline;
                        OpenSubSection(genHubSection, sectionId);
                    }
                }
            }
        }
    }

    [ObservableProperty]
    private string _selectedModule = ModuleGuide;

    /// <summary>
    /// Gets a value indicating whether the "GenHub Guide" module is selected.
    /// </summary>
    public bool IsGuideSelected => SelectedModule == ModuleGuide;

    /// <summary>
    /// Gets a value indicating whether the "Zero Hour" module is selected.
    /// </summary>
    public bool IsZeroHourSelected => SelectedModule == ModuleZeroHour;

    /// <summary>
    /// Gets a value indicating whether the "GeneralsOnline" module is selected.
    /// </summary>
    public bool IsGeneralsOnlineSelected => SelectedModule == ModuleGeneralsOnline;

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
