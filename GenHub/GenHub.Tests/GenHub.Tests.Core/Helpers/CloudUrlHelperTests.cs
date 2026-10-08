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
    public void NormalizeDirectDownloadUrl_DropboxUrl_NormalizesWithDl1(string input, string expected)
    {
        var result = CloudUrlHelper.NormalizeDirectDownloadUrl(input);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that Google Drive URLs with uc?id= or open?id= or file/d/ are normalized to direct download links.
    /// </summary>
    /// <param name="input">The raw Google Drive URL.</param>
    /// <param name="expected">The expected direct download URL.</param>
    [Theory]
    [InlineData("https://drive.google.com/uc?id=1234567890abcdef", "https://drive.google.com/uc?export=download&id=1234567890abcdef")]
    [InlineData("https://drive.google.com/open?id=1234567890abcdef", "https://drive.google.com/uc?export=download&id=1234567890abcdef")]
    [InlineData("https://drive.google.com/file/d/1234567890abcdef/view", "https://drive.google.com/uc?export=download&id=1234567890abcdef")]
    public void NormalizeDirectDownloadUrl_GoogleDriveUrl_NormalizesToDirectDownload(string input, string expected)
    {
        var result = CloudUrlHelper.NormalizeDirectDownloadUrl(input);
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
        const string html = "<html><body><form id=\"download-form\" method=\"post\" action=\"https://drive.usercontent.google.com/download?id=12345\"><input type=\"hidden\" value=\"abc_token\" name=\"confirm\" /></form></body></html>";
        var requestUri = new Uri("https://drive.google.com/uc?export=download&id=12345");

        var success = CloudUrlHelper.TryExtractGoogleDriveConfirmationUrl(html, requestUri, out var confirmedUrl);

        Assert.True(success);
        Assert.NotNull(confirmedUrl);
        Assert.Contains("confirm=abc_token", confirmedUrl);
    }

    /// <summary>
    /// Verifies that form inputs with HTML-encoded entities in name or value are decoded before URL encoding.
    /// </summary>
    [Fact]
    public void TryExtractGoogleDriveConfirmationUrl_WithHtmlEncodedFormInput_DecodesBeforeUrlEncoding()
    {
        const string html = "<html><body><form id=\"download-form\" method=\"post\" action=\"https://drive.usercontent.google.com/download?id=12345\"><input type=\"hidden\" value=\"tok&amp;val\" name=\"confirm\" /></form></body></html>";
        var requestUri = new Uri("https://drive.google.com/uc?export=download&id=12345");

        var success = CloudUrlHelper.TryExtractGoogleDriveConfirmationUrl(html, requestUri, out var confirmedUrl);

        Assert.True(success);
        Assert.NotNull(confirmedUrl);
        Assert.Contains("confirm=tok%26val", confirmedUrl);
        Assert.DoesNotContain("amp", confirmedUrl);
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
    /// Verifies that a relative form action is resolved using the request base URI and validated.
    /// </summary>
    [Fact]
    public void TryExtractGoogleDriveConfirmationUrl_WithRelativeFormAction_ResolvesAbsoluteUri()
    {
        const string html = "<html><body><form action=\"/uc?export=download&amp;id=12345\" method=\"post\"><input name=\"confirm\" value=\"tok123\" /></form></body></html>";
        var requestUri = new Uri("https://drive.google.com/uc?export=download&id=12345");

        var success = CloudUrlHelper.TryExtractGoogleDriveConfirmationUrl(html, requestUri, out var confirmedUrl);

        Assert.True(success);
        Assert.NotNull(confirmedUrl);
        Assert.StartsWith("https://drive.google.com/uc?", confirmedUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirm=tok123", confirmedUrl);
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

    /// <summary>
    /// Verifies that Google Drive download URLs are rewritten to the pre-confirmed direct download form.
    /// </summary>
    /// <param name="url">The input download URL.</param>
    /// <param name="expected">The expected confirmed URL.</param>
    [Theory]
    [InlineData(
        "https://drive.google.com/uc?export=download&id=abc_123",
        "https://drive.usercontent.google.com/download?id=abc_123&export=download&confirm=t")]
    [InlineData(
        "https://docs.google.com/uc?id=abc-123&export=download",
        "https://drive.usercontent.google.com/download?id=abc-123&export=download&confirm=t")]
    [InlineData(
        "https://drive.usercontent.google.com/download?id=abc&export=download",
        "https://drive.usercontent.google.com/download?id=abc&export=download&confirm=t")]
    [InlineData(
        "https://drive.google.com/uc?export=download&id=abc&resourcekey=0-key",
        "https://drive.usercontent.google.com/download?id=abc&export=download&confirm=t&resourcekey=0-key")]
    [InlineData(
        "https://drive.google.com/uc?export=download&id=abc&resourcekey=0-key&uuid=xyz&authuser=0",
        "https://drive.usercontent.google.com/download?id=abc&export=download&confirm=t&resourcekey=0-key&uuid=xyz&authuser=0")]
    public void ToConfirmedGoogleDriveDownloadUri_GoogleDriveDownload_ReturnsConfirmedUri(string url, string expected)
    {
        var result = CloudUrlHelper.ToConfirmedGoogleDriveDownloadUri(new Uri(url));

        Assert.Equal(expected, result.AbsoluteUri);
    }

    /// <summary>
    /// Verifies that non-Drive URLs, already-confirmed URLs, and URLs without a file id are left unchanged.
    /// </summary>
    /// <param name="url">The input URL.</param>
    [Theory]
    [InlineData("https://example.com/uc?export=download&id=abc")]
    [InlineData("https://drive.usercontent.google.com/download?id=abc&export=download&confirm=t&uuid=x")]
    [InlineData("https://drive.google.com/uc?export=download&confirm=tok&id=abc")]
    [InlineData("https://drive.google.com/uc?export=download")]
    [InlineData("https://drive.google.com/uc?export=download&id=")]
    [InlineData("https://drive.google.com/uc?id=abc")]
    [InlineData("https://docs.google.com/uc?id=abc")]
    [InlineData("https://drive.google.com/file/d/abc/view")]
    [InlineData("https://drive.google.com/drive/folders/abc")]
    [InlineData("http://drive.google.com/uc?export=download&id=abc")]
    [InlineData("https://evil-drive.google.com.example.com/uc?id=abc")]
    public void ToConfirmedGoogleDriveDownloadUri_NotApplicable_ReturnsSameUri(string url)
    {
        var uri = new Uri(url);

        var result = CloudUrlHelper.ToConfirmedGoogleDriveDownloadUri(uri);

        Assert.Same(uri, result);
    }
}
