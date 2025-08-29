using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Tests for the <see cref="WorkspaceManager"/> class.
/// </summary>
public class WorkspaceManagerTests
{
    /// <summary>
    /// Tests that PrepareWorkspaceAsync throws when no strategy can handle the configuration.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_ThrowsIfNoStrategy()
    {
        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        mockConfigProvider.Setup(x => x.GetContentStoragePath()).Returns("/test/content/path");

        var mockLogger = new Mock<ILogger<WorkspaceManager>>();
        var strategies = System.Array.Empty<IWorkspaceStrategy>();

        // Create CasReferenceTracker with required dependencies
        var mockCasConfig = new Mock<Microsoft.Extensions.Options.IOptions<CasConfiguration>>();
        mockCasConfig.Setup(x => x.Value).Returns(new CasConfiguration { CasRootPath = "/test/cas" });
        var mockCasLogger = new Mock<ILogger<CasReferenceTracker>>();
        var casReferenceTracker = new CasReferenceTracker(mockCasConfig.Object, mockCasLogger.Object);

        var manager = new WorkspaceManager(strategies, mockConfigProvider.Object, mockLogger.Object, casReferenceTracker);
        var config = new WorkspaceConfiguration
        {
            Strategy = (WorkspaceStrategy)999,
        };
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => manager.PrepareWorkspaceAsync(config));
    }
}
