using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Markdig;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts Markdown text to HTML for display.
/// </summary>
public class MarkdownToHtmlConverter : IValueConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string markdown && !string.IsNullOrEmpty(markdown))
        {
            return Markdig.Markdown.ToHtml(markdown, Pipeline);
        }

        return value;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
