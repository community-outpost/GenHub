using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Shows an element only when a user id is valid and differs from the signed-in user.
/// Binds the row user id first and the signed-in user id second.
/// </summary>
public class NotSelfVisibilityConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return false;
        }

        var userId = ToUserId(values[0]);
        var selfId = ToUserId(values[1]);
        return userId > 0 && userId != selfId;
    }

    private static long ToUserId(object? value)
    {
        return value switch
        {
            long id => id,
            int intId => intId,
            string text when long.TryParse(text, out var parsed) => parsed,
            _ => -1,
        };
    }
}
