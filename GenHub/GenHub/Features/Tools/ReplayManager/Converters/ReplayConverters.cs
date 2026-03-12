using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Features.Tools.ReplayManager.Converters;

/// <summary>
/// Converts PlayerType enum to a color brush.
/// </summary>
public class PlayerTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlayerType playerType)
        {
            return playerType switch
            {
                PlayerType.Human => new SolidColorBrush(Color.Parse("#2196F3")),
                PlayerType.Computer => new SolidColorBrush(Color.Parse("#FF9800")),
                PlayerType.Observer => new SolidColorBrush(Color.Parse("#9E9E9E")),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }

        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts PlayerType enum to an icon/emoji.
/// </summary>
public class PlayerTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlayerType playerType)
        {
            return playerType switch
            {
                PlayerType.Human => "👤",
                PlayerType.Computer => "🤖",
                PlayerType.Observer => "👁️",
                _ => "?"
            };
        }

        return "?";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts color name string to a color brush.
/// </summary>
public class ColorNameToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorName)
        {
            return colorName.ToLowerInvariant() switch
            {
                "orange" => new SolidColorBrush(Color.Parse("#FF9800")),
                "pink" => new SolidColorBrush(Color.Parse("#E91E63")),
                "blue" => new SolidColorBrush(Color.Parse("#2196F3")),
                "green" => new SolidColorBrush(Color.Parse("#4CAF50")),
                "red" => new SolidColorBrush(Color.Parse("#F44336")),
                "yellow" => new SolidColorBrush(Color.Parse("#FFEB3B")),
                "purple" => new SolidColorBrush(Color.Parse("#9C27B0")),
                "teal" => new SolidColorBrush(Color.Parse("#009688")),
                _ => new SolidColorBrush(Colors.Gray)
            };
        }

        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
