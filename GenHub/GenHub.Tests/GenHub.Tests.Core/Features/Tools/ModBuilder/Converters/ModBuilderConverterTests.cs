// <copyright file="ModBuilderConverterTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Converters;

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using GenHub.Infrastructure.Converters;
using Xunit;

/// <summary>
/// Unit tests for ModBuilder XAML value converters.
/// </summary>
public class ModBuilderConverterTests
{
    [Fact]
    public void ActiveBorderConverter_WhenActive_ReturnsCyanBrush()
    {
        var converter = new ActiveBorderConverter();

        var result = converter.Convert(true, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.NotNull(result);
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(result);
        Assert.Equal(Color.Parse("#00D9FF"), brush.Color);
    }

    [Fact]
    public void ActiveBorderConverter_WhenInactive_ReturnsDefaultBrush()
    {
        var converter = new ActiveBorderConverter();

        var result = converter.Convert(false, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.NotNull(result);
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(result);
        Assert.Equal(Color.Parse("#20FFFFFF"), brush.Color);
    }

    [Fact]
    public void ActiveBorderConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var converter = new ActiveBorderConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(null, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 16)]
    [InlineData(2, 32)]
    [InlineData(3, 48)]
    public void IndentConverter_GivenIndentLevel_ReturnsExpectedLeftMargin(int level, double expectedLeft)
    {
        var converter = new IndentConverter();

        var result = converter.Convert(level, typeof(Thickness), null, CultureInfo.InvariantCulture);

        Assert.NotNull(result);
        var thickness = Assert.IsType<Thickness>(result);
        Assert.Equal(expectedLeft, thickness.Left);
        Assert.Equal(0, thickness.Top);
        Assert.Equal(0, thickness.Right);
        Assert.Equal(0, thickness.Bottom);
    }

    [Fact]
    public void IndentConverter_ConvertBack_ThrowsNotSupportedException()
    {
        var converter = new IndentConverter();

        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(null, typeof(int), null, CultureInfo.InvariantCulture));
    }
}
