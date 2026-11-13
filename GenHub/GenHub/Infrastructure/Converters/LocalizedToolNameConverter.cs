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
            return LocalizationConverterHelper.ConvertToolText(value, ToolNameKeyFormat, static metadata => metadata.Name);
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
}
