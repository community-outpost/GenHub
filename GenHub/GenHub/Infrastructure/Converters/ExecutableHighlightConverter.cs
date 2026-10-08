using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converter that highlights executable files with different colors based on selection state.
/// </summary>
public class ExecutableHighlightConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush ExecutableBrush = new(Color.Parse(UiConstants.ExecutableHighlightColor)); // Light blue for executables
    private static readonly SolidColorBrush SelectedExecutableBrush = new(Color.Parse(UiConstants.StatusSuccessColor)); // Green for selected

    /// <inheritdoc />
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return AvaloniaProperty.UnsetValue;
        }

        var isExecutable = values[0] is true;
        var isSelected = values[1] is true;

        if (isSelected)
        {
            return SelectedExecutableBrush;
        }

        if (isExecutable)
        {
            return ExecutableBrush;
        }

        return AvaloniaProperty.UnsetValue;
    }
}
