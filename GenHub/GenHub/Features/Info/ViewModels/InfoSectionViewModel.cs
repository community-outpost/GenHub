using System;
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
public partial class InfoSectionViewModel : ObservableObject
{
    private readonly InfoSection _model;
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty]
    private string _id;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private int _order;

    /// <summary>
    /// Initializes a new instance of the <see cref="InfoSectionViewModel"/> class.
    /// </summary>
    public InfoSectionViewModel(InfoSection model, ILocalizationService? localizationService = null)
    {
        _model = model;
        _localizationService = localizationService;
        _id = model.Id;
        _order = model.Order;

        _title = ResolveString(localizationService, $"Info.Section.{model.Id}.Title", model.Title);
        _description = ResolveString(localizationService, $"Info.Section.{model.Id}.Description", model.Description);

        Cards = new(model.Cards.Select(c => new InfoCardViewModel
        {
            Title = c.Title,
            Content = c.Content,
            Type = c.Type,
            IsExpandable = c.IsExpandable,
            DetailedContent = c.DetailedContent,
            Actions = c.Actions,
        }));
    }

    /// <summary>
    /// Gets the underlying model.
    /// </summary>
    public InfoSection Model => _model;

    /// <summary>
    /// Notifies that localization has changed.
    /// </summary>
    public void NotifyLocalizationChanged()
    {
        Title = ResolveString(_localizationService, $"Info.Section.{Id}.Title", _model.Title);
        Description = ResolveString(_localizationService, $"Info.Section.{Id}.Description", _model.Description);
    }

    /// <summary>
    /// Gets the collection of cards in this section.
    /// </summary>
    public ObservableCollection<InfoCardViewModel> Cards { get; }

    private static string ResolveString(ILocalizationService? loc, string key, string fallback)
    {
        if (loc == null)
        {
            return fallback;
        }

        var val = loc.GetString(key);
        return (!string.IsNullOrEmpty(val) && !string.Equals(val, key, StringComparison.Ordinal)) ? val : fallback;
    }
}
