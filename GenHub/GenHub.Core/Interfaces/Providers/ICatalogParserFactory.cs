namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Factory for creating catalog parsers based on catalog format.
/// </summary>
public interface ICatalogParserFactory
{
    /// <summary>
    /// Gets a catalog parser for the specified format.
    /// </summary>
    /// <param name="catalogFormat">The catalog format identifier (e.g., "genpatcher-dat").</param>
    /// <returns>The catalog parser, or null if no parser is registered for the format.</returns>
    ICatalogParser? GetParser(string catalogFormat);

    /// <summary>
    /// Gets all registered catalog formats.
    /// </summary>
    /// <returns>The registered catalog format identifiers.</returns>
    IEnumerable<string> GetRegisteredFormats();
}
