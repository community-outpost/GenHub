using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Info;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for an info section.
/// </summary>
public partial class InfoSectionViewModel(InfoSection model, ILocalizationService? localizationService = null) : ObservableObject
{
    [ObservableProperty]
    private string _id = model.Id;

    [ObservableProperty]
    private string _title = ResolveString(localizationService, $"Info.Section.{model.Id}.Title", model.Title);

    [ObservableProperty]
    private string _description = ResolveString(localizationService, $"Info.Section.{model.Id}.Description", model.Description);

    [ObservableProperty]
    private int _order = model.Order;

    /// <summary>
    /// Gets the underlying model.
    /// </summary>
    public InfoSection Model => model;

    /// <summary>
    /// Gets the icon kind representing this section.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads observable instance property Id for UI binding")]
    public Material.Icons.MaterialIconKind IconKind => this.Id switch
    {
        InfoConstants.SectionQuickstart => Material.Icons.MaterialIconKind.RocketLaunchOutline,
        InfoConstants.SectionGameProfiles => Material.Icons.MaterialIconKind.AccountMultipleOutline,
        InfoConstants.SectionGameSettings => Material.Icons.MaterialIconKind.TuneVariant,
        InfoConstants.SectionGameProfileContent => Material.Icons.MaterialIconKind.FolderCogOutline,
        InfoConstants.SectionShortcuts => Material.Icons.MaterialIconKind.Launch,
        InfoConstants.SectionSteam => Material.Icons.MaterialIconKind.Steam,
        InfoConstants.SectionLocalContent => Material.Icons.MaterialIconKind.FolderEyeOutline,
        InfoConstants.SectionTools => Material.Icons.MaterialIconKind.HammerWrench,
        InfoConstants.SectionScanGames => Material.Icons.MaterialIconKind.FolderSearchOutline,
        InfoConstants.SectionWorkspaces => Material.Icons.MaterialIconKind.LayersOutline,
        InfoConstants.SectionAppUpdates => Material.Icons.MaterialIconKind.Update,
        InfoConstants.SectionChangelogs => Material.Icons.MaterialIconKind.History,
        InfoConstants.SectionFaq => Material.Icons.MaterialIconKind.HelpCircleOutline,
        InfoConstants.SectionGoChangelog => Material.Icons.MaterialIconKind.ClipboardTextClockOutline,
        _ => Material.Icons.MaterialIconKind.InformationOutline,
    };

    /// <summary>
    /// Gets the collection of cards in this section.
    /// </summary>
    public ObservableCollection<InfoCardViewModel> Cards { get; } = new(model.Cards.Select(c => new InfoCardViewModel(c, model.Id, localizationService)));

    /// <summary>
    /// Notifies that localization has changed.
    /// </summary>
    public void NotifyLocalizationChanged()
    {
        Title = ResolveString(localizationService, $"Info.Section.{Id}.Title", model.Title);
        Description = ResolveString(localizationService, $"Info.Section.{Id}.Description", model.Description);
        foreach (var card in Cards)
        {
            card.NotifyLocalizationChanged();
        }
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
}
