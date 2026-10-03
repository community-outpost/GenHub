using GenHub.Features.Content.Services.GenLauncher;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.GenLauncher;

/// <summary>
/// Unit tests for <see cref="GenLauncherDownloadLinkParser"/>.
/// </summary>
public sealed class GenLauncherDownloadLinkParserTests
{
    /// <summary>
    /// Tests that Dropbox links are normalized to direct download links.
    /// </summary>
    /// <param name="input">The raw Dropbox URL.</param>
    /// <param name="expected">The expected direct download URL.</param>
    [Theory]
    [InlineData("https://www.dropbox.com/s/12345/mod.zip?dl=0", "https://www.dropbox.com/s/12345/mod.zip?dl=1")]
    [InlineData("https://www.dropbox.com/s/12345/mod.zip", "https://www.dropbox.com/s/12345/mod.zip?dl=1")]
    [InlineData("https://www.dropbox.com/s/12345/mod.zip?dl=1", "https://www.dropbox.com/s/12345/mod.zip?dl=1")]
    public void ParseDownloadLink_Dropbox_ConvertsDl0ToDl1(string input, string expected)
    {
        var result = GenLauncherDownloadLinkParser.ParseDownloadLink(input);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that OneDrive embed links are converted to direct download URLs.
    /// </summary>
    [Fact]
    public void ParseDownloadLink_OneDriveEmbed_ConvertsToDownload()
    {
        var input = "https://onedrive.live.com/embed?resid=ABC12345!678&authkey=!AAbbcc";
        var result = GenLauncherDownloadLinkParser.ParseDownloadLink(input);
        Assert.Contains("download", result);
        Assert.Contains("resid=ABC12345!678", result);
    }

    /// <summary>
    /// Tests that Google Drive view links are converted to uc?export=download URLs.
    /// </summary>
    [Fact]
    public void ParseDownloadLink_GoogleDriveView_ConvertsToDirectExport()
    {
        var input = "https://drive.google.com/file/d/1B2C3D4E5F6G7H8I9J/view?usp=sharing";
        var result = GenLauncherDownloadLinkParser.ParseDownloadLink(input);
        Assert.Equal("https://drive.google.com/uc?export=download&id=1B2C3D4E5F6G7H8I9J", result);
    }

    /// <summary>
    /// Tests that direct HTTP/HTTPS URLs remain unchanged.
    /// </summary>
    [Fact]
    public void ParseDownloadLink_DirectUrl_ReturnsUnchanged()
    {
        var input = "https://example.com/files/mod.zip";
        var result = GenLauncherDownloadLinkParser.ParseDownloadLink(input);
        Assert.Equal(input, result);
    }
}
