// <copyright file="IndentConverter.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Infrastructure.Converters;

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

/// <summary>
/// Converts an integer indentation level to an Avalonia Thickness margin for tree views.
/// </summary>
public class IndentConverter : IValueConverter
{
    private const double IndentSize = 16.0;

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int indentLevel)
        {
            return new Thickness(indentLevel * IndentSize, 0, 0, 0);
        }

        return new Thickness(0);
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
