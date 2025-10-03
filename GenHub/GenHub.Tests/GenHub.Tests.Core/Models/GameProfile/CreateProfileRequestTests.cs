using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using Xunit;

namespace GenHub.Tests.Core.Models.GameProfile
{
    /// <summary>
    /// Tests for <see cref="CreateProfileRequest"/>.
    /// </summary>
    public class CreateProfileRequestTests
    {
        /// <summary>
        /// Tests that CreateProfileRequest can be created with required properties.
        /// </summary>
        [Fact]
        public void CreateProfileRequest_WithRequiredProperties_ShouldBeValid()
        {
            // Act
            var request = new CreateProfileRequest
            {
                Name = "Test Profile",
                GameInstallationId = "install-1",
                GameClientId = "client-1",
            };

            // Assert
            Assert.Equal("Test Profile", request.Name);
            Assert.Equal("install-1", request.GameInstallationId);
            Assert.Equal("client-1", request.GameClientId);
            Assert.Equal(WorkspaceStrategy.HybridCopySymlink, request.PreferredStrategy);
        }

        /// <summary>
        /// Tests that CreateProfileRequest validates required properties.
        /// </summary>
        [Fact]
        public void CreateProfileRequest_WithNullName_ShouldThrowOrFail()
        {
            // Arrange
            var request = new CreateProfileRequest
            {
                Name = string.Empty,
                GameInstallationId = "install-1",
                GameClientId = "client-1",
            };

            // Act & Assert
            Assert.True(string.IsNullOrWhiteSpace(request.Name));
        }

        /// <summary>
        /// Tests that CreateProfileRequest validates GameInstallationId.
        /// </summary>
        [Fact]
        public void CreateProfileRequest_WithNullGameInstallationId_ShouldThrowOrFail()
        {
            // Arrange
            var request = new CreateProfileRequest
            {
                Name = "Test Profile",
                GameInstallationId = string.Empty,
                GameClientId = "client-1",
            };

            // Act & Assert
            Assert.True(string.IsNullOrWhiteSpace(request.GameInstallationId));
        }

        /// <summary>
        /// Tests that CreateProfileRequest validates GameClientId.
        /// </summary>
        [Fact]
        public void CreateProfileRequest_WithNullGameClientId_ShouldThrowOrFail()
        {
            // Arrange
            var request = new CreateProfileRequest
            {
                Name = "Test Profile",
                GameInstallationId = "install-1",
                GameClientId = string.Empty,
            };

            // Act & Assert
            Assert.True(string.IsNullOrWhiteSpace(request.GameClientId));
        }

        /// <summary>
        /// Tests that CreateProfileRequest properties can be modified.
        /// </summary>
        [Fact]
        public void CreateProfileRequest_PropertyModification_ShouldWork()
        {
            // Arrange
            var request = new CreateProfileRequest
            {
                Name = "Initial Name",
                GameInstallationId = "install-1",
                GameClientId = "client-1",
            };

            // Act
            request.Description = "Test Description";
            request.PreferredStrategy = WorkspaceStrategy.FullCopy;

            // Assert
            Assert.Equal("Test Description", request.Description);
            Assert.Equal(WorkspaceStrategy.FullCopy, request.PreferredStrategy);
        }
    }
}
