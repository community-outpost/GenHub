using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Provides localized application strings and runtime culture switching.
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>
    /// Gets the cultures backed by the neutral resource or a deployed satellite assembly.
    /// </summary>
    IReadOnlyList<CultureInfo> AvailableCultures { get; }

    /// <summary>
    /// Gets the culture currently used for localized strings and culture-aware formatting.
    /// </summary>
    CultureInfo CurrentCulture { get; }

    /// <summary>
    /// Gets a localized string by resource key.
    /// </summary>
    /// <param name="key">The resource key to resolve.</param>
    /// <returns>The localized value, its English fallback, or the key when no resource exists.</returns>
    string this[string key] { get; }

    /// <summary>
    /// Gets and optionally formats a localized string.
    /// </summary>
    /// <param name="key">The resource key to resolve.</param>
    /// <param name="arguments">Optional format arguments.</param>
    /// <returns>The localized value, its English fallback, or the key when no resource exists.</returns>
    string GetString(string key, params object[] arguments);

    /// <summary>
    /// Changes the active culture when it has a deployed translation.
    /// </summary>
    /// <param name="culture">The culture to activate.</param>
    /// <returns>A result indicating whether the requested culture was available and applied.</returns>
    OperationResult SetCulture(CultureInfo culture);
}
