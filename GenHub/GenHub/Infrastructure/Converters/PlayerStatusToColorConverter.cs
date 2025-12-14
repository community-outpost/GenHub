using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.GeneralsOnline;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a <see cref="PlayerStatus"/> to a color string for UI display.
/// </summary>
public class PlayerStatusToColorConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PlayerStatus status)
        {
            return "#64748b"; // Default gray
        }

        return status switch
        {
            PlayerStatus.InGame => "#22c55e",     // Green
            PlayerStatus.GameSetup => "#f59e0b",  // Amber
            PlayerStatus.InChatRoom => "#6366f1", // Indigo
            _ => "#64748b"                       // Slate gray
        };
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}