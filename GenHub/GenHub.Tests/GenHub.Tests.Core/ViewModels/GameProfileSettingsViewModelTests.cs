using GenHub.Features.GameProfiles.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Contains tests for <see cref="GameProfileSettingsViewModel"/>.
/// </summary>
public class GameProfileSettingsViewModelTests
{
    /// <summary>
    /// Verifies that the <see cref="GameProfileSettingsViewModel"/> can be constructed.
    /// </summary>
    [Fact]
    public void CanConstruct()
    {
        var vm = new GameProfileSettingsViewModel("test-profile");
        Assert.NotNull(vm);
    }
}
