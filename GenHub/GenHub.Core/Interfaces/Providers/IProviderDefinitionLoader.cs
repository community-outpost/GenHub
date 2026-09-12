using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Interface for loading and managing provider definitions from external configuration.
/// Supports both embedded default providers and user-added custom providers.
/// </summary>
public interface IProviderDefinitionLoader
{
    /// <summary>
    /// Loads all provider definitions from the configured sources.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing all loaded provider definitions.</returns>
    Task<OperationResult<IEnumerable<ProviderDefinition>>> LoadProvidersAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a provider definition by ID.
    /// </summary>
    /// <param name="providerId">The provider ID to look up.</param>
    /// <returns>The provider definition, or null if not found.</returns>
    ProviderDefinition? GetProvider(string providerId);

    /// <summary>
    /// Gets all currently loaded provider definitions.
    /// </summary>
    /// <returns>All loaded provider definitions.</returns>
    IEnumerable<ProviderDefinition> GetAllProviders();

    /// <summary>
    /// Gets provider definitions by type (static or dynamic).
    /// </summary>
    /// <param name="providerType">The provider type to filter by.</param>
    /// <returns>Provider definitions matching the specified type.</returns>
    IEnumerable<ProviderDefinition> GetProvidersByType(ProviderType providerType);

    /// <summary>
    /// Reloads provider definitions from disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<OperationResult<bool>> ReloadProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a custom provider definition (from user configuration).
    /// </summary>
    /// <param name="definition">The provider definition to add.</param>
    /// <returns>A result indicating success or failure.</returns>
    OperationResult<bool> AddCustomProvider(ProviderDefinition definition);

    /// <summary>
    /// Removes a custom provider definition.
    /// </summary>
    /// <param name="providerId">The provider ID to remove.</param>
    /// <returns>A result indicating success or failure.</returns>
    OperationResult<bool> RemoveCustomProvider(string providerId);
}
