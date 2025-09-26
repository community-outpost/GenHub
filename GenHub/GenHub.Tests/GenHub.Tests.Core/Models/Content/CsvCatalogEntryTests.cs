using GenHub.Core.Models.Content;
using Xunit;

namespace GenHub.Tests.Core.Models.Content;

/// <summary>
/// Unit tests for <see cref="CsvCatalogEntry"/>.
/// </summary>
public class CsvCatalogEntryTests
{
    /// <summary>
    /// Verifies that CsvCatalogEntry can be instantiated with default values.
    /// </summary>
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var entry = new CsvCatalogEntry();

        // Assert
        Assert.Equal(string.Empty, entry.RelativePath);
        Assert.Equal(0, entry.Size);
        Assert.Equal(string.Empty, entry.Md5);
        Assert.Equal(string.Empty, entry.Sha256);
        Assert.Equal("Generals", entry.GameType);
        Assert.Equal("All", entry.Language);
        Assert.True(entry.IsRequired);
        Assert.Null(entry.Metadata);
    }

    /// <summary>
    /// Verifies that all properties can be set and retrieved.
    /// </summary>
    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var entry = new CsvCatalogEntry
        {
            RelativePath = "Data/INI/GameData.ini",
            Size = 12345,
            Md5 = "abc123",
            Sha256 = "def456",
            GameType = "Generals",
            Language = "EN",
            IsRequired = true,
            Metadata = "{\"category\":\"config\"}",
        };

        // Assert
        Assert.Equal("Data/INI/GameData.ini", entry.RelativePath);
        Assert.Equal(12345, entry.Size);
        Assert.Equal("abc123", entry.Md5);
        Assert.Equal("def456", entry.Sha256);
        Assert.Equal("Generals", entry.GameType);
        Assert.Equal("EN", entry.Language);
        Assert.True(entry.IsRequired);
        Assert.Equal("{\"category\":\"config\"}", entry.Metadata);
    }

    /// <summary>
    /// Verifies that Language defaults to "All".
    /// </summary>
    [Fact]
    public void Language_DefaultsToAll()
    {
        // Arrange & Act
        var entry = new CsvCatalogEntry();

        // Assert
        Assert.Equal("All", entry.Language);
    }

    /// <summary>
    /// Verifies that IsRequired defaults to true.
    /// </summary>
    [Fact]
    public void IsRequired_DefaultsToTrue()
    {
        // Arrange & Act
        var entry = new CsvCatalogEntry();

        // Assert
        Assert.True(entry.IsRequired);
    }
}
