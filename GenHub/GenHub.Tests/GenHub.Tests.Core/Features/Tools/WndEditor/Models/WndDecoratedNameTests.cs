using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndDecoratedName"/>.
/// </summary>
public sealed class WndDecoratedNameTests
{
    /// <summary>
    /// Tests that decorated names split into file and window portions.
    /// </summary>
    [Fact]
    public void Parse_DecoratedValue_SplitsPortions()
    {
        // Act
        var name = WndDecoratedName.Parse("\"ChallengeLoadScreen.wnd:VersusBackdrop\"");

        // Assert
        name.FileName.Should().Be("ChallengeLoadScreen.wnd");
        name.ShortName.Should().Be("VersusBackdrop");
    }

    /// <summary>
    /// Tests that undecorated names keep a null file portion.
    /// </summary>
    [Fact]
    public void Parse_UndecoratedValue_KeepsNullFile()
    {
        // Act
        var name = WndDecoratedName.Parse("\"VersusBackdrop\"");

        // Assert
        name.FileName.Should().BeNull();
        name.ShortName.Should().Be("VersusBackdrop");
    }

    /// <summary>
    /// Tests that empty values produce an empty short name.
    /// </summary>
    /// <param name="value">The value under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_EmptyValue_ProducesEmptyShortName(string? value)
    {
        // Act
        var name = WndDecoratedName.Parse(value);

        // Assert
        name.ShortName.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that the quoted decorated form round-trips.
    /// </summary>
    [Fact]
    public void ToString_ReturnsQuotedDecoratedForm()
    {
        // Act
        var text = new WndDecoratedName("ChallengeLoadScreen.wnd", "VersusBackdrop").ToString();

        // Assert
        text.Should().Be("\"ChallengeLoadScreen.wnd:VersusBackdrop\"");
    }
}
