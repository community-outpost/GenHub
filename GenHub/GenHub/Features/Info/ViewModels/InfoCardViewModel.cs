using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Info;
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
    private InfoCardType _type;

    [ObservableProperty]
    private bool _isExpandable;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string? _detailedContent;

    [ObservableProperty]
    private List<InfoAction> _actions = [];

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

    private static string ResolveString(ILocalizationService? loc, string key, string fallback)
    {
        if (loc == null)
        {
            return fallback;
        }

        var val = loc.GetString(key);
        return (!string.IsNullOrEmpty(val) && !string.Equals(val, key, StringComparison.Ordinal)) ? val : fallback;
    }

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
