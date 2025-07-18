using System;
using Avalonia;
using GenHub.Core;
using GenHub.Core.Interfaces.AppUpdate;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Linux.Features.AppUpdate;
using GenHub.Linux.GameInstallations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Linux;

/// <summary>
/// Main class for main entry point.
/// </summary>
public class Program
{
    private const string UpdaterUserAgent = "GenHub-Updater/1.0";
    private static readonly TimeSpan UpdaterTimeout = TimeSpan.FromMinutes(10);

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

            // Register shared services and pass in platform-specific registrations
            services.ConfigureApplicationServices(s =>
            {
                // Register Linux-specific services
                s.AddHttpClient<LinuxUpdateInstaller>(client =>
                {
                    client.Timeout = UpdaterTimeout;
                    client.DefaultRequestHeaders.Add("User-Agent", UpdaterUserAgent);
                });
                s.AddSingleton<IPlatformUpdateInstaller, LinuxUpdateInstaller>();
            });

            // Linux-specific DI
            services.AddSingleton<IGameInstallationDetector, LinuxInstallationDetector>();

            var serviceProvider = services.BuildServiceProvider();
            AppLocator.Services = serviceProvider;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
    /// <remarks>
    /// Don't remove; also used by visual designer.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
