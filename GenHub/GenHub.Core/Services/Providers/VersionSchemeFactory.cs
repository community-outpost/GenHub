using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Providers;

/// <summary>
/// Factory for resolving version schemes by identifier.
/// </summary>
public class VersionSchemeFactory : IVersionSchemeFactory
{
    private readonly Dictionary<string, IVersionScheme> _schemes;
    private readonly IVersionScheme _defaultScheme;
    private readonly ILogger<VersionSchemeFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionSchemeFactory"/> class.
    /// </summary>
    /// <param name="schemes">The registered version schemes.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="InvalidOperationException">Thrown when the default scheme is not registered.</exception>
    public VersionSchemeFactory(IEnumerable<IVersionScheme> schemes, ILogger<VersionSchemeFactory> logger)
    {
        _logger = logger;
        _schemes = schemes.ToDictionary(scheme => scheme.SchemeId, scheme => scheme, StringComparer.OrdinalIgnoreCase);

        if (!_schemes.TryGetValue(VersionSchemeConstants.Default, out var defaultScheme))
        {
            throw new InvalidOperationException(
                $"The default version scheme '{VersionSchemeConstants.Default}' is not registered.");
        }

        _defaultScheme = defaultScheme;

        _logger.LogDebug(
            "VersionSchemeFactory initialized with {Count} schemes: {Schemes}",
            _schemes.Count,
            string.Join(", ", _schemes.Keys));
    }

    /// <inheritdoc/>
    public IVersionScheme GetScheme(string? schemeId)
    {
        if (string.IsNullOrWhiteSpace(schemeId))
        {
            return _defaultScheme;
        }

        if (_schemes.TryGetValue(schemeId, out var scheme))
        {
            return scheme;
        }

        _logger.LogWarning(
            "No version scheme registered for '{SchemeId}', falling back to '{Default}'",
            schemeId,
            VersionSchemeConstants.Default);

        return _defaultScheme;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetRegisteredSchemes() => _schemes.Keys;
}
