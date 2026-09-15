using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Info;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for an info section.
/// </summary>
public partial class InfoSectionViewModel(InfoSection model) : ObservableObject
{
    [ObservableProperty]
    private string _id = model.Id;

    [ObservableProperty]
    private string _title = model.Title;

    [ObservableProperty]
    private string _description = model.Description;

    [ObservableProperty]
    private int _order = model.Order;

    /// <summary>
    /// Gets the collection of cards in this section.
    /// </summary>
    public ObservableCollection<InfoCardViewModel> Cards { get; } = new(model.Cards.Select(c => new InfoCardViewModel
    {
        Title = c.Title,
        Content = c.Content,
        Type = c.Type,
        IsExpandable = c.IsExpandable,
        DetailedContent = c.DetailedContent,
        Actions = c.Actions,
    }));
}
