using Avalonia.Data.Converters;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;
using System;
using System.Globalization;

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

    private const string ToolDescriptionKeyFormat = "Tools.Plugin.{0}.Description";

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();

            if (value is IToolPlugin plugin)
            {
                return GetLocalizedDescription(localizationService, plugin.Metadata.Id, plugin.Metadata.Description ?? string.Empty);
            }

            if (value is ToolMetadata metadata)
            {
                return GetLocalizedDescription(localizationService, metadata.Id, metadata.Description ?? string.Empty);
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

    private static string GetLocalizedDescription(ILocalizationService? localizationService, string id, string fallback) =>
        LocalizationConverterHelper.ResolveToolMetadataText(localizationService, ToolDescriptionKeyFormat, id, fallback);
}
