using GenHub.Core.Helpers;
using System;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="CloudUrlHelper"/>.
/// </summary>
public class CloudUrlHelperTests
{
    /// <summary>
    /// Verifies that Dropbox URLs are normalized with dl=1.
    /// </summary>
    /// <param name="input">The raw cloud URL.</param>
    /// <param name="expected">The expected normalized direct download URL.</param>
    [Theory]
    [InlineData("https://www.dropbox.com/s/12345/setup.exe?dl=0", "https://www.dropbox.com/s/12345/setup.exe?dl=1")]
    [InlineData("https://www.dropbox.com/s/12345/setup.exe", "https://www.dropbox.com/s/12345/setup.exe?dl=1")]
    [InlineData("https://www.dropbox.com/s/12345/setup.exe?foo=bar", "https://www.dropbox.com/s/12345/setup.exe?foo=bar&dl=1")]
    public void NormalizeCloudUrl_DropboxUrl_NormalizesWithDl1(string input, string expected)
    {
        var result = CloudUrlHelper.NormalizeCloudUrl(input);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that an explicit confirmation anchor href is extracted and decoded.
    /// </summary>
    [Fact]
    public void TryExtractGoogleDriveConfirmationUrl_WithAnchorHref_ExtractsUrl()
    {
        const string html = "<html><body><a id=\"uc-download-link\" class=\"btn\" href=\"https://drive.usercontent.google.com/download?id=1F7RhZoaLJPbO3OSr2aiNbo7yn_n9mZ2m&amp;export=download&amp;confirm=t&amp;uuid=12345\">Download anyway</a></body></html>";
        var requestUri = new Uri("https://drive.google.com/uc?export=download&id=1F7RhZoaLJPbO3OSr2aiNbo7yn_n9mZ2m");

        var success = CloudUrlHelper.TryExtractGoogleDriveConfirmationUrl(html, requestUri, out var confirmedUrl);

        Assert.True(success);
        Assert.NotNull(confirmedUrl);
        Assert.Contains("confirm=t", confirmedUrl);
        Assert.DoesNotContain("&amp;", confirmedUrl);
    }

    /// <summary>
    /// Verifies that a confirmation form action with hidden confirm input is properly extracted.
    /// </summary>
    [Fact]
    public void TryExtractGoogleDriveConfirmationUrl_WithFormActionAndInput_ExtractsUrl()
    {
        const string html = "<html><body><form id=\"download-form\" action=\"https://drive.usercontent.google.com/download?id=12345\" method=\"post\"><input type=\"hidden\" name=\"confirm\" value=\"abc_token\" /></form></body></html>";
        var requestUri = new Uri("https://drive.google.com/uc?export=download&id=12345");

        var success = CloudUrlHelper.TryExtractGoogleDriveConfirmationUrl(html, requestUri, out var confirmedUrl);

        Assert.True(success);
        Assert.NotNull(confirmedUrl);
        Assert.Contains("confirm=abc_token", confirmedUrl);
    }

    /// <summary>
    /// Verifies that a relative confirmation link is correctly resolved using the request base URI.
    /// </summary>
    [Fact]
    public void TryExtractGoogleDriveConfirmationUrl_WithRelativeUrl_ResolvesAbsoluteUri()
    {
        const string html = "<html><body><a href=\"/uc?export=download&amp;id=12345&amp;confirm=xyz\">Download</a></body></html>";
        var requestUri = new Uri("https://drive.google.com/uc?export=download&id=12345");

        var success = CloudUrlHelper.TryExtractGoogleDriveConfirmationUrl(html, requestUri, out var confirmedUrl);

        Assert.True(success);
        Assert.NotNull(confirmedUrl);
        Assert.StartsWith("https://drive.google.com/uc?", confirmedUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirm=xyz", confirmedUrl);
    }

    /// <summary>
    /// Verifies that null, empty, or HTML without a confirmation link returns false.
    /// </summary>
    /// <param name="html">The HTML payload to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body><p>No confirmation links here</p></body></html>")]
    public void TryExtractGoogleDriveConfirmationUrl_InvalidOrMissingConfirm_ReturnsFalse(string? html)
    {
        var requestUri = new Uri("https://drive.google.com/uc?export=download&id=12345");

        var success = CloudUrlHelper.TryExtractGoogleDriveConfirmationUrl(html, requestUri, out var confirmedUrl);

        Assert.False(success);
        Assert.Null(confirmedUrl);
    }
}
