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
    /// <summary>
    /// Parse raw publisher catalog JSON into a PublisherCatalog model and produce an operation result.
    /// </summary>
    /// <param name="catalogJson">Raw JSON content of the publisher catalog.</param>
    /// <param name="cancellationToken">Token to cancel the parse operation.</param>
    /// <returns>An OperationResult containing the parsed PublisherCatalog on success, or errors describing why parsing failed.</returns>
    Task<OperationResult<PublisherCatalog>> ParseCatalogAsync(string catalogJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a catalog conforms to the expected schema version.
    /// </summary>
    /// <param name="catalog">The catalog to validate.</param>
    /// <summary>
    /// Validates that a PublisherCatalog conforms to the expected schema version and structure.
    /// </summary>
    /// <param name="catalog">The parsed publisher catalog to validate.</param>
    /// <returns>`true` if the catalog is valid; `false` otherwise. The OperationResult contains validation errors when present.</returns>
    OperationResult<bool> ValidateCatalog(PublisherCatalog catalog);

    /// <summary>
    /// Verifies the catalog signature if present.
    /// </summary>
    /// <param name="catalogJson">The raw JSON content.</param>
    /// <param name="catalog">The parsed catalog with signature field.</param>
    /// <summary>
/// Verifies the publisher catalog's signature when present.
/// </summary>
/// <param name="catalogJson">The raw catalog JSON used for signature verification.</param>
/// <param name="catalog">The parsed PublisherCatalog which may include signature metadata.</param>
/// <returns>`true` if the signature is valid or verification is not required, `false` otherwise.</returns>
    bool VerifySignature(string catalogJson, PublisherCatalog catalog);
}