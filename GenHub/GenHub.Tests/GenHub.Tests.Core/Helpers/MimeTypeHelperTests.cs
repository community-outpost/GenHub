using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="MimeTypeHelper"/>.
/// </summary>
public class MimeTypeHelperTests
{
    /// <summary>
    /// Verifies that known artifact extensions resolve to their expected MIME types.
    /// </summary>
    /// <param name="fileName">The file name to resolve.</param>
    /// <param name="expected">The expected MIME type.</param>
    [Theory]
    [InlineData("mod.zip", HostingConstants.ZipContentType)]
    [InlineData("archive.rar", HostingConstants.RarContentType)]
    [InlineData("archive.7z", HostingConstants.SevenZipContentType)]
    [InlineData("gameclient.exe", HostingConstants.ExecutableContentType)]
    [InlineData("setup.msi", HostingConstants.MsiContentType)]
    [InlineData("catalog.json", HostingConstants.JsonContentType)]
    [InlineData("readme.txt", HostingConstants.TextContentType)]
    [InlineData("GAMECLIENT.EXE", HostingConstants.ExecutableContentType)]
    public void FromFileName_KnownExtension_ReturnsExpectedMimeType(string fileName, string expected)
    {
        Assert.Equal(expected, MimeTypeHelper.FromFileName(fileName));
    }

    /// <summary>
    /// Verifies that unknown extensions and empty input fall back to binary.
    /// </summary>
    /// <param name="fileName">The file name to resolve.</param>
    [Theory]
    [InlineData("map.big")]
    [InlineData("file.unknownext")]
    [InlineData("")]
    [InlineData(null)]
    public void FromFileName_UnknownOrEmpty_ReturnsBinary(string? fileName)
    {
        Assert.Equal(HostingConstants.BinaryContentType, MimeTypeHelper.FromFileName(fileName));
    }
}
