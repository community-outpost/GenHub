using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Info;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for an info section.
/// </summary>
public partial class InfoSectionViewModel(InfoSection model, ILocalizationService? localizationService = null) : ObservableObject
{
    [ObservableProperty]
    private string _id = model.Id;

    [ObservableProperty]
    private string _title = localizationService?.GetString($"Info.Section.{model.Id}.Title") ?? model.Title;

    [ObservableProperty]
    private string _description = localizationService?.GetString($"Info.Section.{model.Id}.Description") ?? model.Description;

    [ObservableProperty]
    private int _order = model.Order;

    /// <summary>
    /// Gets the underlying model.
    /// </summary>
    public InfoSection Model => model;

    /// <summary>
    /// Notifies that localization has changed.
    /// </summary>
    public void NotifyLocalizationChanged()
    {
        Title = localizationService?.GetString($"Info.Section.{Id}.Title") ?? model.Title;
        Description = localizationService?.GetString($"Info.Section.{Id}.Description") ?? model.Description;
    }

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
