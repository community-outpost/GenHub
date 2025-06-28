namespace GenHub.Tests.Core.DependencyInjection;

using GenHub.Common.ViewModels;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Tests for SharedViewModelModule DI registration.
/// </summary>
public class SharedViewModelModuleTests
{
    /// <summary>
    /// Ensures all shared ViewModels are registered in DI.
    /// </summary>
    [Fact]
    public void AllViewModels_Registered()
    {
        var services = new ServiceCollection()
            .AddSharedViewModelModule()
            .BuildServiceProvider();

        Assert.NotNull(services.GetService<MainViewModel>());
        Assert.NotNull(services.GetService<GameProfileLauncherViewModel>());
        Assert.NotNull(services.GetService<DownloadsViewModel>());
        Assert.NotNull(services.GetService<SettingsViewModel>());
    }
}
