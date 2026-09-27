using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndStatusValue"/>.
/// </summary>
public sealed class WndStatusValueTests
{
    /// <summary>
    /// Tests that status flags parse case-insensitively.
    /// </summary>
    [Fact]
    public void ParseStatus_ValidValue_ParsesFlags()
    {
        // Act
        var value = WndStatusValue.ParseStatus("ENABLED+IMAGE");

        // Assert
        value.Flags.Should().BeEquivalentTo(["ENABLED", "IMAGE"]);
        value.UnknownTokens.Should().BeEmpty();
        value.HasAny.Should().BeTrue();
    }

    /// <summary>
    /// Tests that NULL and empty values parse to no flags.
    /// </summary>
    /// <param name="value">The value under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NULL")]
    [InlineData("NONE")]
    public void ParseStatus_EmptyValue_HasNoFlags(string? value)
    {
        // Act
        var parsed = WndStatusValue.ParseStatus(value);

        // Assert
        parsed.HasAny.Should().BeFalse();
    }

    /// <summary>
    /// Tests that unrecognized tokens are preserved.
    /// </summary>
    [Fact]
    public void ParseStatus_UnknownToken_PreservesToken()
    {
        // Act
        var value = WndStatusValue.ParseStatus("ENABLED+FROBNICATE");

        // Assert
        value.Flags.Should().BeEquivalentTo(["ENABLED"]);
        value.UnknownTokens.Should().Equal("FROBNICATE");
    }

    /// <summary>
    /// Tests that flags emit in canonical engine order with unknowns appended.
    /// </summary>
    [Fact]
    public void ToString_EmitsCanonicalOrder()
    {
        // Arrange
        var value = WndStatusValue.ParseStatus("IMAGE+FROBNICATE+ENABLED");

        // Act
        var text = value.ToString(WndConstants.StatusFlags.All);

        // Assert
        text.Should().Be("ENABLED+IMAGE+FROBNICATE");
    }

    /// <summary>
    /// Tests that an empty value emits NULL.
    /// </summary>
    [Fact]
    public void ToString_EmptyValue_EmitsNull()
    {
        // Act
        var text = WndStatusValue.ParseStatus(null).ToString(WndConstants.StatusFlags.All);

        // Assert
        text.Should().Be("NULL");
    }

    /// <summary>
    /// Tests that style values parse against the style table.
    /// </summary>
    [Fact]
    public void ParseStyle_ValidValue_ParsesFlags()
    {
        // Act
        var value = WndStatusValue.ParseStyle("STATICTEXT+MOUSETRACK");

        // Assert
        value.Flags.Should().BeEquivalentTo(["STATICTEXT", "MOUSETRACK"]);
        value.UnknownTokens.Should().BeEmpty();
    }

    /// <summary>
    /// Tests that status flags rejected by the style table are reported unknown.
    /// </summary>
    [Fact]
    public void ParseStyle_StatusFlag_ReportsUnknown()
    {
        // Act
        var value = WndStatusValue.ParseStyle("ENABLED");

        // Assert
        value.Flags.Should().BeEmpty();
        value.UnknownTokens.Should().Equal("ENABLED");
    }
}
