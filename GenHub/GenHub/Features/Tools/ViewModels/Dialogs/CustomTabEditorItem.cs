using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services;
using System;
using System.Text.Json;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// An editable publisher-defined custom tab on a content item in Publisher Studio.
/// </summary>
/// <remarks>
/// Fields that Studio does not edit, such as cards and metadata, are kept from the source tab
/// so authoring a tab in Studio never drops data written by hand in the catalog.
/// </remarks>
public partial class CustomTabEditorItem : ObservableObject
{
    private readonly CatalogTabDefinition _source;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _tabId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string _header = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private int _order = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string? _intro;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    private string? _body;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isPreviewVisible;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomTabEditorItem"/> class.
    /// </summary>
    /// <param name="source">The tab to edit, or null for a new tab.</param>
    public CustomTabEditorItem(CatalogTabDefinition? source = null)
    {
        _source = source != null ? Clone(source) : new CatalogTabDefinition();
        TabId = _source.TabId ?? string.Empty;
        Header = _source.Header ?? string.Empty;
        Order = _source.Order;
        Intro = _source.Intro;
        Body = _source.Body;
        IsVisible = _source.IsVisible;
    }

    /// <summary>
    /// Gets the number of cards kept from the source tab.
    /// </summary>
    public int CardCount => _source.Cards?.Count ?? 0;

    /// <summary>
    /// Gets the runtime tab definition rendered by the live preview.
    /// </summary>
    public CustomTabDefinition Preview => CatalogTabMapper.Map(ToDefinition());

    /// <summary>
    /// Builds the catalog tab definition from the edited fields.
    /// </summary>
    /// <returns>A new catalog tab definition.</returns>
    public CatalogTabDefinition ToDefinition()
    {
        var definition = Clone(_source);
        definition.TabId = TabId.Trim();
        definition.Header = Header.Trim();
        definition.Order = Order;
        definition.Intro = string.IsNullOrWhiteSpace(Intro) ? null : Intro.Trim();
        definition.Body = string.IsNullOrWhiteSpace(Body) ? null : Body;
        definition.IsVisible = IsVisible;
        return definition;
    }

    private static CatalogTabDefinition Clone(CatalogTabDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return JsonSerializer.Deserialize<CatalogTabDefinition>(JsonSerializer.Serialize(source)) ?? new CatalogTabDefinition();
    }
}
