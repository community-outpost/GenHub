using GenHub.Core.Interfaces.Providers;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Providers;

/// <summary>
/// Factory for creating catalog parsers based on catalog format.
/// </summary>
public class CatalogParserFactory : ICatalogParserFactory
{
    private readonly Dictionary<string, ICatalogParser> _parsers;
    private readonly ILogger<CatalogParserFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogParserFactory"/> class.
    /// </summary>
    /// <param name="parsers">The registered catalog parsers.</param>
    /// <param name="logger">The logger instance.</param>
    public CatalogParserFactory(
        IEnumerable<ICatalogParser> parsers,
        ILogger<CatalogParserFactory> logger)
    {
        _logger = logger;
        _parsers = parsers.ToDictionary(p => p.CatalogFormat, p => p, StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug(
            "CatalogParserFactory initialized with {Count} parsers: {Formats}",
            _parsers.Count,
            string.Join(", ", _parsers.Keys));
    }

    /// <inheritdoc/>
    public ICatalogParser? GetParser(string catalogFormat)
    {
        if (string.IsNullOrWhiteSpace(catalogFormat))
        {
            _logger.LogWarning("GetParser called with null or empty catalog format");
            return null;
        }

        if (_parsers.TryGetValue(catalogFormat, out var parser))
        {
            _logger.LogDebug("Found parser for catalog format '{Format}'", catalogFormat);
            return parser;
        }

        _logger.LogWarning("No parser registered for catalog format '{Format}'", catalogFormat);
        return null;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetRegisteredFormats()
    {
        return _parsers.Keys;
    }
}
