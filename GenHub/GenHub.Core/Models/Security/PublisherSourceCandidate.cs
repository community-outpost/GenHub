using GenHub.Core.Models.Providers;

namespace GenHub.Core.Models.Security;

/// <summary>
/// The source a user is about to subscribe to.
/// </summary>
/// <param name="PublisherId">The effective publisher ID.</param>
/// <param name="DefinitionUrl">The URL the definition was fetched from, or null for a catalog-direct subscription.</param>
/// <param name="CatalogUrl">The catalog URL the subscription will use.</param>
/// <param name="Definition">The fetched publisher definition, or null for a catalog-direct subscription.</param>
public sealed record PublisherSourceCandidate(
    string PublisherId,
    string? DefinitionUrl,
    string CatalogUrl,
    PublisherDefinition? Definition);
