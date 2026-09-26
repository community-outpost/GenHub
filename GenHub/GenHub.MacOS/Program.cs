using Avalonia;
using GenHub.Core.Constants;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.MacOS.Infrastructure.AppActivation;
using GenHub.MacOS.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.Versioning;
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
            bootstrapLogger.LogInformation("Starting GenHub macOS application ({Version})", AppConstants.FullDisplayVersion);

            // Initialize configured data-path resolver before checking conflict so AppDataPath is respected
            ConfigurationModule.InitializeConfiguredDataPathResolver();

            // Check for duplicate installation collision and adopt configuration early
            // before dependency injection initializes UserSettingsService.
            var registeredCustom = Common.Services.FileInstallationLocationTracker.GetRegisteredCustomInstallPathStatic(bootstrapLogger);
            Common.Services.StorageMigrationService.EarlyAdoptIfConflict(registeredCustom, bootstrapLogger);

            // Record custom installation location if running outside default root
            Common.Services.FileInstallationLocationTracker.RecordInstallLocationStatic(bootstrapLogger);

            var services = new ServiceCollection();
            services.ConfigureApplicationServices(platformServices => platformServices.AddMacOSServices());

            using var serviceProvider = services.BuildServiceProvider();
            AppLocator.Services = serviceProvider;

            var activationLogger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(MacAppActivationGate).FullName!);
            BuildAvaloniaApp(serviceProvider)
                .AfterSetup(_ => MacAppActivationGate.Install(Environment.ProcessPath, activationLogger))
                .StartWithClassicDesktopLifetime(args);
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
