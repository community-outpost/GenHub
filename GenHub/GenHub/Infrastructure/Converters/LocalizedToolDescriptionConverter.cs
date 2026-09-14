using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a tool plugin or tool ID/name to its localized description if available.
/// </summary>
public class LocalizedToolDescriptionConverter : IValueConverter
{
    /// <summary>
    /// Gets the shared instance of <see cref="LocalizedToolDescriptionConverter"/>.
    /// </summary>
    public static readonly LocalizedToolDescriptionConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();

            if (value is IToolPlugin plugin)
            {
                return GetLocalizedDescription(localizationService, plugin.Metadata.Id, plugin.Metadata.Description);
            }

            if (value is ToolMetadata metadata)
            {
                return GetLocalizedDescription(localizationService, metadata.Id, metadata.Description);
            }

            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return GetLocalizedDescription(localizationService, text, text);
            }
        }
        catch
        {
            // Converter must never throw
        }

        return value?.ToString() ?? string.Empty;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string GetLocalizedDescription(ILocalizationService? localizationService, string id, string fallback)
    {
        if (localizationService == null || string.IsNullOrWhiteSpace(id))
        {
            return fallback;
        }

        var key = $"Tools.Plugin.{id}.Description";
        var localized = localizationService[key];
        if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
        {
            return localized;
        }

        return fallback;
    }
}
