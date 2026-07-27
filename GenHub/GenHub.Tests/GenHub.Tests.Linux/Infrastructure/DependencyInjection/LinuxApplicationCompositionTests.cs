using System.Runtime.Versioning;
using GenHub.Common.ViewModels;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Shortcuts;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.Settings.ViewModels;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Linux.GameInstallations;
using GenHub.Linux.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Tests.Linux.Infrastructure.DependencyInjection;

/// <summary>
/// Verifies the Linux application dependency injection composition.
/// </summary>
[Collection(ApplicationCompositionCollection.Name)]
public class LinuxApplicationCompositionTests
{
    private sealed class TemporaryApplicationEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = [];

        internal TemporaryApplicationEnvironment()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"GenHub.Tests.{Guid.NewGuid():N}");
            AppDataPath = Path.Combine(RootPath, "AppData");
            Directory.CreateDirectory(AppDataPath);

            SetEnvironmentVariable("GENHUB_GenHub__AppDataPath", AppDataPath);
            SetEnvironmentVariable("APPDATA", Path.Combine(RootPath, "RoamingAppData"));
            SetEnvironmentVariable("LOCALAPPDATA", Path.Combine(RootPath, "LocalAppData"));
            SetEnvironmentVariable("USERPROFILE", RootPath);
            SetEnvironmentVariable("HOME", RootPath);
            SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(RootPath, "Config"));
            SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(RootPath, "Data"));
        }

        internal string AppDataPath { get; }

        private string RootPath { get; }

        void IDisposable.Dispose()
        {
            foreach (var pair in _originalValues)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            Directory.Delete(RootPath, recursive: true);
        }

        private void SetEnvironmentVariable(string name, string value)
        {
            _originalValues[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <summary>
    /// Verifies that the real shared and Linux registrations resolve the startup view model graph.
    /// </summary>
    /// <remarks>
    /// This is dependency injection composition coverage. It does not launch Avalonia or a packaged
    /// Linux application.
    /// </remarks>
    [Fact]
    [SupportedOSPlatform("linux")]
    public void ConfigureApplicationServices_ResolvesStartupViewModels()
    {
        using var testEnvironment = new TemporaryApplicationEnvironment();
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(platformServices => platformServices.AddLinuxServices());

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Equal(
            testEnvironment.AppDataPath,
            serviceProvider.GetRequiredService<IConfigurationProviderService>().GetRootAppDataPath());
        Assert.IsType<LinuxInstallationDetector>(
            serviceProvider.GetRequiredService<IGameInstallationDetector>());
        Assert.NotNull(serviceProvider.GetRequiredService<IShortcutService>());
        Assert.Null(serviceProvider.GetService<IGitHubTokenStorage>());

        var settingsViewModel = serviceProvider.GetRequiredService<SettingsViewModel>();
        Assert.NotNull(serviceProvider.GetRequiredService<GameProfileSettingsViewModel>());

        var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
        Assert.Same(settingsViewModel, mainViewModel.SettingsViewModel);
        Assert.NotNull(mainViewModel.GameProfilesViewModel);
        Assert.NotNull(mainViewModel.DownloadsViewModel);
        Assert.NotNull(mainViewModel.ToolsViewModel);
    }
}
