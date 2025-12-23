using System.Globalization;
using System.Reactive.Subjects;
using GenHub.Core.Interfaces.Localization;
using GenHub.Core.Models.Localization;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Localization;

/// <summary>
/// Provides localization services with runtime language switching and reactive updates.
/// </summary>
public class LocalizationService : ILocalizationService, IDisposable
{
    private readonly ILanguageProvider _languageProvider;
    private readonly ILogger<LocalizationService> _logger;
    private readonly LocalizationOptions _options;
    private readonly Subject<CultureInfo> _cultureChangedSubject;
    private readonly object _lock = new();
    private CultureInfo _currentCulture;
    private IReadOnlyList<CultureInfo>? _availableCultures;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationService"/> class.
    /// </summary>
    /// <param name="languageProvider">The language provider for resource management.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The localization options.</param>
    public LocalizationService(
        ILanguageProvider languageProvider,
        ILogger<LocalizationService> logger,
        LocalizationOptions? options = null)
    {
        _languageProvider = languageProvider ?? throw new ArgumentNullException(nameof(languageProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new LocalizationOptions();
        _cultureChangedSubject = new Subject<CultureInfo>();

        // Initialize with default culture
        _currentCulture = GetCultureFromString(_options.DefaultCulture);
        SetThreadCulture(_currentCulture);

        _logger.LogInformation("LocalizationService initialized with culture: {Culture}", _currentCulture.Name);
    }

    /// <inheritdoc/>
    public CultureInfo CurrentCulture
    {
        get
        {
            lock (_lock)
            {
                return _currentCulture;
            }
        }

        set
        {
            SetCulture(value).GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<CultureInfo> AvailableCultures
    {
        get
        {
            if (_availableCultures != null)
            {
                return _availableCultures;
            }

            // Synchronously get cached or discover
            _availableCultures = _languageProvider.DiscoverAvailableLanguages().GetAwaiter().GetResult();
            return _availableCultures;
        }
    }

    /// <inheritdoc/>
    public IObservable<CultureInfo> CultureChanged => _cultureChangedSubject;

    /// <summary>
    /// Gets a localized string from the specified resource set.
    /// </summary>
    /// <param name="resourceSet">The resource set base name (e.g., from StringResources constants).</param>
    /// <param name="key">The resource key.</param>
    /// <returns>The localized string, or a fallback if not found.</returns>
    public string GetString(string resourceSet, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceSet, nameof(resourceSet));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));

        try
        {
            var resourceManager = _languageProvider.GetResourceManager(resourceSet);

            lock (_lock)
            {
                var value = resourceManager.GetString(key, _currentCulture);

                if (string.IsNullOrEmpty(value))
                {
                    return HandleMissingTranslation(key);
                }

                return value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving resource string for key: {Key} from resource set: {ResourceSet}", key, resourceSet);
            return HandleMissingTranslation(key);
        }
    }

    /// <summary>
    /// Gets a formatted localized string from the specified resource set.
    /// </summary>
    /// <param name="resourceSet">The resource set base name (e.g., from StringResources constants).</param>
    /// <param name="key">The resource key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted localized string, or a fallback if not found.</returns>
    public string GetString(string resourceSet, string key, params object[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceSet, nameof(resourceSet));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));

        try
        {
            var format = GetString(resourceSet, key);

            if (args == null || args.Length == 0)
            {
                return format;
            }

            return string.Format(_currentCulture, format, args);
        }
        catch (FormatException ex)
        {
            _logger.LogError(
                ex,
                "Format error for key '{Key}' from resource set '{ResourceSet}' with {ArgCount} arguments",
                key,
                resourceSet,
                args?.Length ?? 0);

            return HandleMissingTranslation(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error formatting resource string for key: {Key} from resource set: {ResourceSet}", key, resourceSet);
            return HandleMissingTranslation(key);
        }
    }

    /// <inheritdoc/>
    public async Task SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture, nameof(culture));

        await Task.Run(() =>
        {
            lock (_lock)
            {
                try
                {
                    // Validate the culture is available
                    if (!_languageProvider.ValidateCulture(culture))
                    {
                        _logger.LogWarning(
                            "Culture '{Culture}' is not available. Falling back to '{Fallback}'",
                            culture.Name,
                            _options.FallbackCulture);

                        culture = GetCultureFromString(_options.FallbackCulture);
                    }

                    // Set the culture
                    _currentCulture = culture;
                    SetThreadCulture(culture);

                    _logger.LogInformation("Culture changed to: {Culture}", culture.Name);

                    // Notify observers
                    _cultureChangedSubject.OnNext(culture);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error setting culture to: {Culture}", culture.Name);
                    throw;
                }
            }
        });
    }

    /// <inheritdoc/>
    public async Task SetCulture(string cultureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName, nameof(cultureName));

        try
        {
            var culture = GetCultureFromString(cultureName);
            await SetCulture(culture);
        }
        catch (CultureNotFoundException ex)
        {
            _logger.LogError(ex, "Invalid culture name: {CultureName}", cultureName);
            throw new ArgumentException($"Invalid culture name: {cultureName}", nameof(cultureName), ex);
        }
    }

    /// <inheritdoc/>
    public async Task RefreshAvailableCultures()
    {
        _logger.LogDebug("Refreshing available cultures");

        _availableCultures = await _languageProvider.DiscoverAvailableLanguages();

        _logger.LogInformation(
            "Available cultures refreshed. Found {Count} cultures",
            _availableCultures.Count);
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cultureChangedSubject?.OnCompleted();
            _cultureChangedSubject?.Dispose();
        }

        _disposed = true;
    }

    /// <summary>
    /// Sets the thread culture for both CurrentCulture and CurrentUICulture.
    /// </summary>
    /// <param name="culture">The culture to set.</param>
    private void SetThreadCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    /// <summary>
    /// Gets a CultureInfo from a culture string, with error handling.
    /// </summary>
    /// <param name="cultureName">The culture name.</param>
    /// <returns>The CultureInfo instance.</returns>
    private CultureInfo GetCultureFromString(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "Culture '{Culture}' not found. Using invariant culture",
                cultureName);

            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>
    /// Handles missing translation keys according to configuration.
    /// </summary>
    /// <param name="key">The missing translation key.</param>
    /// <returns>A placeholder string for the missing translation.</returns>
    private string HandleMissingTranslation(string key)
    {
        if (_options.LogMissingTranslations)
        {
            _logger.LogWarning(
                "Missing translation for key '{Key}' in culture '{Culture}'",
                key,
                _currentCulture.Name);
        }

        if (_options.ThrowOnMissingTranslation)
        {
            throw new InvalidOperationException(
                $"Missing translation for key '{key}' in culture '{_currentCulture.Name}'");
        }

        // In development, return key with markers; in production, return clean key
        return _options.LogMissingTranslations ? $"[{key}]" : key;
    }
}