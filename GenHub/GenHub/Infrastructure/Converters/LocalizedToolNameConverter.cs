using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.DependencyInjection;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;

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

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var localizationService = Ioc.Default.GetService<ILocalizationService>();

        if (value is IToolPlugin plugin)
        {
            return GetLocalizedName(localizationService, plugin.Metadata.Id, plugin.Metadata.Name);
        }

        if (value is ToolMetadata metadata)
        {
            return GetLocalizedName(localizationService, metadata.Id, metadata.Name);
        }

        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return GetLocalizedName(localizationService, text, text);
        }

        return value?.ToString() ?? string.Empty;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string GetLocalizedName(ILocalizationService? localizationService, string id, string fallback)
    {
        if (localizationService == null || string.IsNullOrWhiteSpace(id))
        {
            return fallback;
        }

        var key = $"Tools.Plugin.{id}.Name";
        var localized = localizationService[key];
        if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
        {
            return localized;
        }

        return fallback;
    }
}
