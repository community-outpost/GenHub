using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
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
        private readonly Mock<IConfigurationProviderService> _mockConfigProvider;
        private readonly Mock<ILogger<WorkspaceManager>> _mockLogger;
        private readonly Mock<IWorkspaceValidator> _mockWorkspaceValidator;
        private readonly Mock<ILogger<WorkspaceReconciler>> _mockReconcilerLogger;
        private readonly IWorkspaceStrategy[] _strategies;
        private readonly CasReferenceTracker _casReferenceTracker;
        private readonly WorkspaceReconciler _workspaceReconciler;
        private readonly WorkspaceManager _manager;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkspaceManagerTests"/> class.
        /// </summary>
        public WorkspaceManagerTests()
        {
            _mockConfigProvider = new Mock<IConfigurationProviderService>();
            _mockConfigProvider.Setup(x => x.GetContentStoragePath()).Returns("/test/content/path");

            _mockLogger = new Mock<ILogger<WorkspaceManager>>();
            _mockWorkspaceValidator = new Mock<IWorkspaceValidator>();
            _mockReconcilerLogger = new Mock<ILogger<WorkspaceReconciler>>();
            _strategies = System.Array.Empty<IWorkspaceStrategy>();

            // Create CasReferenceTracker with required dependencies
            var mockCasConfig = new Mock<Microsoft.Extensions.Options.IOptions<CasConfiguration>>();
            mockCasConfig.Setup(x => x.Value).Returns(new CasConfiguration { CasRootPath = "/test/cas" });
            var mockCasLogger = new Mock<ILogger<CasReferenceTracker>>();
            _casReferenceTracker = new CasReferenceTracker(mockCasConfig.Object, mockCasLogger.Object);

            // Create WorkspaceReconciler
            _workspaceReconciler = new WorkspaceReconciler(_mockReconcilerLogger.Object);

            _manager = new WorkspaceManager(_strategies, _mockConfigProvider.Object, _mockLogger.Object, _casReferenceTracker, _mockWorkspaceValidator.Object, _workspaceReconciler);
        }

        /// <summary>
        /// Tests that PrepareWorkspaceAsync throws InvalidOperationException for invalid strategy.
        /// </summary>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Fact]
        public async Task PrepareWorkspaceAsync_InvalidStrategy_ThrowsInvalidOperationException()
        {
            var config = new WorkspaceConfiguration
            {
                Strategy = (WorkspaceStrategy)999,
            };
            await Assert.ThrowsAsync<System.InvalidOperationException>(() => _manager.PrepareWorkspaceAsync(config));
        }

        /// <summary>
        /// Disposes the test resources.
        /// </summary>
        public void Dispose()
        {
            // Cleanup if needed
        }
    }
}
