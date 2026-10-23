using GenHub.Core.Constants;
using GenHub.Features.Content.Services.Catalog;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests generating custom tab IDs from headers in <see cref="CustomTabIdGenerator"/>.
/// </summary>
public sealed class CustomTabIdGeneratorTests
{
    /// <summary>
    /// Latin headers become lowercase hyphenated slugs with accents folded.
    /// </summary>
    /// <param name="header">The tab header.</param>
    /// <param name="expected">The expected slug.</param>
    [Theory]
    [InlineData("How to Play", "how-to-play")]
    [InlineData("  Release Notes: v2.0!  ", "release-notes-v2-0")]
    [InlineData("Café Déjà Vu", "cafe-deja-vu")]
    [InlineData("FAQ", "faq")]
    [InlineData("2024", "2024")]
    [InlineData("Level 2 Как", "level-2")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Slugify_LatinHeader_ReturnsSlug(string header, string expected)
    {
        Assert.Equal(expected, CustomTabIdGenerator.Slugify(header));
    }

    /// <summary>
    /// Headers with no ASCII letters or digits, such as punctuation, Arabic or Russian text, get a stable fallback ID.
    /// </summary>
    /// <param name="header">The tab header.</param>
    [Theory]
    [InlineData("!!! ???")]
    [InlineData("كيف تلعب")]
    [InlineData("Как играть")]
    [InlineData("Как играть 2")]
    [InlineData("الجزء 3")]
    [InlineData("𐌰𐌱 2")]
    public void Slugify_HeaderWithoutAsciiLetters_ReturnsStableFallback(string header)
    {
        var slug = CustomTabIdGenerator.Slugify(header);

        Assert.Matches("^tab-[0-9a-f]{8}$", slug);
        Assert.Equal(slug, CustomTabIdGenerator.Slugify(header));
        Assert.NotEqual(slug, CustomTabIdGenerator.Slugify(header + " 2"));
    }

    /// <summary>
    /// Generated IDs stay within the ID length limit, including the uniqueness suffix.
    /// </summary>
    [Fact]
    public void Suggest_LongDuplicateHeaders_StayWithinLimit()
    {
        var header = string.Join(" ", Enumerable.Repeat("Very long header words", 10));

        var suggestions = CustomTabIdGenerator.Suggest([(null, header), (null, header), (null, header)]);

        Assert.All(suggestions, s => Assert.True(s!.Length <= CustomTabConstants.MaxTabIdLength, s));
        Assert.Equal(3, suggestions.Distinct().Count());
        Assert.EndsWith("-2", suggestions[1]);
        Assert.EndsWith("-3", suggestions[2]);
    }

    /// <summary>
    /// Declared IDs win and are never reused by a generated ID, compared case-insensitively.
    /// </summary>
    [Fact]
    public void Suggest_DeclaredIds_WinAndStayUnique()
    {
        var suggestions = CustomTabIdGenerator.Suggest(
        [
            ("How-To-Play", "Rules"),
            (null, "How to Play"),
            (" ", "How to Play"),
            (null, " "),
        ]);

        Assert.Equal([null, "how-to-play-2", "how-to-play-3", null], suggestions);
    }
}
