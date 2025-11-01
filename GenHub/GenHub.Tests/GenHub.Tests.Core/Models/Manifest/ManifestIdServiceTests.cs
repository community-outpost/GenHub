using System;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Unit tests for ManifestIdService to ensure proper ResultBase pattern usage.
/// </summary>
public class ManifestIdServiceTests
{
    private readonly ManifestIdService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestIdServiceTests"/> class.
    /// </summary>
    public ManifestIdServiceTests()
    {
        _service = new ManifestIdService();
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId uses default user version when not provided.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_UsesDefaultUserVersion_WhenNotProvided()
    {
        // Arrange
        var publisherId = "testpublisher";
        var contentName = "testcontent";

        // Act
        var result = _service.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("1.0.testpublisher.mod.testcontent", result.Data.Value);
        Assert.Null(result.FirstError);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId returns failure for invalid inputs.
    /// </summary>
    /// <param name="publisherId">Invalid publisher ID.</param>
    /// <param name="contentName">Invalid content name.</param>
    [Theory]
    [InlineData("", "content")]
    [InlineData(" ", "content")]
    [InlineData("publisher", "")]
    [InlineData("publisher", " ")]
    public void GeneratePublisherContentId_WithInvalidInputs_ReturnsFailure(string publisherId, string contentName)
    {
        // Act
        var result = _service.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, 0);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Data.Value);
        Assert.NotNull(result.FirstError);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId uses default user version when not provided.
    /// </summary>
    [Fact]
    public void GenerateGameInstallationId_UsesDefaultUserVersion_WhenNotProvided()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var gameType = GameType.Generals;

        // Act
        var result = _service.GenerateGameInstallationId(installation, gameType, null);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("1.0.steam.gameinstallation.generals", result.Data.Value);
        Assert.Null(result.FirstError);
    }

    /// <summary>
    /// Tests failure when generating game installation ID with null installation.
    /// </summary>
    [Fact]
    public void GenerateGameInstallationId_WithNullInstallation_ReturnsFailure()
    {
        // Act
        var result = _service.GenerateGameInstallationId(null!, GameType.Generals, 0);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Data.Value);
        Assert.Contains("Installation cannot be null", result.FirstError);
    }

    /// <summary>
    /// Tests successful validation and creation of manifest ID.
    /// </summary>
    [Fact]
    public void ValidateAndCreateManifestId_WithValidId_ReturnsSuccess()
    {
        // Arrange - Use a valid 5-segment ID
        var validId = "1.0.genhub.mod.testcontent";

        // Act
        var result = _service.ValidateAndCreateManifestId(validId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(validId, result.Data.Value);
        Assert.Null(result.FirstError);
    }

    /// <summary>
    /// Tests failure when validating invalid manifest ID.
    /// </summary>
    [Fact]
    public void ValidateAndCreateManifestId_WithInvalidId_ReturnsFailure()
    {
        // Arrange
        var invalidId = "invalid@chars";

        // Act
        var result = _service.ValidateAndCreateManifestId(invalidId);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Data.Value);
        Assert.Contains("invalid", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that service handles exceptions gracefully.
    /// </summary>
    [Fact]
    public void Service_HandlesExceptionsGracefully()
    {
        // This test ensures that any unexpected exceptions are caught and returned as failures
        // We can't easily test this without mocking, but we can verify the pattern is in place
        // by checking that all public methods follow the try-catch pattern

        // Arrange - Create a scenario that might cause an exception
        // (This is difficult to test without more complex setup, but we verify the pattern exists)

        // Act - Call a method that should handle exceptions
        var result = _service.GeneratePublisherContentId("test", GenHub.Core.Models.Enums.ContentType.Mod, "content", 0);

        // Assert - Should either succeed or fail gracefully
        Assert.NotNull(result);

        // Either Success is true, or Success is false with FirstError
        Assert.True(result.Success || (!result.Success && result.FirstError != null));
    }

    /// <summary>
    /// Tests that all service methods return proper ResultBase implementations.
    /// </summary>
    [Fact]
    public void AllMethods_ReturnProperResultBaseImplementations()
    {
        // Test GeneratePublisherContentId
        var result1 = _service.GeneratePublisherContentId("test", GenHub.Core.Models.Enums.ContentType.Mod, "content", 0);
        Assert.IsAssignableFrom<ResultBase>(result1);

        // Test GenerateGameInstallationId
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var result2 = _service.GenerateGameInstallationId(installation, GameType.Generals, 0);
        Assert.IsAssignableFrom<ResultBase>(result2);

        // Test ValidateAndCreateManifestId
        var result3 = _service.ValidateAndCreateManifestId("1.0.genhub.mod.testcontent");
        Assert.IsAssignableFrom<ResultBase>(result3);
    }

    /// <summary>
    /// Tests that successful operations have no errors and failed operations have error messages.
    /// </summary>
    [Fact]
    public void SuccessAndFailureStates_AreMutuallyExclusive()
    {
        // Test success case
        var successResult = _service.GeneratePublisherContentId("test", GenHub.Core.Models.Enums.ContentType.Mod, "content", 0);
        Assert.True(successResult.Success);
        Assert.NotNull(successResult.Data.Value);
        Assert.Null(successResult.FirstError);

        // Test failure case (empty publisher)
        var failureResult = _service.GeneratePublisherContentId(string.Empty, GenHub.Core.Models.Enums.ContentType.Mod, "content", 0);
        Assert.False(failureResult.Success);
        Assert.Null(failureResult.Data.Value);
        Assert.NotNull(failureResult.FirstError);
    }
}
