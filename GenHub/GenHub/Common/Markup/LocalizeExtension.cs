using System;
using Avalonia;
using Avalonia.Data;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;

namespace GenHub.Common.Markup;

/// <summary>
/// Creates a live one-way binding to a localized resource key.
/// </summary>
/// <param name="key">The resource key to bind.</param>
public sealed class LocalizeExtension(string key)
{
    /// <summary>
    /// Gets the resource key resolved by the extension.
    /// </summary>
    public string Key { get; } = string.IsNullOrWhiteSpace(key)
        ? throw new ArgumentException("A localization resource key is required.", nameof(key))
        : key;

    /// <summary>
    /// Provides a live binding when localization is initialized, or the key for design-time fallback.
    /// </summary>
    /// <returns>A localization binding or the unresolved resource key.</returns>
    public object ProvideValue()
    {
        if (Application.Current?.TryGetResource(
                LocalizationConstants.ResourceServiceKey,
                theme: null,
                out var resource) != true ||
            resource is not ILocalizationService localizationService)
        {
            return Key;
        }

        return new Binding($"[{Key}]", BindingMode.OneWay)
        {
            FallbackValue = Key,
            Source = localizationService,
        };
    }
}
