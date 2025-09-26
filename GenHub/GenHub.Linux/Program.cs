using System;
using Avalonia;
using GenHub.Core;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Linux.Features.AppUpdate;
using GenHub.Linux.GameInstallations;
using GenHub.Linux.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux;

/// <summary>
/// Main class for main entry point.
/// </summary>
public class Program
{
    private const string UpdaterUserAgent = "GenHub-Updater/1.0";
    private static readonly TimeSpan UpdaterTimeout = TimeIntervals.UpdaterTimeout;

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
        using var bootstrapLoggerFactory = LoggingModule.CreateBootstrapLoggerFactory();
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<Program>();
        try
        {
            bootstrapLogger.LogInformation("Starting GenHub Linux application");

            var services = new ServiceCollection();

            try
            {
                // Register shared services and Linux-specific services
                services.ConfigureApplicationServices(s => s.AddLinuxServices());
            }
            catch (Exception configEx)
            {
                bootstrapLogger.LogCritical(configEx, "Failed to configure application services");
                throw;
            }

            var serviceProvider = services.BuildServiceProvider();
            AppLocator.Services = serviceProvider;

            BuildAvaloniaApp(serviceProvider).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            bootstrapLogger.LogCritical(ex, "Application terminated unexpectedly");
            throw;
        }
    }

    /// <summary>
    /// Avalonia configuration.
    /// </summary>
    /// <returns>The <see cref="AppBuilder"/>.</returns>
    /// <param name="serviceProvider">The application's dependency injection service provider.</param>
    /// <remarks>
    /// Don't remove; also used by visual designer.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp(IServiceProvider serviceProvider)
        => AppBuilder.Configure(() => new App(serviceProvider))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
