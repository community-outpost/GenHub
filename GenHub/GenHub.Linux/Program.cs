using System;
using Avalonia;
using GenHub.Core;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Services;
using GenHub.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GenHub.Linux;

/// <summary>
/// Main class for main entry point.
/// </summary>
public class Program
{
    /// <summary>
    /// Main entry point for the application.
    /// </summary>
    /// <param name="args">Program startup arguments.</param>
    /// <remarks>
    /// Initialization code. Don't use any Avalonia, third-party APIs or any
    /// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    /// yet and stuff might break.
    /// </remarks>
    [STAThread]
    public static void Main(string[] args)
    {
        // TODO: Create lockfile to guarantee that only one instance is running on linux

        // Dependency injection
        var services = new ServiceCollection();

        // Linux-specific DI
        services.AddSingleton<IGameDetector, LinuxGameDetector>();

        // Register shared services
        services.ConfigureApplicationServices();

        var serviceProvider = services.BuildServiceProvider();

        // Set static service locator for bootstrapping. This is needed for avalonia to receive the service provider
        AppLocator.Services = serviceProvider;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Avalonia configuration.
    /// </summary>
    /// <returns>The <see cref="AppBuilder"/>.</returns>
    /// <remarks>
    /// Don't remove; also used by visual designer.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
