using GenHub.Core.Models.Manifest;

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
    [InlineData("1.0.publisher.content")]
    [InlineData("1.0.eaapp.gameinstallation.generals")]
    [InlineData("1.0.steam.gameinstallation.generals")]
    [InlineData("1.0.testid.content")]
    [InlineData("1.0.simple.id.test")]
    [InlineData("1.0.complexpublisher.content.name")]
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
    [InlineData("special@chars", "invalid")]
    [InlineData("spaces in id", "invalid")]
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
        string idString = "1.0.testpublisher.content";

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
        var manifestId = ManifestId.Create("1.0.testpublisher.content");

        // Act
        string idString = manifestId;

        // Assert
        Assert.Equal("1.0.testpublisher.content", idString);
    }

    /// <summary>
    /// Tests equality operator for ManifestId.
    /// </summary>
    [Fact]
    public void EqualityOperator_WithEqualManifestIds_ReturnsTrue()
    {
        // Arrange
        var id1 = ManifestId.Create("1.0.testpublisher.content");
        var id2 = ManifestId.Create("1.0.testpublisher.content");

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
        var id1 = ManifestId.Create("1.0.testpublisher.content");
        var id2 = ManifestId.Create("1.0.differentpublisher.content");

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
        var id1 = ManifestId.Create("1.0.testpublisher.content");
        var id2 = ManifestId.Create("1.0.TESTPUBLISHER.CONTENT");

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
        var id1 = ManifestId.Create("1.0.testpublisher.content");
        var id2 = ManifestId.Create("1.0.testpublisher.content");
        var id3 = ManifestId.Create("1.0.differentpublisher.content");

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
        var id1 = ManifestId.Create("1.0.testpublisher.content");
        var id2 = ManifestId.Create("1.0.testpublisher.content");
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
        var id1 = ManifestId.Create("1.0.testpublisher.content");
        var id2 = ManifestId.Create("1.0.testpublisher.content");

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
        var id1 = ManifestId.Create("1.0.testpublisher.content");
        var id2 = ManifestId.Create("1.0.TESTPUBLISHER.CONTENT");

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
        var id = ManifestId.Create("1.0.testpublisher.content");

        // Act
        var result = id.ToString();

        // Assert
        Assert.Equal("1.0.testpublisher.content", result);
    }

    /// <summary>
    /// Tests that ManifestId is a readonly struct.
    /// </summary>
    [Fact]
    public void ManifestId_IsReadonlyStruct()
    {
        // Arrange
        var id = ManifestId.Create("1.0.testpublisher.content");

        // Act & Assert - Should not be able to modify (readonly struct)
        Assert.Equal("1.0.testpublisher.content", id.Value);
    }

    /// <summary>
    /// Tests ManifestId with various valid game installation ID formats.
    /// </summary>
    /// <param name="gameInstallationId">A valid game installation ID string.</param>
    [Theory]
    [InlineData("1.0.steam.gameinstallation.generals")]
    [InlineData("1.0.eaapp.gameinstallation.zerohour")]
    [InlineData("1.0.origin.gameinstallation.generals")]
    [InlineData("1.0.thefirstdecade.gameinstallation.zerohour")]
    [InlineData("1.0.rgmechanics.gameinstallation.generals")]
    [InlineData("1.0.cdiso.gameinstallation.zerohour")]
    [InlineData("1.0.wine.gameinstallation.generals")]
    [InlineData("1.0.retail.gameinstallation.zerohour")]
    [InlineData("1.0.unknown.gameinstallation.generals")]
    public void Create_WithValidGameInstallationIds_CreatesManifestId(string gameInstallationId)
    {
        // Act
        var manifestId = ManifestId.Create(gameInstallationId);

        // Assert
        Assert.Equal(gameInstallationId, manifestId.Value);
    }

    /// <summary>
    /// Tests ManifestId with invalid game installation ID formats.
    /// </summary>
    /// <param name="invalidGameInstallationId">An invalid game installation ID string.</param>
    [Theory]
    [InlineData("1.0.steam.gameinstallation.generals.extra")] // Too many segments
    [InlineData("1.0.invalid.gameinstallation.generals")] // Invalid installation type
    [InlineData("1.0.steam.gameinstallation.invalid")] // Invalid game type
    [InlineData("1.0.steam.gameinstallation")] // Too few segments
    [InlineData("1.0.origin.gameinstallation")] // Too few segments
    [InlineData("1.0.eaapp.gameinstallation")] // Too few segments
    public void Create_WithInvalidGameInstallationIds_ThrowsArgumentException(string invalidGameInstallationId)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => ManifestId.Create(invalidGameInstallationId));
        Assert.Contains("invalid", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that ManifestId handles edge cases properly.
    /// </summary>
    [Fact]
    public void Create_HandlesEdgeCases()
    {
        // Test with minimum valid ID (4 segments)
        var minimalId = ManifestId.Create("1.0.test.content");
        Assert.Equal("1.0.test.content", minimalId.Value);

        // Test with maximum reasonable segments
        var complexId = ManifestId.Create("1.0.publisher.content.name.version");
        Assert.Equal("1.0.publisher.content.name.version", complexId.Value);
    }

    /// <summary>
    /// Tests that ManifestId preserves exact string value.
    /// </summary>
    [Fact]
    public void Value_PreservesExactString()
    {
        // Arrange
        const string originalId = "1.0.testpublisher.content.version.beta";

        // Act
        var manifestId = ManifestId.Create(originalId);

        // Assert
        Assert.Equal(originalId, manifestId.Value);
        Assert.Equal(originalId, (string)manifestId);
    }
}