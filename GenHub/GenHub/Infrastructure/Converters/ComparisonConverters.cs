using Avalonia.Data.Converters;
using System;
using System.Globalization;
using System.Linq;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Provides converters for numeric comparisons.
/// </summary>
public static class ComparisonConverters
{
    /// <summary>
    /// A multi-value converter that returns true if the first value is greater than the second.
    /// </summary>
    public static readonly FuncMultiValueConverter<object, bool> GreaterThan = new(args =>
    {
        if (args == null)
            return false;

        var argList = args.Where(a => a is not null).ToList();
        if (argList.Count < 2)
            return false;

        if (TryGetDouble(argList[0], out var val1) && TryGetDouble(argList[1], out var val2))
        {
            return val1 > val2;
        }

        return false;
    });

    /// <summary>
    /// A value converter that returns true if the integer value is greater than zero.
    /// </summary>
    public static readonly IValueConverter IsPositive = new FuncValueConverter<int, bool>(
        count => count > 0);

    /// <summary>
    /// A value converter that returns true if the value is not equal to the converter parameter.
    /// </summary>
    public static readonly IValueConverter IsNotEqualTo = new NotEqualToConverter();

    /// <summary>
    /// Attempts to convert the provided value to a <see cref="double"/> using the invariant culture.
    /// </summary>
    /// <param name="value">The value to convert; may be null.</param>
    /// <param name="result">When this method returns, contains the converted double when successful, or 0 when conversion fails.</param>
    /// <returns><c>true</c> if the value was successfully converted to a double; <c>false</c> otherwise.</returns>
    private static bool TryGetDouble(object? value, out double result)
    {
        if (value == null)
        {
            result = 0;
            return false;
        }

        try
        {
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }
}