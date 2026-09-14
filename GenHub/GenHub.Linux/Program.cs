using System;
using System.IO;
using System.Runtime.Versioning;
using Avalonia;
using GenHub.Core.Constants;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Linux.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Velopack;

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
    [SupportedOSPlatform("linux")]
    public static void Main(string[] args)
    {
        // Initialize Velopack - must be first to handle install/update hooks
        VelopackApp.Build().Run();

        // Create lockfile to guarantee that only one instance is running on linux
        var lockFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StorageMigrationConstants.GenHubConfigDirectoryName, "lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockFilePath)!);
        FileStream? lockFile = null;
        try
        {
            lockFile = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            // Another instance is running
            return;
        }

        using (lockFile)
        using (var bootstrapLoggerFactory = LoggingModule.CreateBootstrapLoggerFactory())
        {
            var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<Program>();
            try
            {
                bootstrapLogger.LogInformation("Starting GenHub Linux application");

                // Initialize configured data-path resolver before checking conflict so AppDataPath is respected
                ConfigurationModule.InitializeConfiguredDataPathResolver();

                // Check for duplicate installation collision and adopt configuration early
                // before dependency injection initializes UserSettingsService.
                var registeredCustom = Features.Storage.LinuxInstallationTracker.GetRegisteredCustomInstallPathStatic(bootstrapLogger);
                Common.Services.StorageMigrationService.EarlyAdoptIfConflict(registeredCustom, bootstrapLogger);

                // Record custom installation location if running outside default root
                Features.Storage.LinuxInstallationTracker.RecordInstallLocationStatic(bootstrapLogger);

                var services = new ServiceCollection();
                services.ConfigureApplicationServices(platformServices => platformServices.AddLinuxServices());

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
