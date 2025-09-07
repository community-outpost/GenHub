using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Results;
using GenHub.Features.AppUpdate.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.AppUpdate.ViewModels;

/// <summary>
/// Unit tests for <see cref="UpdateNotificationViewModel"/>.
/// </summary>
public class UpdateNotificationViewModelTests
{
    /// <summary>
    /// Verifies that executing the CheckForUpdatesCommand updates the status and result.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CheckForUpdatesCommand_UpdatesStatus()
    {
        var svc = new Mock<IAppUpdateService>();
        svc.Setup(x => x.CheckForUpdatesAsync("o", "r", It.IsAny<CancellationToken>()))
           .ReturnsAsync(UpdateCheckResult.UpdateAvailable(new GitHubRelease { TagName = "1.2.3", HtmlUrl = "https://example.com", Body = "Release notes", Name = "Release 1.2.3" }));
        var vm = new UpdateNotificationViewModel(svc.Object, Mock.Of<IUpdateInstaller>(), Mock.Of<ILogger<UpdateNotificationViewModel>>())
        {
            RepositoryOwner = "o",
            RepositoryName = "r",
        };

        // Use the generated RelayCommand property name
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.CheckForUpdatesCommand).ExecuteAsync(null);

        Assert.NotNull(vm.UpdateCheckResult);
        Assert.True(vm.UpdateCheckResult.IsUpdateAvailable);
        Assert.Contains("1.2.3", vm.StatusMessage);
    }
}
