using System.Globalization;

namespace GenHub.Core.Interfaces.Localization;

/// <summary>
/// Provides localization services for retrieving translated strings with runtime language switching.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets or sets the currently active culture.
    /// </summary>
    CultureInfo CurrentCulture { get; set; }

    /// <summary>
    /// Gets all available cultures (languages) in the application.
    /// </summary>
    IReadOnlyList<CultureInfo> AvailableCultures { get; }

    /// <summary>
    /// Gets an observable that emits whenever the language/culture changes.
    /// Subscribe to this for reactive UI updates.
    /// </summary>
    IObservable<CultureInfo> CultureChanged { get; }

    /// <summary>
    /// Gets a localized string for the specified key.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The localized string, or the key if not found.</returns>
    string GetString(string key);

    /// <summary>
    /// Gets a localized string with parameter substitution.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="args">Arguments for string.Format.</param>
    /// <returns>The formatted localized string.</returns>
    string GetString(string key, params object[] args);

    /// <summary>
    /// Changes the active culture/language asynchronously.
    /// </summary>
    /// <param name="culture">The culture to activate.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetCulture(CultureInfo culture);

    /// <summary>
    /// Changes the active culture by language code (e.g., "en", "de") asynchronously.
    /// </summary>
    /// <param name="cultureName">The language code.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetCulture(string cultureName);

    /// <summary>
    /// Refreshes the list of available cultures by rescanning for languages.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RefreshAvailableCultures();
}