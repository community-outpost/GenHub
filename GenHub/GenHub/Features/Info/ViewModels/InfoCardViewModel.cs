using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Info;
using GenHub.Infrastructure.Converters;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for an individual information card.
/// </summary>
public partial class InfoCardViewModel : ObservableObject
{
    private readonly InfoCard? _model;
    private readonly string _sectionId = string.Empty;
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    private InfoCardType _type;

    [ObservableProperty]
    private bool _isExpandable;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string? _detailedContent;

    [ObservableProperty]
    private List<InfoAction> _actions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    private Material.Icons.MaterialIconKind? _customIconKind;

    /// <summary>
    /// Gets or sets an optional associated target object (e.g. changelog release or patch note).
    /// </summary>
    public object? TargetItem { get; set; }

    /// <summary>
    /// Gets the unique identifier of the underlying card model.
    /// </summary>
    public string Id => _model?.Id ?? string.Empty;

    /// <summary>
    /// Gets the underlying card model.
    /// </summary>
    public InfoCard? Model => _model;

    /// <summary>
    /// Gets the icon kind representing this card.
    /// </summary>
    public Material.Icons.MaterialIconKind IconKind => CustomIconKind ?? Type switch
    {
        InfoCardType.HowTo => Material.Icons.MaterialIconKind.LightbulbOutline,
        InfoCardType.Feature => Material.Icons.MaterialIconKind.StarOutline,
        InfoCardType.Concept => Material.Icons.MaterialIconKind.BookOpenPageVariantOutline,
        InfoCardType.Warning => Material.Icons.MaterialIconKind.AlertCircleOutline,
        InfoCardType.Tip => Material.Icons.MaterialIconKind.LightbulbOnOutline,
        InfoCardType.Example => Material.Icons.MaterialIconKind.PlayCircleOutline,
        _ => Material.Icons.MaterialIconKind.FileDocumentOutline,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="InfoCardViewModel"/> class with default values.
    /// </summary>
    public InfoCardViewModel()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InfoCardViewModel"/> class from an <see cref="InfoCard"/> model.
    /// </summary>
    /// <param name="model">The card model.</param>
    /// <param name="sectionId">The ID of the section this card belongs to.</param>
    /// <param name="localizationService">Optional localization service for dynamic updates.</param>
    public InfoCardViewModel(InfoCard model, string sectionId, ILocalizationService? localizationService = null)
    {
        _model = model;
        _sectionId = sectionId;
        _localizationService = localizationService;
        _type = model.Type;
        _isExpandable = model.IsExpandable;
        _isExpanded = false;

        UpdateLocalizedContent();
    }

    /// <summary>
    /// Notifies that localization has changed and refreshes localized text.
    /// </summary>
    public void NotifyLocalizationChanged()
    {
        UpdateLocalizedContent();
    }

    private static string ResolveString(ILocalizationService? loc, string key, string fallback) =>
        LocalizationConverterHelper.GetLocalizedOrDefault(loc, key, fallback);

    [RelayCommand]
    private void ToggleExpansion()
    {
        if (IsExpandable)
        {
            IsExpanded = !IsExpanded;
        }
    }

    private void UpdateLocalizedContent()
    {
        if (_model == null)
        {
            return;
        }

        var cardKey = string.IsNullOrEmpty(_model.Id) ? string.Empty : $".{_model.Id}";
        Title = ResolveString(_localizationService, $"Info.Card.{_sectionId}{cardKey}.Title", _model.Title);
        Content = ResolveString(_localizationService, $"Info.Card.{_sectionId}{cardKey}.Content", _model.Content);

        if (!string.IsNullOrEmpty(_model.DetailedContent))
        {
            DetailedContent = ResolveString(_localizationService, $"Info.Card.{_sectionId}{cardKey}.DetailedContent", _model.DetailedContent);
        }
        else
        {
            DetailedContent = null;
        }

        if (_model.Actions != null && _model.Actions.Count > 0)
        {
            var localizedActions = new List<InfoAction>(_model.Actions.Count);
            for (int i = 0; i < _model.Actions.Count; i++)
            {
                var action = _model.Actions[i];
                var localizedLabel = string.Empty;

                if (!string.IsNullOrEmpty(action.ActionId))
                {
                    localizedLabel = ResolveString(
                        _localizationService,
                        $"Info.Card.{_sectionId}{cardKey}.Action.{action.ActionId}",
                        string.Empty);
                }

                if (string.IsNullOrEmpty(localizedLabel))
                {
                    localizedLabel = ResolveString(
                        _localizationService,
                        $"Info.Card.{_sectionId}{cardKey}.Action.{i}",
                        action.Label);
                }

                localizedActions.Add(new InfoAction
                {
                    ActionId = action.ActionId,
                    Label = localizedLabel,
                    IconKey = action.IconKey,
                    IsPrimary = action.IsPrimary,
                });
            }

            Actions = localizedActions;
        }
        else
        {
            Actions = _model.Actions ?? [];
        }
    }
}
