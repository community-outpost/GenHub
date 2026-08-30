using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a <see cref="ContentState"/> enum value to a compact emoji indicator
/// suitable for space-constrained UI like the variant dropdown.
/// </summary>
public class ContentStateToTextConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is ContentState state
            ? state switch
            {
                ContentState.Downloaded => "✅",
                ContentState.UpdateAvailable => "🔄",
                _ => "⇩",
            }
            : "⇩";
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
