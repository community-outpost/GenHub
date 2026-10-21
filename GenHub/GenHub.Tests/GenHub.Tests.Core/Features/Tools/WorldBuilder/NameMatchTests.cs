// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using FluentAssertions;
using GenHub.Features.Tools.WorldBuilder.Common;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="NameMatch"/> against the golden values documented in
/// qt/WBQtNameMatch.h.
/// </summary>
public sealed class NameMatchTests
{
    /// <summary>
    /// Verifies the documented raw similarities: four inserted characters score
    /// 0.7647, five score 0.6875, and the spliced variant scores 0.7333.
    /// </summary>
    /// <param name="target">The missing name.</param>
    /// <param name="candidate">The existing name.</param>
    /// <param name="expected">The expected similarity.</param>
    [Theory]
    [InlineData("GLAArmsDealer", "AsltGLAArmsDealer", 0.7647f)]
    [InlineData("GLABarracks", "Aslt_GLABarracks", 0.6875f)]
    [InlineData("GLABarracks", "GLAHoleBarracks", 0.7333f)]
    [InlineData("GLAPalace", "Slth_GLAPalace", 0.6429f)]
    public void Similarity_DocumentedPairs_MatchesGoldens(string target, string candidate, float expected)
    {
        NameMatch.Similarity(target, candidate).Should().BeApproximately(expected, 0.0001f);
    }

    /// <summary>
    /// Verifies the boundary conditions of the similarity metric.
    /// </summary>
    [Fact]
    public void Similarity_Boundaries_Behaves()
    {
        NameMatch.Similarity(string.Empty, string.Empty).Should().Be(1.0f);
        NameMatch.Similarity(string.Empty, "Tank").Should().Be(0.0f);
        NameMatch.Similarity("Tank", string.Empty).Should().Be(0.0f);
        NameMatch.Similarity("Tank", "tank").Should().Be(1.0f);
        NameMatch.Similarity("Tank", "Tank").Should().Be(1.0f);
    }

    /// <summary>
    /// Verifies the contains boost closes half the gap to 1.0 and never applies
    /// to equal names.
    /// </summary>
    [Fact]
    public void MatchScore_ContainsBoost_ClosesHalfGap()
    {
        NameMatch.MatchScore("GLABarracks", "Aslt_GLABarracks").Should().BeApproximately(0.8438f, 0.0001f);
        NameMatch.MatchScore("GLAPalace", "Slth_GLAPalace").Should().BeApproximately(0.8214f, 0.0001f);
        NameMatch.MatchScore("Tank", "Tank").Should().Be(1.0f);
    }

    /// <summary>
    /// Verifies admission is on the boosted score: the prefixed variants beat
    /// the spliced variant for GLABarracks despite a lower raw similarity.
    /// </summary>
    [Fact]
    public void BestMatch_PrefixedBeatsSpliced_AdmitsOnBoostedScore()
    {
        // Arrange
        var candidates = new List<string> { "GLAHoleBarracks", "Aslt_GLABarracks", "Slth_GLABarracks" };

        // Act
        var best = NameMatch.BestMatch(candidates, "GLABarracks");

        // Assert
        best.Should().Be("Aslt_GLABarracks");
    }

    /// <summary>
    /// Verifies nothing clears the bar when every candidate is distant.
    /// </summary>
    [Fact]
    public void BestMatch_NothingClose_ReturnsNull()
    {
        NameMatch.BestMatch(["Fish", "Chips"], "GLAPalace").Should().BeNull();
    }

    /// <summary>
    /// Verifies ranking is best-first with tree order kept for ties.
    /// </summary>
    [Fact]
    public void RankMatches_Ties_KeepTreeOrder()
    {
        // Arrange
        var names = new List<string> { "GLAHoleBarracks", "Aslt_GLABarracks", "Slth_GLABarracks", "Fish" };

        // Act
        var ranked = NameMatch.RankMatches(names, "GLABarracks");

        // Assert
        ranked.Should().ContainInOrder("Aslt_GLABarracks", "Slth_GLABarracks", "GLAHoleBarracks");
        ranked.Should().NotContain("Fish");
    }

    /// <summary>
    /// Verifies the cursor wraps around in both directions.
    /// </summary>
    [Fact]
    public void MatchCursor_Step_WrapsAround()
    {
        // Arrange
        var cursor = new NameMatch.MatchCursor();
        cursor.Reset(["A", "B", "C"]);

        // Act & Assert
        cursor.Current.Should().Be("A");
        cursor.Step(1).Should().Be("B");
        cursor.Step(1).Should().Be("C");
        cursor.Step(1).Should().Be("A");
        cursor.Step(-1).Should().Be("C");
    }

    /// <summary>
    /// Verifies the arming sequence selects the best match and reports content.
    /// </summary>
    [Fact]
    public void ArmMatchCursor_WithMatches_SelectsBest()
    {
        // Arrange
        var cursor = new NameMatch.MatchCursor();
        string? selected = null;

        // Act
        var armed = NameMatch.ArmMatchCursor(cursor, ["GLAHoleBarracks", "Aslt_GLABarracks"], "GLABarracks", name => selected = name);

        // Assert
        armed.Should().BeTrue();
        selected.Should().Be("Aslt_GLABarracks");
        cursor.Size.Should().Be(2);
    }

    /// <summary>
    /// Verifies names are tracked by value for post-rebuild reselection.
    /// </summary>
    [Fact]
    public void IndexOfName_PresentAndAbsent_ReportsIndex()
    {
        NameMatch.IndexOfName(["A", "B"], "B").Should().Be(1);
        NameMatch.IndexOfName(["A", "B"], "Ghost").Should().Be(-1);
    }
}
