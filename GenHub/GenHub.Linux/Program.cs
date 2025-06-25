using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using GenHub.Features.GameVersions.Services;
using GenHub.Core.Interfaces.GameVersions;
using GenHub.Common.ViewModels;

namespace GenHub.Linux;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // TODO: Create lockfile to guarantee that only one instance is running on linux

        // Dependency injection
        var services = new ServiceCollection();

        // Linux-specific DI
        services.AddSingleton<IGameDetector, LinuxGameDetector>();

        // Core DI
        services.AddSingleton<GameDetectionService>();
        services.AddSingleton<MainViewModel>();

        var serviceProvider = services.BuildServiceProvider();

        // Set static service locator for bootstrapping. This is needed for avalonia to receive the service provider
        AppLocator.Services = serviceProvider;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
