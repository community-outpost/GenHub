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
        var formatGroups = parsers.GroupBy(p => p.CatalogFormat, StringComparer.OrdinalIgnoreCase);
        _parsers = new Dictionary<string, ICatalogParser>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in formatGroups)
        {
            // Skip null or whitespace format keys
            if (string.IsNullOrWhiteSpace(group.Key))
            {
                _logger.LogWarning("Skipping catalog parser with null or whitespace format key");
                continue;
            }

            // Use deterministic ordering to avoid non-deterministic behavior
            var parser = group.OrderBy(p => p.GetType().FullName).First();
            _parsers[group.Key] = parser;

            if (group.Count() > 1)
            {
                var allParserTypes = string.Join(", ", group.Select(p => p.GetType().Name));
                _logger.LogWarning("Multiple parsers registered for format '{Format}'. Using {ParserType}. All: {AllTypes}", group.Key, parser.GetType().Name, allParserTypes);
            }
        }

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
