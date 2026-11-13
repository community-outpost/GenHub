using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Parses publisher catalog JSON into structured models.
/// </summary>
public interface IPublisherCatalogParser
{
    /// <summary>
    /// Parses a catalog from JSON content.
    /// </summary>
    /// <param name="catalogJson">The raw JSON content of the catalog.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed catalog or an error.</returns>
    Task<OperationResult<PublisherCatalog>> ParseCatalogAsync(string catalogJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies import-time defaults to a catalog (display names, same-catalog
    /// dependency publisher ids). This mutates the catalog; validation never does.
    /// </summary>
    /// <param name="catalog">The catalog to normalize.</param>
    void NormalizeCatalog(PublisherCatalog catalog);

    /// <summary>
    /// Validates that a catalog conforms to the expected schema version.
    /// This is a pure check and never mutates the catalog.
    /// </summary>
    /// <param name="catalog">The catalog to validate.</param>
    /// <returns>Validation result with any errors.</returns>
    OperationResult<bool> ValidateCatalog(PublisherCatalog catalog);

    /// <summary>
    /// Verifies the catalog signature if present.
    /// </summary>
    /// <param name="catalogJson">The raw JSON content.</param>
    /// <param name="catalog">The parsed catalog with signature field.</param>
    /// <returns>True if signature is valid or not required.</returns>
    bool VerifySignature(string catalogJson, PublisherCatalog catalog);
}
