using GenHub.Core.Helpers;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="CommandLineHelper"/>.
/// </summary>
public sealed class CommandLineHelperTests
{
    /// <summary>
    /// Verifies that plain arguments without special characters are returned unchanged.
    /// </summary>
    [Fact]
    public void QuoteArgument_WithoutWhitespaceOrQuotes_ReturnsOriginal()
    {
        Assert.Equal("simple", CommandLineHelper.QuoteArgument("simple"));
        Assert.Equal("-quickstart", CommandLineHelper.QuoteArgument("-quickstart"));
    }

    /// <summary>
    /// Verifies that arguments containing spaces or tabs are enclosed in double quotes.
    /// </summary>
    /// <param name="input">The input argument string.</param>
    /// <param name="expected">The expected quoted argument string.</param>
    [Theory]
    [InlineData("has space", "\"has space\"")]
    [InlineData("has\ttab", "\"has\ttab\"")]
    public void QuoteArgument_WithWhitespace_QuotesArgument(string input, string expected)
    {
        Assert.Equal(expected, CommandLineHelper.QuoteArgument(input));
    }

    /// <summary>
    /// Verifies that arguments containing double quotes have quotes escaped and are enclosed in quotes.
    /// </summary>
    /// <param name="input">The input argument string.</param>
    /// <param name="expected">The expected quoted and escaped argument string.</param>
    [Theory]
    [InlineData("say \"hello\"", "\"say \\\"hello\\\"\"")]
    [InlineData("nested\"quote", "\"nested\\\"quote\"")]
    public void QuoteArgument_WithQuotes_EscapesAndQuotesArgument(string input, string expected)
    {
        Assert.Equal(expected, CommandLineHelper.QuoteArgument(input));
    }

    /// <summary>
    /// Verifies that .exe paths are identified as Windows executables regardless of case.
    /// </summary>
    /// <param name="path">The executable path to test.</param>
    /// <param name="expected">Whether the path is expected to be a Windows executable.</param>
    [Theory]
    [InlineData("game.exe", true)]
    [InlineData("C:\\Games\\Generals.EXE", true)]
    [InlineData("/path/to/game.Exe", true)]
    [InlineData("generalszh", false)]
    [InlineData("run.sh", false)]
    [InlineData("script.bat", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsWindowsExecutable_IdentifiesCorrectly(string? path, bool expected)
    {
        Assert.Equal(expected, CommandLineHelper.IsWindowsExecutable(path));
    }
}
