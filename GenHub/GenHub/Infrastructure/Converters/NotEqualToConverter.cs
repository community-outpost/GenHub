using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converter that returns true if the value is not equal to the parameter.
/// </summary>
internal sealed class NotEqualToConverter : IValueConverter
{
    /// <summary>
    /// Determines whether the provided value is not equal to the specified parameter.
    /// </summary>
    /// <param name="value">The value to compare.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">The value to compare against.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>`true` if exactly one of <paramref name="value"/> or <paramref name="parameter"/> is null or if they are not equal; `false` if both are null or they are equal.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null)
        {
            return false;
        }

        if (value == null || parameter == null)
        {
            return true;
        }

        return !value.Equals(parameter);
    }

    /// <summary>
    /// Converts a value from the target back to a source value: if the incoming value is the boolean `false`, returns the converter parameter; otherwise signals no action.
    /// </summary>
    /// <param name="value">The value produced by the binding target (expected to be a boolean).</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <param name="parameter">The converter parameter to return when <paramref name="value"/> is `false`.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>The <paramref name="parameter"/> when <paramref name="value"/> is `false`; otherwise <see cref="Avalonia.Data.BindingOperations.DoNothing"/>.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // For NotEqualTo, returning the parameter when value is false (meaning they are equal)
        // effectively sets the source to a value it already equals.
        // We only return parameter if we want two-way binding to update the source.
        if (value is bool b && !b)
        {
            return parameter;
        }

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}