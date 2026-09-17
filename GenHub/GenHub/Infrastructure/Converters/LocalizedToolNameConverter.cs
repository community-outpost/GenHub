using Avalonia.Data.Converters;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a tool plugin or tool ID/name to its localized display name if available.
/// </summary>
public class LocalizedToolNameConverter : IValueConverter
{
    /// <summary>
    /// Gets the shared instance of <see cref="LocalizedToolNameConverter"/>.
    /// </summary>
    public static readonly LocalizedToolNameConverter Instance = new();

    private const string ToolNameKeyFormat = "Tools.Plugin.{0}.Name";

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();

            if (value is IToolPlugin plugin)
            {
                return GetLocalizedName(localizationService, plugin.Metadata.Id, plugin.Metadata.Name ?? string.Empty);
            }

            if (value is ToolMetadata metadata)
            {
                return GetLocalizedName(localizationService, metadata.Id, metadata.Name ?? string.Empty);
            }

            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return GetLocalizedName(localizationService, text, text);
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

    private static string GetLocalizedName(ILocalizationService? localizationService, string id, string fallback) =>
        LocalizationConverterHelper.ResolveToolMetadataText(localizationService, ToolNameKeyFormat, id, fallback);
}
