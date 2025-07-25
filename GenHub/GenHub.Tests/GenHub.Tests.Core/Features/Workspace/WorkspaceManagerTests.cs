using System.Threading.Tasks;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

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
        var logger = new Mock<ILogger<WorkspaceManager>>();
        var manager = new WorkspaceManager(System.Array.Empty<IWorkspaceStrategy>(), logger.Object);
        var config = new WorkspaceConfiguration();
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => manager.PrepareWorkspaceAsync(config));
    }
}
