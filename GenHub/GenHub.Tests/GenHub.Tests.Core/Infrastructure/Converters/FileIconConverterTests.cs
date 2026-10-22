using GenHub.Core.Constants;
using GenHub.Infrastructure.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Converters;

/// <summary>
/// Unit tests for <see cref="FileIconConverter"/>.
/// </summary>
public class FileIconConverterTests
{
    private readonly CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Verifies that directories resolve to the folder icon path.
    /// </summary>
    [Fact]
    public void Convert_Directory_ReturnsFolderIconPath()
    {
        var converter = new FileIconConverter();

        var result = converter.Convert([true, "anything"], typeof(string), null, _culture);

        Assert.Equal(UiConstants.FileFolderIconPath, result);
    }

    /// <summary>
    /// Verifies that too few values fall back to the text file icon path.
    /// </summary>
    [Fact]
    public void Convert_TooFewValues_ReturnsTextIconPath()
    {
        var converter = new FileIconConverter();

        var result = converter.Convert([false], typeof(string), null, _culture);

        Assert.Equal(UiConstants.FileTextIconPath, result);
    }

    /// <summary>
    /// Verifies that file extensions resolve to the expected icon paths.
    /// </summary>
    /// <param name="extension">The file extension to convert.</param>
    /// <param name="expectedPath">The expected SVG path data.</param>
    [Theory]
    [InlineData("ini", UiConstants.FileConfigIconPath)]
    [InlineData("INI", UiConstants.FileConfigIconPath)]
    [InlineData("png", UiConstants.FileImageIconPath)]
    [InlineData("dds", UiConstants.FileImageIconPath)]
    [InlineData("w3d", UiConstants.FileModelIconPath)]
    [InlineData("lua", UiConstants.FileScriptIconPath)]
    [InlineData("py", UiConstants.FileScriptIconPath)]
    [InlineData("mp3", UiConstants.FileAudioIconPath)]
    [InlineData("ogg", UiConstants.FileAudioIconPath)]
    [InlineData("txt", UiConstants.FileTextIconPath)]
    [InlineData("md", UiConstants.FileTextIconPath)]
    [InlineData("big", UiConstants.FilePackageIconPath)]
    [InlineData("zip", UiConstants.FileArchiveIconPath)]
    [InlineData("7z", UiConstants.FileArchiveIconPath)]
    [InlineData("unknown", UiConstants.FileTextIconPath)]
    [InlineData("", UiConstants.FileTextIconPath)]
    public void Convert_Extension_ReturnsExpectedIconPath(string extension, string expectedPath)
    {
        var converter = new FileIconConverter();

        var result = converter.Convert(new List<object?> { false, extension }, typeof(string), null, _culture);

        Assert.Equal(expectedPath, result);
    }
}
