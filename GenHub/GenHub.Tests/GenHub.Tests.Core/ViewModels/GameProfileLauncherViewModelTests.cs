using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Tests for GameProfileLauncherViewModel.
/// </summary>
public class GameProfileLauncherViewModelTests
{
    /// <summary>
    /// Ensures InitializeAsync completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_CompletesSuccessfully()
    {
        var vm = new GameProfileLauncherViewModel();
        await vm.InitializeAsync();
    }
}
