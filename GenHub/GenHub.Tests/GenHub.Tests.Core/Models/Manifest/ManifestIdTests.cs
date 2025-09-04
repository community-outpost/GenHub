using System;
using GenHub.Core.Models.Manifest;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Unit tests for ManifestId struct to ensure proper validation, equality, and conversion.
/// </summary>
public class ManifestIdTests
{
    /// <summary>
    /// Tests that ManifestId.Create accepts valid manifest ID strings.
    /// </summary>
    /// <param name="validId">A valid manifest ID string.</param>
    [Theory]
    [InlineData("publisher.content.version")]
    [InlineData("ea.generals.1.0")]
    [InlineData("steam.generals.1.04")]
    [InlineData("testid.content.1.0")]
    [InlineData("simple.id")]
    [InlineData("complexpublisher.content.name.1.0")]
    public void Create_WithValidManifestIdStrings_CreatesManifestId(string validId)
    {
        // Act
        var manifestId = ManifestId.Create(validId);

        // Assert
        Assert.Equal(validId, manifestId.Value);
    }

    /// <summary>
    /// Tests that ManifestId.Create throws ArgumentException for invalid manifest ID strings.
    /// </summary>
    /// <param name="invalidId">An invalid manifest ID string.</param>
    /// <param name="expectedError">Expected error message substring.</param>
    [Theory]
    [InlineData("", "cannot be null or empty")]
    [InlineData("   ", "cannot be null or empty")]
    [InlineData("special@chars", "invalid. Must follow either")]
    [InlineData("spaces in id", "invalid. Must follow either")]
    public void Create_WithInvalidManifestIdStrings_ThrowsArgumentException(string invalidId, string expectedError)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => ManifestId.Create(invalidId));
        Assert.Contains(expectedError, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests implicit conversion from string to ManifestId.
    /// </summary>
    [Fact]
    public void ImplicitConversion_FromStringToManifestId_Works()
    {
        // Arrange
        string idString = "testpublisher.content.1.0";

        // Act
        ManifestId manifestId = idString;

        // Assert
        Assert.Equal(idString, manifestId.Value);
    }

    /// <summary>
    /// Tests implicit conversion from ManifestId to string.
    /// </summary>
    [Fact]
    public void ImplicitConversion_FromManifestIdToString_Works()
    {
        // Arrange
        var manifestId = ManifestId.Create("testpublisher.content.1.0");

        // Act
        string idString = manifestId;

        // Assert
        Assert.Equal("testpublisher.content.1.0", idString);
    }

    /// <summary>
    /// Tests equality operator for ManifestId.
    /// </summary>
    [Fact]
    public void EqualityOperator_WithEqualManifestIds_ReturnsTrue()
    {
        // Arrange
        var id1 = ManifestId.Create("testpublisher.content.1.0");
        var id2 = ManifestId.Create("testpublisher.content.1.0");

        // Act & Assert
        Assert.True(id1 == id2);
        Assert.False(id1 != id2);
    }

    /// <summary>
    /// Tests equality operator for different ManifestIds.
    /// </summary>
    [Fact]
    public void EqualityOperator_WithDifferentManifestIds_ReturnsFalse()
    {
        // Arrange
        var id1 = ManifestId.Create("testpublisher.content.1.0");
        var id2 = ManifestId.Create("differentpublisher.content.1.0");

        // Act & Assert
        Assert.False(id1 == id2);
        Assert.True(id1 != id2);
    }

    /// <summary>
    /// Tests case-insensitive equality.
    /// </summary>
    [Fact]
    public void Equality_IsCaseInsensitive()
    {
        // Arrange
        var id1 = ManifestId.Create("testpublisher.content.1.0");
        var id2 = ManifestId.Create("TESTPUBLISHER.CONTENT.1.0");

        // Act & Assert
        Assert.True(id1 == id2);
        Assert.True(id1.Equals(id2));
    }

    /// <summary>
    /// Tests Equals method with ManifestId.
    /// </summary>
    [Fact]
    public void Equals_WithManifestId_Works()
    {
        // Arrange
        var id1 = ManifestId.Create("testpublisher.content.1.0");
        var id2 = ManifestId.Create("testpublisher.content.1.0");
        var id3 = ManifestId.Create("differentpublisher.content.1.0");

        // Act & Assert
        Assert.True(id1.Equals(id2));
        Assert.False(id1.Equals(id3));
    }

    /// <summary>
    /// Tests Equals method with object.
    /// </summary>
    [Fact]
    public void Equals_WithObject_Works()
    {
        // Arrange
        var id1 = ManifestId.Create("testpublisher.content.1.0");
        var id2 = ManifestId.Create("testpublisher.content.1.0");
        var differentObject = new object();

        // Act & Assert
        Assert.True(id1.Equals((object)id2));
        Assert.False(id1.Equals(differentObject));
        Assert.False(id1.Equals((object?)null));
    }

    /// <summary>
    /// Tests GetHashCode consistency.
    /// </summary>
    [Fact]
    public void GetHashCode_IsConsistent()
    {
        // Arrange
        var id1 = ManifestId.Create("testpublisher.content.1.0");
        var id2 = ManifestId.Create("testpublisher.content.1.0");

        // Act & Assert
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    /// <summary>
    /// Tests GetHashCode is case-insensitive.
    /// </summary>
    [Fact]
    public void GetHashCode_IsCaseInsensitive()
    {
        // Arrange
        var id1 = ManifestId.Create("testpublisher.content.1.0");
        var id2 = ManifestId.Create("TESTPUBLISHER.CONTENT.1.0");

        // Act & Assert
        Assert.Equal(id1.GetHashCode(), id2.GetHashCode());
    }

    /// <summary>
    /// Tests ToString method.
    /// </summary>
    [Fact]
    public void ToString_ReturnsValue()
    {
        // Arrange
        var id = ManifestId.Create("testpublisher.content.1.0");

        // Act
        var result = id.ToString();

        // Assert
        Assert.Equal("testpublisher.content.1.0", result);
    }

    /// <summary>
    /// Tests that ManifestId is a readonly struct.
    /// </summary>
    [Fact]
    public void ManifestId_IsReadonlyStruct()
    {
        // Arrange
        var id = ManifestId.Create("testpublisher.content.1.0");

        // Act & Assert - Should not be able to modify (readonly struct)
        Assert.Equal("testpublisher.content.1.0", id.Value);
    }

    /// <summary>
    /// Tests ManifestId with various valid base game ID formats.
    /// </summary>
    /// <param name="baseGameId">A valid base game ID string.</param>
    [Theory]
    [InlineData("steam.generals.1.0")]
    [InlineData("eaapp.zerohour.1.04")]
    [InlineData("origin.generals.2.0")]
    [InlineData("thefirstdecade.zerohour.1.04")]
    [InlineData("rgmechanics.generals.1.0")]
    [InlineData("cdiso.zerohour.1.0")]
    [InlineData("wine.generals.1.0")]
    [InlineData("retail.zerohour.1.0")]
    [InlineData("unknown.generals.1.0")]
    [InlineData("steam.generals")] // 2-segment base game ID
    [InlineData("origin.generals")] // 2-segment base game ID
    [InlineData("steam.zerohour")] // 2-segment base game ID
    public void Create_WithValidBaseGameIds_CreatesManifestId(string baseGameId)
    {
        // Act
        var manifestId = ManifestId.Create(baseGameId);

        // Assert
        Assert.Equal(baseGameId, manifestId.Value);
    }

    /// <summary>
    /// Tests ManifestId with invalid base game ID formats.
    /// </summary>
    /// <param name="invalidBaseGameId">An invalid base game ID string.</param>
    [Theory]
    [InlineData("steam.generals.1.0.extra")] // Too many segments
    [InlineData("invalid.generals.1.0")] // Invalid installation type
    [InlineData("steam.invalid.1.0")] // Invalid game type
    [InlineData("invalid.generals")] // Invalid installation type (2-segment)
    [InlineData("steam.invalid")] // Invalid game type (2-segment)
    public void Create_WithInvalidBaseGameIds_ThrowsArgumentException(string invalidBaseGameId)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => ManifestId.Create(invalidBaseGameId));
        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that ManifestId handles edge cases properly.
    /// </summary>
    [Fact]
    public void Create_HandlesEdgeCases()
    {
        // Test with minimum valid ID
        var minimalId = ManifestId.Create("a.b");
        Assert.Equal("a.b", minimalId.Value);

        // Test with maximum reasonable segments
        var complexId = ManifestId.Create("publishercontent.name.version.patch");
        Assert.Equal("publishercontent.name.version.patch", complexId.Value);
    }

    /// <summary>
    /// Tests that ManifestId preserves exact string value.
    /// </summary>
    [Fact]
    public void Value_PreservesExactString()
    {
        // Arrange
        const string originalId = "testpublisher.content.1.0.0.beta";

        // Act
        var manifestId = ManifestId.Create(originalId);

        // Assert
        Assert.Equal(originalId, manifestId.Value);
        Assert.Equal(originalId, (string)manifestId);
    }
}
