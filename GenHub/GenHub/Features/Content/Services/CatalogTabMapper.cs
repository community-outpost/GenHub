using GenHub.Core.Models.Content;
using GenHub.Core.Models.Providers;
using System;
using System.Linq;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Maps catalog tab definitions to the runtime tab definitions rendered by the content detail view.
/// </summary>
public static class CatalogTabMapper
{
    /// <summary>
    /// Parses a catalog tab content type hint.
    /// </summary>
    /// <param name="contentType">The content type string from the catalog.</param>
    /// <param name="parsed">The parsed content type, or <see cref="TabContentType.Custom"/> when unsupported.</param>
    /// <returns><c>true</c> when the hint names a supported content type.</returns>
    public static bool TryParseContentType(string? contentType, out TabContentType parsed)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            !int.TryParse(contentType, out _) &&
            Enum.TryParse(contentType, true, out parsed) &&
            Enum.IsDefined(parsed))
        {
            return true;
        }

        parsed = TabContentType.Custom;
        return false;
    }

    /// <summary>
    /// Maps a catalog tab definition to a runtime tab definition.
    /// </summary>
    /// <param name="catalogTab">The catalog tab definition.</param>
    /// <returns>The runtime tab definition.</returns>
    public static CustomTabDefinition Map(CatalogTabDefinition catalogTab)
    {
        ArgumentNullException.ThrowIfNull(catalogTab);

        TryParseContentType(catalogTab.ContentType, out var contentType);

        return new CustomTabDefinition
        {
            TabId = catalogTab.TabId?.Trim() ?? string.Empty,
            Header = catalogTab.Header?.Trim() ?? string.Empty,
            Icon = catalogTab.Icon,
            Order = catalogTab.Order,
            ContentType = contentType,
            DataSourceUrl = catalogTab.DataSourceUrl,
            ContentTemplate = catalogTab.ContentTemplate,
            Intro = catalogTab.Intro,
            Body = catalogTab.Body,
            Cards = catalogTab.Cards?
                .Where(card => card != null)
                .Select(card => new CustomTabCardDefinition
                {
                    Title = card.Title,
                    Description = card.Description,
                    ImageUrl = card.ImageUrl,
                    Label = card.Label,
                    AccentColor = card.AccentColor,
                })
                .ToList() ?? [],
            Metadata = catalogTab.Metadata ?? [],
            IsVisible = catalogTab.IsVisible,
            LazyLoad = catalogTab.LazyLoad,
        };
    }
}
