using System;
using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;

namespace GenHub.Common.Markup;

/// <summary>
/// Creates a live one-way binding to a localized resource key.
/// </summary>
/// <param name="key">The resource key to bind.</param>
public sealed class LocalizeExtension(string key) : MarkupExtension
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
    /// <param name="serviceProvider">The XAML service provider for the target object.</param>
    /// <returns>A localization binding or the unresolved resource key.</returns>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        if (Application.Current?.TryGetResource(
                LocalizationConstants.ResourceServiceKey,
                theme: null,
                out var resource) != true ||
            resource is not ILocalizationService localizationService)
        {
            return Key;
        }

        return CreateBinding(localizationService);
    }

    /// <summary>
    /// Creates the binding used to resolve and refresh a resource key.
    /// </summary>
    /// <param name="localizationService">The source localization service.</param>
    /// <returns>A live one-way localization binding.</returns>
    internal Binding CreateBinding(ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        return new Binding($"[{Key}]", BindingMode.OneWay)
        {
            Source = localizationService,
        };
    }
}
