using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services;
using GenHub.Features.Content.Services.Catalog;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    private string _tabId = string.Empty;

    [ObservableProperty]
    private string _header = string.Empty;

    [ObservableProperty]
    private int _order = 100;

    [ObservableProperty]
    private string? _intro;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExpiringImageLinks))]
    private string? _body;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isPreviewVisible;

    [ObservableProperty]
    private string? _suggestedTabId;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomTabEditorItem"/> class.
    /// </summary>
    /// <param name="source">The tab to edit, or null for a new tab.</param>
    public CustomTabEditorItem(CatalogTabDefinition? source = null)
    {
        _source = source != null ? Clone(source) : new CatalogTabDefinition();
        CatalogTabValidator.Normalize([_source]);
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
    /// Gets a value indicating whether a card image or a link in the body is a signed Discord attachment
    /// link, which stops working when Discord's signature expires.
    /// </summary>
    public bool HasExpiringImageLinks =>
        (_source.Cards?.Any(card => PublisherImageUrl.IsExpiringDiscordAttachment(card?.ImageUrl)) ?? false) ||
        (!string.IsNullOrEmpty(Body) && UrlRegex().Matches(Body).Any(match => PublisherImageUrl.IsExpiringDiscordAttachment(match.Value)));

    /// <summary>
    /// Gets the runtime tab definition rendered by the live preview, or null while the preview is hidden.
    /// </summary>
    public CustomTabDefinition? Preview => IsPreviewVisible ? CatalogTabMapper.Map(ToDefinition()) : null;

    /// <summary>
    /// Builds the catalog tab definition from the edited fields.
    /// </summary>
    /// <returns>A new catalog tab definition.</returns>
    public CatalogTabDefinition ToDefinition()
    {
        var definition = Clone(_source);
        definition.TabId = string.IsNullOrWhiteSpace(TabId) ? SuggestedTabId ?? string.Empty : TabId.Trim();
        definition.Header = Header.Trim();
        definition.Order = Order;
        definition.Intro = string.IsNullOrWhiteSpace(Intro) ? null : Intro.Trim();
        definition.Body = string.IsNullOrWhiteSpace(Body) ? null : Body;
        definition.IsVisible = IsVisible;
        return definition;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsPreviewVisible) ||
            (IsPreviewVisible && e.PropertyName is nameof(TabId) or nameof(SuggestedTabId) or nameof(Header) or nameof(Order) or nameof(Intro) or nameof(Body)))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Preview)));
        }
    }

    [GeneratedRegex(@"https?://[^\s)\]""'<>]+")]
    private static partial Regex UrlRegex();

    private static CatalogTabDefinition Clone(CatalogTabDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return JsonSerializer.Deserialize<CatalogTabDefinition>(JsonSerializer.Serialize(source)) ?? new CatalogTabDefinition();
    }
}
