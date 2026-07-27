namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Resolves the registered <see cref="IVersionScheme"/> for a scheme identifier.
/// </summary>
public interface IVersionSchemeFactory
{
    /// <summary>
    /// Gets the scheme for the given identifier, falling back to the default scheme
    /// when the identifier is absent or unregistered.
    /// </summary>
    /// <param name="schemeId">The scheme identifier from a provider definition.</param>
    /// <returns>A usable version scheme.</returns>
    IVersionScheme GetScheme(string? schemeId);

    /// <summary>
    /// Gets the identifiers of every registered scheme.
    /// </summary>
    /// <returns>The registered scheme identifiers.</returns>
    IEnumerable<string> GetRegisteredSchemes();
}
