using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Info;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the Info tab, managing multiple info sections.
/// </summary>
public partial class InfoViewModel : ViewModelBase, IDisposable
{
    private readonly IEnumerable<IInfoSectionViewModel> _sectionViewModels;

    [ObservableProperty]
    private IInfoSectionViewModel? _selectedSection;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    /// <summary>
    /// Gets the list of available modules.
    /// </summary>
    public ObservableCollection<string> Modules { get; } = ["GenHub Guide", "Zero Hour"];

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
        if (IsGuideSelected)
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
        SelectedSection = Sections.OfType<GenHubInfoSectionViewModel>().FirstOrDefault();

        // Initialize sidebar items
        UpdateSidebarItems();
    }

    /// <summary>
    /// Gets the available info sections.
    /// </summary>
    public ObservableCollection<IInfoSectionViewModel> Sections { get; }

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
}
