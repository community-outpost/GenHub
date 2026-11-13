using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.GeneralsOnline;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a Generals Online lobby state to its status brush.
/// </summary>
public class GeneralsOnlineLobbyStateBrushConverter : IValueConverter
{
    private static readonly IBrush WaitingBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusSuccessColor));
    private static readonly IBrush InProgressBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusUpdateAvailableColor));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse(UiConstants.StatusInactiveColor));

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GeneralsOnlineLobbyState state)
        {
            return state switch
            {
                GeneralsOnlineLobbyState.GameSetup => WaitingBrush,
                GeneralsOnlineLobbyState.InGame => InProgressBrush,
                _ => UnknownBrush,
            };
        }

        return UnknownBrush;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
