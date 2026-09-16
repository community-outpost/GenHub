using FluentAssertions;
using GenHub.Core.Constants;
using System;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Unit tests for <see cref="ModDBConstants"/>.
/// </summary>
public class ModDBConstantsTests
{
    /// <summary>
    /// Verifies that domain constants have expected values.
    /// </summary>
    [Fact]
    public void DomainConstants_ShouldHaveExpectedValues()
    {
        ModDBConstants.Domain.Should().Be("moddb.com");
        ModDBConstants.DBolicalDomain.Should().Be("dbolical.com");
    }

    /// <summary>
    /// Verifies that <see cref="ModDBConstants.IsModDbOrDbolicalHost"/> returns true for valid ModDB and DBolical hosts.
    /// </summary>
    /// <param name="host">The host name to test.</param>
    [Theory]
    [InlineData("moddb.com")]
    [InlineData("www.moddb.com")]
    [InlineData("media.moddb.com")]
    [InlineData("files.moddb.com")]
    [InlineData("MODDB.COM")]
    [InlineData("WWW.MODDB.COM")]
    [InlineData("dbolical.com")]
    [InlineData("dl.dbolical.com")]
    [InlineData("fmt1.dl.dbolical.com")]
    [InlineData("iad1.dl.dbolical.com")]
    [InlineData("ams1.dl.dbolical.com")]
    [InlineData("sjc1.dl.dbolical.com")]
    [InlineData("DBOLICAL.COM")]
    [InlineData("FMT1.DL.DBOLICAL.COM")]
    public void IsModDbOrDbolicalHost_WithValidHosts_ShouldReturnTrue(string host)
    {
        ModDBConstants.IsModDbOrDbolicalHost(host).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <see cref="ModDBConstants.IsModDbOrDbolicalHost"/> returns false for invalid, spoofed, or empty hosts.
    /// </summary>
    /// <param name="host">The host name to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notmoddb.com")]
    [InlineData("evil-moddb.com")]
    [InlineData("evilmoddb.com")]
    [InlineData("moddb.com.evil.com")]
    [InlineData("evil-dbolical.com")]
    [InlineData("evildbolical.com")]
    [InlineData("dbolical.com.attacker.com")]
    [InlineData("google.com")]
    [InlineData("github.com")]
    public void IsModDbOrDbolicalHost_WithInvalidHosts_ShouldReturnFalse(string? host)
    {
        ModDBConstants.IsModDbOrDbolicalHost(host).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <see cref="ModDBConstants.IsModDbOrDbolicalUri"/> returns true for valid HTTP/HTTPS URIs.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    [Theory]
    [InlineData("https://www.moddb.com/downloads/start/12345")]
    [InlineData("http://media.moddb.com/images/screenshot.jpg")]
    [InlineData("https://fmt1.dl.dbolical.com/dl/2026/08/01/GeneralsUndone_v1.0.zip?st=abc&e=123")]
    [InlineData("https://ams1.dl.dbolical.com/dl/file.zip")]
    [InlineData("http://dbolical.com")]
    public void IsModDbOrDbolicalUri_WithValidUris_ShouldReturnTrue(string url)
    {
        var uri = new Uri(url);
        ModDBConstants.IsModDbOrDbolicalUri(uri).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <see cref="ModDBConstants.IsModDbOrDbolicalUri"/> returns false for non-HTTP(S) or invalid host URIs.
    /// </summary>
    [Fact]
    public void IsModDbOrDbolicalUri_WithNull_ShouldReturnFalse()
    {
        ModDBConstants.IsModDbOrDbolicalUri(null).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <see cref="ModDBConstants.IsModDbOrDbolicalUri"/> returns false for unsupported schemes and spoofed hosts.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    [Theory]
    [InlineData("ftp://fmt1.dl.dbolical.com/file.zip")]
    [InlineData("file:///C:/downloads/mod.zip")]
    [InlineData("https://evil-moddb.com/downloads/start/123")]
    [InlineData("https://evil-dbolical.com/dl/file.zip")]
    [InlineData("https://github.com/GenHub/GenHub")]
    public void IsModDbOrDbolicalUri_WithInvalidSchemeOrHost_ShouldReturnFalse(string url)
    {
        var uri = new Uri(url);
        ModDBConstants.IsModDbOrDbolicalUri(uri).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <see cref="ModDBConstants.IsChallengePageTitle"/> returns true for challenge page titles and false otherwise.
    /// </summary>
    /// <param name="title">The page title to test.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [InlineData("Just a moment...", true)]
    [InlineData("Attention Required! | Cloudflare", true)]
    [InlineData("Checking your browser before accessing moddb.com", true)]
    [InlineData("Please wait... Cloudflare Ray ID", true)]
    [InlineData("C&C Generals: Zero Hour - Mod DB", false)]
    [InlineData("Downloads - Shockwave mod for C&C Generals Zero Hour", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsChallengePageTitle_WithVariousTitles_ShouldReturnExpectedResult(string? title, bool expected)
    {
        ModDBConstants.IsChallengePageTitle(title).Should().Be(expected);
    }
}
