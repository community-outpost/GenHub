using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
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
        var dummyLogger = new Mock<ILogger<CasReferenceTracker>>().Object;
        var dummyOptions = new Mock<Microsoft.Extensions.Options.IOptions<CasConfiguration>>();
        dummyOptions.Setup(x => x.Value).Returns(new CasConfiguration { CasRootPath = Path.GetTempPath() });
        var casReferenceTracker = new CasReferenceTracker(dummyOptions.Object, dummyLogger);
        var manager = new WorkspaceManager(System.Array.Empty<IWorkspaceStrategy>(), mockConfigProvider.Object, mockLogger.Object, casReferenceTracker);
        var config = new WorkspaceConfiguration();
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => manager.PrepareWorkspaceAsync(config));
    }
}
