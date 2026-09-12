using System;
using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="CommandLineParser"/>.
/// </summary>
public sealed class CommandLineParserTests
{
    /// <summary>
    /// Verifies that ExtractProfileId correctly extracts profile id from spaced argument.
    /// </summary>
    [Fact]
    public void ExtractProfileId_WithSpacedArgument_ReturnsProfileId()
    {
        var args = new[] { "--other", "value", "--launch-profile", "test-profile-123" };

        var result = CommandLineParser.ExtractProfileId(args);

        Assert.Equal("test-profile-123", result);
    }

    /// <summary>
    /// Verifies that ExtractProfileId correctly extracts profile id from inline argument.
    /// </summary>
    [Fact]
    public void ExtractProfileId_WithInlineArgument_ReturnsProfileId()
    {
        var args = new[] { "--launch-profile=test-profile-456" };

        var result = CommandLineParser.ExtractProfileId(args);

        Assert.Equal("test-profile-456", result);
    }

    /// <summary>
    /// Verifies that ExtractProfileId trims surrounding quotes.
    /// </summary>
    [Fact]
    public void ExtractProfileId_WithQuotedValues_ReturnsTrimmedProfileId()
    {
        var argsSpaced = new[] { "--launch-profile", "\"quoted-profile\"" };
        var argsInline = new[] { "--launch-profile=\"quoted-profile\"" };

        Assert.Equal("quoted-profile", CommandLineParser.ExtractProfileId(argsSpaced));
        Assert.Equal("quoted-profile", CommandLineParser.ExtractProfileId(argsInline));
    }

    /// <summary>
    /// Verifies that ExtractProfileId returns null when launch profile argument is absent.
    /// </summary>
    [Fact]
    public void ExtractProfileId_WhenMissing_ReturnsNull()
    {
        var args = new[] { "--verbose", "--other" };

        var result = CommandLineParser.ExtractProfileId(args);

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that ExtractProfileId returns null when spaced argument has no subsequent value.
    /// </summary>
    [Fact]
    public void ExtractProfileId_WhenFlagAtEndWithoutValue_ReturnsNull()
    {
        var args = new[] { "--launch-profile" };

        var result = CommandLineParser.ExtractProfileId(args);

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl parses direct catalog URLs.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_WithDirectUrl_ReturnsDecodedUrl()
    {
        var args = new[] { "genhub://subscribe?url=https://example.com/catalog.json" };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Equal("https://example.com/catalog.json", result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl correctly decodes URL encoded parameters.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_WithUrlEncodedParameter_ReturnsDecodedUrl()
    {
        var args = new[] { "genhub://subscribe?url=https%3A%2F%2Fexample.com%2Fcatalog.json%3Fversion%3D1" };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Equal("https://example.com/catalog.json?version=1", result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl trims quotes around the url value.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_WithQuotedArgument_ReturnsTrimmedUrl()
    {
        var argsClean = new[] { "genhub://subscribe?url=\"https://example.com/catalog.json\"" };

        Assert.Equal("https://example.com/catalog.json", CommandLineParser.ExtractSubscriptionUrl(argsClean));
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl returns null when no subscribe URI is present.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_WhenNotPresent_ReturnsNull()
    {
        var args = new[] { "--launch-profile", "test" };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl is case insensitive with protocol prefix and query parameter.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_CaseInsensitivePrefix_ReturnsUrl()
    {
        var args = new[] { "GENHUB://SUBSCRIBE?URL=https://example.com/catalog.json" };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Equal("https://example.com/catalog.json", result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl returns null when subscribe URI lacks the url query parameter.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_WithoutUrlParameter_ReturnsNull()
    {
        var args = new[] { "genhub://subscribe" };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl returns null when the url query parameter is empty.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_WithEmptyUrlParameter_ReturnsNull()
    {
        var args = new[] { "genhub://subscribe?url=" };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl extracts the URL even when preceded by other arguments.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_WhenNotFirstArgument_ReturnsUrl()
    {
        var args = new[] { "--verbose", "--launch-profile", "test-profile", "genhub://subscribe?url=https://example.com/catalog.json" };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Equal("https://example.com/catalog.json", result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl returns the first matching subscription URL when multiple are present.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_MultipleUrls_ReturnsFirstMatch()
    {
        var args = new[]
        {
            "genhub://subscribe?url=https://example.com/first.json",
            "genhub://subscribe?url=https://example.com/second.json",
        };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Equal("https://example.com/first.json", result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl returns null for non-HTTP and non-HTTPS URI schemes.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_NonHttpOrHttpsScheme_ReturnsNull()
    {
        var fileSchemeArgs = new[] { "genhub://subscribe?url=file:///C:/malicious.exe" };
        var jsSchemeArgs = new[] { "genhub://subscribe?url=javascript:alert(1)" };

        Assert.Null(CommandLineParser.ExtractSubscriptionUrl(fileSchemeArgs));
        Assert.Null(CommandLineParser.ExtractSubscriptionUrl(jsSchemeArgs));
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl strips newlines and control characters from the URL.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_WithNewlinesAndControlChars_ReturnsSanitizedUrl()
    {
        var args = new[] { "genhub://subscribe?url=https%3A%2F%2Fexample.com%2Fcatalog.json%0D%0A" };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Equal("https://example.com/catalog.json", result);
    }

    /// <summary>
    /// Verifies that ExtractSubscriptionUrl returns null for non-command subscribe-prefixed URIs.
    /// </summary>
    [Fact]
    public void ExtractSubscriptionUrl_WithNonCommandSubscribePrefixedUri_ReturnsNull()
    {
        var args = new[] { "genhub://subscribe-anything?url=https://example.com/catalog.json" };

        var result = CommandLineParser.ExtractSubscriptionUrl(args);

        Assert.Null(result);
    }
}
