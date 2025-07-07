using System.Threading.Tasks;
using GenHub.Common.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Contains unit tests for the <see cref="MainViewModel"/> class.
/// </summary>
public class MainViewModelTests
{
    /// <summary>
    /// Tests that <see cref="MainViewModel.InitializeAsync"/> completes successfully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_CompletesSuccessfully()
    {
        // Arrange
        var vm = new MainViewModel();

        // Act
        var task = vm.InitializeAsync();
        await task;

        // Assert
        Assert.True(task.IsCompletedSuccessfully);
        Assert.NotNull(vm);
    }

    /// <summary>
    /// Tests that <see cref="MainViewModel"/> can be instantiated successfully.
    /// </summary>
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Act
        var vm = new MainViewModel();

        // Assert
        Assert.NotNull(vm);
        Assert.IsType<MainViewModel>(vm);
    }

    /// <summary>
    /// Tests that multiple calls to <see cref="MainViewModel.InitializeAsync"/> are safe.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task InitializeAsync_MultipleCallsAreSafe()
    {
        // Arrange
        var vm = new MainViewModel();

        // Act
        await vm.InitializeAsync();
        await vm.InitializeAsync();

        // Assert
        Assert.NotNull(vm);
    }
}
