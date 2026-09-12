// <copyright file="ActiveBorderConverter.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Infrastructure.Converters;

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

/// <summary>
/// Converts a boolean active state to an active border brush or transparent/default border brush.
/// </summary>
public class ActiveBorderConverter : IValueConverter
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#00D9FF"));
    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#20FFFFFF"));

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive && isActive)
        {
            return ActiveBrush;
        }

        return InactiveBrush;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
