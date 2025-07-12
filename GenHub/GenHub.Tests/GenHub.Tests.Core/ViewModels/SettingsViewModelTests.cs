using GenHub.Features.Settings.ViewModels;
using Xunit;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Tests for SettingsViewModel.
/// </summary>
public class SettingsViewModelTests
{
    /// <summary>
    /// Ensures InitializeAsync completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_CompletesSuccessfully()
    {
        var vm = new SettingsViewModel();
        await vm.InitializeAsync();
    }
}
