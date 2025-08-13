using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace
{
    /// <summary>
    /// Tests for the <see cref="WorkspaceManager"/> class.
    /// </summary>
    public class WorkspaceManagerTests : IDisposable
    {
        private readonly Mock<IWorkspaceStrategy> _strategyMock;
        private readonly Mock<IConfigurationProviderService> _configProviderMock;
        private readonly Mock<ILogger<WorkspaceManager>> _loggerMock;
        private readonly Mock<ICasService> _casServiceMock;
        private readonly CasReferenceTracker _casReferenceTracker;
        private readonly WorkspaceManager _workspaceManager;
        private readonly string _tempDir;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkspaceManagerTests"/> class.
        /// </summary>
        public WorkspaceManagerTests()
        {
            _strategyMock = new Mock<IWorkspaceStrategy>();
            _configProviderMock = new Mock<IConfigurationProviderService>();
            _loggerMock = new Mock<ILogger<WorkspaceManager>>();
            _casServiceMock = new Mock<ICasService>();
            _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);

            _configProviderMock.Setup(x => x.GetContentStoragePath()).Returns(_tempDir);

            // Create CasReferenceTracker with proper dependencies
            var casConfig = new Mock<IOptions<CasConfiguration>>();
            casConfig.Setup(x => x.Value).Returns(new CasConfiguration { CasRootPath = _tempDir });
            var casLogger = new Mock<ILogger<CasReferenceTracker>>();
            _casReferenceTracker = new CasReferenceTracker(casConfig.Object, casLogger.Object);

            _workspaceManager = new WorkspaceManager(
                new[] { _strategyMock.Object },
                _configProviderMock.Object,
                _loggerMock.Object,
                _casServiceMock.Object,
                _casReferenceTracker);
        }

        /// <summary>
        /// Tests that PrepareWorkspaceAsync throws when no strategy can handle the configuration.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Fact]
        public async Task PrepareWorkspaceAsync_ThrowsIfNoStrategy()
        {
            // Create a temporary directory for this test
            var testTempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(testTempDir);

            try
            {
                var mockConfigProvider = new Mock<IConfigurationProviderService>();
                mockConfigProvider.Setup(x => x.GetContentStoragePath()).Returns(Path.Combine(testTempDir, "content"));

                var mockLogger = new Mock<ILogger<WorkspaceManager>>();
                var mockCasService = new Mock<ICasService>();

                // Create CasReferenceTracker for this test with proper temp directory
                var casConfig = new Mock<IOptions<CasConfiguration>>();
                casConfig.Setup(x => x.Value).Returns(new CasConfiguration { CasRootPath = Path.Combine(testTempDir, "cas") });
                var casLogger = new Mock<ILogger<CasReferenceTracker>>();
                var casTracker = new CasReferenceTracker(casConfig.Object, casLogger.Object);

                var manager = new WorkspaceManager(
                    System.Array.Empty<IWorkspaceStrategy>(),
                    mockConfigProvider.Object,
                    mockLogger.Object,
                    mockCasService.Object,
                    casTracker);

                var config = new WorkspaceConfiguration();
                await Assert.ThrowsAsync<System.InvalidOperationException>(() => manager.PrepareWorkspaceAsync(config));
            }
            finally
            {
                // Clean up the temporary directory
                if (Directory.Exists(testTempDir))
                {
                    Directory.Delete(testTempDir, true);
                }
            }
        }

        /// <summary>
        /// Tests that PrepareWorkspaceAsync with a valid configuration returns a successful result.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Fact]
        public async Task PrepareWorkspaceAsync_WithValidConfiguration_ShouldSucceed()
        {
            // Arrange
            var config = new WorkspaceConfiguration
            {
                Id = "test-workspace",
                Strategy = WorkspaceStrategy.FullCopy,
                Manifests = new List<ContentManifest>
                {
                    new() { Files = new List<ManifestFile> { new() { RelativePath = "test.txt", Size = 100 } } },
                },
            };

            var workspaceInfo = new WorkspaceInfo { Id = config.Id, Success = true };
            _strategyMock.Setup(x => x.CanHandle(config)).Returns(true);
            _strategyMock.Setup(x => x.PrepareAsync(config, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(workspaceInfo);

            // Act
            var result = await _workspaceManager.PrepareWorkspaceAsync(config);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
        }

        /// <summary>
        /// Tests that CleanupWorkspaceAsync removes an existing workspace directory successfully.
        /// </summary>
        /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
        [Fact]
        public async Task CleanupWorkspaceAsync_WithExistingWorkspace_ShouldSucceed()
        {
            // Arrange
            var workspaceId = "test-workspace";
            var workspaceDir = Path.Combine(_tempDir, workspaceId);
            Directory.CreateDirectory(workspaceDir);

            // Create a workspace info and save it to the metadata file to simulate an existing workspace
            var workspaceInfo = new WorkspaceInfo
            {
                Id = workspaceId,
                WorkspacePath = workspaceDir,
                Success = true,
            };

            var workspaces = new List<WorkspaceInfo> { workspaceInfo };
            var workspaceMetadataPath = Path.Combine(_tempDir, "workspaces.json");
            var json = System.Text.Json.JsonSerializer.Serialize(workspaces, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(workspaceMetadataPath, json);

            // Act
            var result = await _workspaceManager.CleanupWorkspaceAsync(workspaceId);

            // Assert
            Assert.True(result);
            Assert.False(Directory.Exists(workspaceDir));
        }

        /// <summary>
        /// Disposes of test resources and suppresses finalization.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }

            GC.SuppressFinalize(this);
        }
    }
}
