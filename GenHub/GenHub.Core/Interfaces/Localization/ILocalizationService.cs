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
    /// Gets a localized string from the specified resource set.
    /// </summary>
    /// <param name="resourceSet">The resource set base name (e.g., from StringResources constants).</param>
    /// <param name="key">The resource key.</param>
    /// <returns>The localized string, or a fallback if not found.</returns>
    string GetString(string resourceSet, string key);

    /// <summary>
    /// Gets a formatted localized string from the specified resource set.
    /// </summary>
    /// <param name="resourceSet">The resource set base name (e.g., from StringResources constants).</param>
    /// <param name="key">The resource key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted localized string, or a fallback if not found.</returns>
    string GetString(string resourceSet, string key, params object[] args);

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