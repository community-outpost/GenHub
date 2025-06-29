using System.Threading.Tasks;
using GenHub.Common.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Verifies basic behavior of <see cref="MainViewModel"/>.
/// </summary>
public class MainViewModelTests
{
    [Fact]
    public async Task InitializeAsync_CompletesSuccessfully()
    {
        // Arrange
        var vm = new MainViewModel();

        // Act & Assert
        await vm.InitializeAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SelectTabCommand_SetsSelectedTabIndex(int index)
    {
        var vm = new MainViewModel();
        vm.SelectTabCommand.Execute(index);
        Assert.Equal(index, vm.SelectedTabIndex);
    }
}
