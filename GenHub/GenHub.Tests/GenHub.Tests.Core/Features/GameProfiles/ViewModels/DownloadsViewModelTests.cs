using GenHub.Core.Interfaces.Manifest;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Tests for DownloadsViewModel.
/// </summary>
public class DownloadsViewModelTests
{
    /// <summary>
    /// Ensures InitializeAsync completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_CompletesSuccessfully()
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerMock = new Mock<ILogger<DownloadsViewModel>>();

        var contentOrchestratorMock = new Mock<GenHub.Core.Interfaces.Content.IContentOrchestrator>();
        var manifestPoolMock = new Mock<IContentManifestPool>();
        var vm = new DownloadsViewModel(serviceProviderMock.Object, loggerMock.Object);
        await vm.InitializeAsync();
    }
}
