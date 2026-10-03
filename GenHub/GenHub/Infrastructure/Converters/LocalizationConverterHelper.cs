using Avalonia;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Provides shared helper methods for converters requiring localization services.
/// </summary>
internal static class LocalizationConverterHelper
{
    /// <summary>
    /// Resolves the current application's <see cref="ILocalizationService"/> from resources.
    /// </summary>
    /// <returns>The localization service if available; otherwise, null.</returns>
    public static ILocalizationService? ResolveLocalizationService()
    {
        try
        {
            if (Application.Current?.TryGetResource(LocalizationConstants.ResourceServiceKey, null, out var res) == true &&
                res is ILocalizationService service)
            {
                return service;
            }
        }
        catch (InvalidOperationException)
        {
            // Resource lookup failure fallback
        }

        return null;
    }

    /// <summary>
    /// Gets a localized string for the specified key, or returns the fallback if missing or echoed as key.
    /// </summary>
    /// <param name="localizationService">The localization service.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The fallback value.</param>
    /// <returns>The localized string if available and not equal to the key; otherwise, the fallback.</returns>
    public static string GetLocalizedOrDefault(ILocalizationService? localizationService, string key, string fallback)
    {
        if (localizationService == null || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        if (localizationService.TryGetString(key, out var localized) && !string.IsNullOrEmpty(localized))
        {
            return localized;
        }

        return fallback;
    }

    /// <summary>
    /// Resolves localized metadata text using candidate keys derived from an ID.
    /// </summary>
    /// <param name="localizationService">The localization service.</param>
    /// <param name="keyFormat">The key format string (e.g., "Tools.Plugin.{0}.Name").</param>
    /// <param name="id">The tool or metadata ID.</param>
    /// <param name="fallback">The fallback string to return if no localized value is found.</param>
    /// <returns>The localized metadata text if found; otherwise, the fallback value.</returns>
    public static string ResolveToolMetadataText(
        ILocalizationService? localizationService,
        string keyFormat,
        string id,
        string fallback)
    {
        if (localizationService == null || string.IsNullOrWhiteSpace(id))
        {
            return fallback;
        }

        Span<string> candidates =
        [
            string.Format(CultureInfo.InvariantCulture, keyFormat, id),
            string.Format(CultureInfo.InvariantCulture, keyFormat, id.ToLowerInvariant()),
            string.Format(CultureInfo.InvariantCulture, keyFormat, NormalizeId(id)),
        ];

        foreach (var key in candidates)
        {
            if (localizationService.TryGetString(key, out var localized) && !string.IsNullOrEmpty(localized))
            {
                return localized;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Converts a tool plugin, tool metadata, or raw tool ID to localized text.
    /// </summary>
    /// <param name="value">The converter input value.</param>
    /// <param name="keyFormat">The key format string (e.g., "Tools.Plugin.{0}.Name").</param>
    /// <param name="selectText">Selects the fallback display text from tool metadata.</param>
    /// <returns>The localized text, or the string form of the input when no localization applies.</returns>
    public static string ConvertToolText(
        object? value,
        string keyFormat,
        Func<GenHub.Core.Models.Tools.ToolMetadata, string?> selectText)
    {
        var localizationService = ResolveLocalizationService();

        if (value is GenHub.Core.Interfaces.Tools.IToolPlugin plugin)
        {
            return ResolveToolMetadataText(localizationService, keyFormat, plugin.Metadata.Id, selectText(plugin.Metadata) ?? string.Empty);
        }

        if (value is GenHub.Core.Models.Tools.ToolMetadata metadata)
        {
            return ResolveToolMetadataText(localizationService, keyFormat, metadata.Id, selectText(metadata) ?? string.Empty);
        }

        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return ResolveToolMetadataText(localizationService, keyFormat, text, text);
        }

        return value?.ToString() ?? string.Empty;
    }

    private static string NormalizeId(string id)
    {
        var lastDot = id.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < id.Length - 1)
        {
            return id[(lastDot + 1)..].ToLowerInvariant();
        }

        return id.ToLowerInvariant();
    }
}
