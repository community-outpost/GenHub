using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Features.GameProfiles.ViewModels;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Tests for <see cref="GameProfileItemViewModel"/>.
/// </summary>
public class GameProfileItemViewModelTests
{
    /// <summary>
    /// Verifies construction of <see cref="GameProfileItemViewModel"/>.
    /// </summary>
    [Fact]
    public void CanConstruct()
    {
        var mockProfile = new Mock<IGameProfile>();
        mockProfile.SetupGet(p => p.Version).Returns("1.0");
        mockProfile.SetupGet(p => p.ExecutablePath).Returns("C:/fake/path.exe");
        var vm = new GameProfileItemViewModel(mockProfile.Object, "icon.png", "cover.jpg");
        Assert.NotNull(vm);
    }
}
