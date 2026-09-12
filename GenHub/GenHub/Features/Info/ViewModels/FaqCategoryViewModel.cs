using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Info;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for a FAQ category.
/// </summary>
public partial class FaqCategoryViewModel : ObservableObject
{
    private readonly FaqCategory _category;

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="FaqCategoryViewModel"/> class.
    /// </summary>
    /// <param name="category">The FAQ category model.</param>
    public FaqCategoryViewModel(FaqCategory category)
    {
        _category = category;
        Items = new ObservableCollection<FaqItem>(category.Items);
    }

    /// <summary>
    /// Gets the category title.
    /// </summary>
    public string Title => _category.Title;

    /// <summary>
    /// Gets the list of FAQ items.
    /// </summary>
    public ObservableCollection<FaqItem> Items { get; }
}
