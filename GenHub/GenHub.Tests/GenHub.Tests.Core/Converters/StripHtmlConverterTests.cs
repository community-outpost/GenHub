using System;
using System.Globalization;
using GenHub.Infrastructure.Converters;
using Xunit;

namespace GenHub.Tests.Core.Converters;

/// <summary>
/// Unit tests for <see cref="StripHtmlConverter"/>.
/// </summary>
public sealed class StripHtmlConverterTests
{
    private readonly StripHtmlConverter _converter = new();

    /// <summary>
    /// Verifies Convert strips HTML tags and normalizes text.
    /// </summary>
    [Fact]
    public void Convert_WithHtmlMarkup_StripsTags()
    {
        var input = "<p>Test <b>content</b> with <a href=\"#\">links</a>.</p>";
        var result = _converter.Convert(input, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("Test content with links.", result);
    }

    /// <summary>
    /// Verifies Convert with integer parameter truncates and single-lines text.
    /// </summary>
    [Fact]
    public void Convert_WithMaxLenParameter_CleansToSingleLineAndTruncates()
    {
        var input = "<p>First line</p>\n\n<p>Second line with a lot of details here.</p>";
        var result = _converter.Convert(input, typeof(string), 25, CultureInfo.InvariantCulture);

        Assert.Equal("First line Second line...", result);
    }

    /// <summary>
    /// Verifies Convert with string parameter parses integer and truncates.
    /// </summary>
    [Fact]
    public void Convert_WithStringParameter_ParsesAndTruncates()
    {
        var input = "<p>First line</p>\n\n<p>Second line with a lot of details here.</p>";
        var result = _converter.Convert(input, typeof(string), "25", CultureInfo.InvariantCulture);

        Assert.Equal("First line Second line...", result);
    }

    /// <summary>
    /// Verifies Convert handles non-string input by returning value untouched.
    /// </summary>
    [Fact]
    public void Convert_WithScriptAndStyleTags_StripsContents()
    {
        var input = "<style>.hide { display: none; }</style><p>Hello World</p><script>alert('bad');</script>";
        var result = _converter.Convert(input, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("Hello World", result);
    }

    /// <summary>
    /// Verifies Convert handles non-string input by returning value untouched.
    /// </summary>
    [Fact]
    public void Convert_NonStringValue_ReturnsOriginalValue()
    {
        var result = _converter.Convert(42, typeof(int), null, CultureInfo.InvariantCulture);
        Assert.Equal(42, result);
    }

    /// <summary>
    /// Verifies ConvertBack throws NotSupportedException.
    /// </summary>
    [Fact]
    public void ConvertBack_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack("test", typeof(string), null, CultureInfo.InvariantCulture));
    }
}
