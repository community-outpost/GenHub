using System;
using System.Runtime.Versioning;
using Avalonia;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.MacOS.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Velopack;

namespace GenHub.MacOS;

/// <summary>
/// Main entry point for the macOS application.
/// </summary>
public static class Program
{
    /// <summary>
    /// Starts the GenHub macOS application.
    /// </summary>
    /// <param name="args">Application startup arguments.</param>
    [STAThread]
    [SupportedOSPlatform("macos")]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        using var bootstrapLoggerFactory = LoggingModule.CreateBootstrapLoggerFactory();
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger(typeof(Program).FullName!);

        try
        {
            bootstrapLogger.LogInformation("Starting GenHub macOS application");

            var services = new ServiceCollection();
            services.ConfigureApplicationServices(platformServices => platformServices.AddMacOSServices());

            using var serviceProvider = services.BuildServiceProvider();
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
    /// Configures the Avalonia application.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <returns>The configured Avalonia application builder.</returns>
    public static AppBuilder BuildAvaloniaApp(IServiceProvider serviceProvider)
        => AppBuilder.Configure(() => new App(serviceProvider))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
