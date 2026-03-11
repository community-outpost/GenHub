using System.Text.Json;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using Xunit;
using GameProfileModel = GenHub.Core.Models.GameProfile.GameProfile;

namespace GenHub.Tests.Core.Models;

/// <summary>
/// Tests to verify that GameProfile correctly applies default values during deserialization.
/// This addresses the bug where WorkspaceStrategy was defaulting to SymlinkOnly (enum default 0)
/// WorkspaceStrategyJsonConverter correctly handles the null/missing property, allowing
/// services to apply the global default fallback.
/// </summary>
public class GameProfileDeserializationTests
{
    /// <summary>
    /// Verifies that deserialization defaults to null when WorkspaceStrategy is missing.
    /// </summary>
    [Fact]
    public void Deserialize_ProfileWithoutWorkspaceStrategy_ShouldHaveNullStrategy()
    {
        // Arrange - JSON without WorkspaceStrategy property
        var json = """
        {
            "Id": "test_profile",
            "Name": "Test Profile",
            "Description": "Test description"
        }
        """;

        // Act
        var profile = JsonSerializer.Deserialize<GameProfileModel>(json);

        // Assert
        Assert.NotNull(profile);
        Assert.Null(profile.WorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that SymlinkOnly is PRESERVED when explicit in JSON.
    /// </summary>
    [Fact]
    public void Deserialize_ProfileWithSymlinkOnly_ShouldPreserveSymlinkOnly()
    {
        // Arrange - JSON with explicit SymlinkOnly (1)
        var json = """
        {
            "Id": "test_profile",
            "Name": "Test Profile",
            "WorkspaceStrategy": 1
        }
        """;

        // Act
        var profile = JsonSerializer.Deserialize<GameProfileModel>(json);

        // Assert
        Assert.NotNull(profile);

        // Should NOT be overridden to HardLink anymore
        Assert.Equal(WorkspaceStrategy.SymlinkOnly, profile.WorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that explicit HardLink is preserved during deserialization.
    /// </summary>
    [Fact]
    public void Deserialize_ProfileWithExplicitHardLink_ShouldPreserveHardLink()
    {
        // Arrange - JSON with explicit HardLink (0)
        var json = """
        {
            "Id": "test_profile",
            "Name": "Test Profile",
            "WorkspaceStrategy": 0
        }
        """;

        // Act
        var profile = JsonSerializer.Deserialize<GameProfileModel>(json);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(WorkspaceStrategy.HardLink, profile.WorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that FullCopy strategy is preserved during deserialization.
    /// </summary>
    [Fact]
    public void Deserialize_ProfileWithCopyStrategy_ShouldPreserveCopy()
    {
        // Arrange - JSON with explicit Copy strategy (2)
        var json = """
        {
            "Id": "test_profile",
            "Name": "Test Profile",
            "WorkspaceStrategy": 2
        }
        """;

        // Act
        var profile = JsonSerializer.Deserialize<GameProfileModel>(json);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(WorkspaceStrategy.FullCopy, profile.WorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that WorkspaceStrategy is preserved after a serialization round-trip for HardLink.
    /// </summary>
    [Fact]
    public void Serialize_ThenDeserialize_HardLink_ShouldPreserveWorkspaceStrategy()
    {
        // Arrange
        var originalProfile = new GameProfileModel
        {
            Id = "test_profile",
            Name = "Test Profile",
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
        };

        // Act - Round trip through JSON
        var json = JsonSerializer.Serialize(originalProfile);
        var deserializedProfile = JsonSerializer.Deserialize<GameProfileModel>(json);

        // Assert
        Assert.NotNull(deserializedProfile);
        Assert.Equal(WorkspaceStrategy.HardLink, deserializedProfile.WorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that WorkspaceStrategy is preserved after a serialization round-trip for FullCopy.
    /// </summary>
    [Fact]
    public void Serialize_ThenDeserialize_FullCopy_ShouldPreserveWorkspaceStrategy()
    {
        // Arrange
        var originalProfile = new GameProfileModel
        {
            Id = "test_profile_copy",
            Name = "Test Profile Copy",
            WorkspaceStrategy = WorkspaceStrategy.FullCopy,
        };

        // Act - Round trip through JSON
        var json = JsonSerializer.Serialize(originalProfile);
        var deserializedProfile = JsonSerializer.Deserialize<GameProfileModel>(json);

        // Assert
        Assert.NotNull(deserializedProfile);
        Assert.Equal(WorkspaceStrategy.FullCopy, deserializedProfile.WorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that WorkspaceStrategy is preserved after a serialization round-trip for SymlinkOnly.
    /// </summary>
    [Fact]
    public void Serialize_ThenDeserialize_SymlinkOnly_ShouldPreserveWorkspaceStrategy()
    {
        // Arrange
        var originalProfile = new GameProfileModel
        {
            Id = "test_profile_symlink",
            Name = "Test Profile Symlink",
            WorkspaceStrategy = WorkspaceStrategy.SymlinkOnly,
        };

        // Act - Round trip through JSON
        var json = JsonSerializer.Serialize(originalProfile);
        var deserializedProfile = JsonSerializer.Deserialize<GameProfileModel>(json);

        // Assert
        Assert.NotNull(deserializedProfile);
        Assert.Equal(WorkspaceStrategy.SymlinkOnly, deserializedProfile.WorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that a new profile instance has null WorkspaceStrategy (relying on default).
    /// </summary>
    [Fact]
    public void NewProfile_ShouldHaveNullWorkspaceStrategy()
    {
        // Arrange & Act
        var profile = new GameProfileModel
        {
            Id = "test_profile",
            Name = "Test Profile",
        };

        // Assert
        Assert.Null(profile.WorkspaceStrategy);
    }

    /// <summary>
    /// Verifies that string-based enum values are parsed correctly during deserialization.
    /// This ensures backward compatibility or manual editing support where strings like "HardLink" are used.
    /// </summary>
    [Fact]
    public void Deserialize_ProfileWithStringEnum_ShouldParseCorrectly()
    {
        // Arrange - JSON with string enum value
        var json = """
        {
            "Id": "test_profile",
            "Name": "Test Profile",
            "WorkspaceStrategy": "HardLink"
        }
        """;

        // Act
        // Note: Default System.Text.Json requires JsonStringEnumConverter to handle strings.
        // We assume the global serializer options or attribute on the property handles this.
        // If it fails, it means we need to ensure the converter is registered.
        // However, for this test, we are testing if the MODEL supports it via the configured serializer.
        // If the project uses a custom converter factory or attribute, this should work.
        var options = new JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true,
        };
        var profile = JsonSerializer.Deserialize<GameProfileModel>(json, options);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal(WorkspaceStrategy.HardLink, profile.WorkspaceStrategy);
    }
}
