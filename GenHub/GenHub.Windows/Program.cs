using System;
using System.Linq;
using Avalonia;
using DotNetEnv;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Windows.Infrastructure.DependencyInjection;
using GenHub.Windows.Infrastructure.SingleInstance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Velopack;

namespace GenHub.Windows;

/// <summary>
/// Main class for main entry point.
/// </summary>
public class Program
{
    private static readonly TimeSpan UpdaterTimeout = TimeIntervals.UpdaterTimeout;
    private static SingleInstanceManager? _singleInstanceManager;

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
        // Initialize Velopack - must be first to handle install/update hooks
        VelopackApp.Build().Run();

        // Load environment variables from .env file if it exists
        Exception? envLoadException = null;
        try
        {
            Env.TraversePath().Load();
        }
        catch (Exception ex)
        {
            envLoadException = ex;
        }

        using var bootstrapLoggerFactory = LoggingModule.CreateBootstrapLoggerFactory();
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<Program>();

        if (envLoadException != null)
        {
            bootstrapLogger.LogWarning(envLoadException, "Failed to load environment variables from .env file");
        }

        if (TryForwardToExistingInstance(args, bootstrapLogger, bootstrapLoggerFactory))
        {
            return;
        }

        // Check for duplicate installation collision: adopt custom configuration early and preserve registry entry
        HandleEarlyInstallationConflict(bootstrapLogger);

        // Record custom installation location in registry if running outside default root
        Features.Storage.WindowsInstallationTracker.RecordInstallLocationStatic(bootstrapLogger);

        // Register the genhub:// URI scheme with Windows so clicked links open this executable.
        // Registered for primary instance only; idempotent and per-user (HKCU).
        Features.Shortcuts.UriSchemeRegistrar.Register(bootstrapLogger);

        try
        {
            bootstrapLogger.LogInformation("Starting GenHub Windows application");

            var services = new ServiceCollection();

            try
            {
                // Register shared services and Windows-specific services
                services.ConfigureApplicationServices(s => s.AddWindowsServices());
            }
            catch (Exception configEx)
            {
                throw new InvalidOperationException("Failed to configure application services", configEx);
            }

            using (_singleInstanceManager)
            {
                var serviceProvider = services.BuildServiceProvider();
                AppLocator.Services = serviceProvider;
                AppLocator.SingleInstanceManager = _singleInstanceManager;

                BuildAvaloniaApp(serviceProvider).StartWithClassicDesktopLifetime(args);
            }
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

    private static void HandleEarlyInstallationConflict(ILogger logger)
    {
        // Initialize configured data-path resolver before checking conflict so AppDataPath is respected
        ConfigurationModule.InitializeConfiguredDataPathResolver();

        // Check for duplicate installation collision: if running from default %LOCALAPPDATA%
        // but a custom installation was previously registered or recorded, adopt user configuration early
        // before dependency injection initializes UserSettingsService.
        var registeredCustom = Features.Storage.WindowsInstallationTracker.GetRegisteredCustomInstallPathStatic(logger);
        Common.Services.StorageMigrationService.EarlyAdoptIfConflict(registeredCustom, logger);

        // If a duplicate custom installation was discovered during conflict check or adoption,
        // persist it in Windows registry before URI scheme re-registration overwrites the open command.
        if (Common.Services.StorageMigrationService.HasDuplicateInstallationConflict(registeredCustom, out var detectedCustom) &&
            !string.IsNullOrWhiteSpace(detectedCustom))
        {
            Features.Storage.WindowsInstallationTracker.RecordCustomInstallPathStatic(detectedCustom, logger);
        }
    }

    private static bool TryForwardToExistingInstance(string[] args, ILogger bootstrapLogger, ILoggerFactory bootstrapLoggerFactory)
    {
        bool multiInstance = args.Contains(CommandLineConstants.MultiInstanceArg, StringComparer.OrdinalIgnoreCase) ||
                             args.Contains(CommandLineConstants.MultiInstanceShortArg, StringComparer.OrdinalIgnoreCase) ||
                             Environment.GetEnvironmentVariable(CommandLineConstants.MultiInstanceEnvVar) == CommandLineConstants.MultiInstanceEnvEnabledValue;

        if (multiInstance)
        {
            bootstrapLogger.LogInformation("Multi-instance mode enabled - skipping single-instance check");
            return false;
        }

        _singleInstanceManager = new SingleInstanceManager(bootstrapLoggerFactory.CreateLogger<SingleInstanceManager>());
        if (_singleInstanceManager.IsFirstInstance)
        {
            return false;
        }

        ForwardCommandLineCommands(args, bootstrapLogger);
        SingleInstanceManager.FocusPrimaryInstance();
        _singleInstanceManager.Dispose();
        return true;
    }

    private static void ForwardCommandLineCommands(string[] args, ILogger bootstrapLogger)
    {
        var profileId = CommandLineParser.ExtractProfileId(args);
        if (!string.IsNullOrEmpty(profileId))
        {
            bootstrapLogger.LogInformation("Forwarding launch-profile command to primary instance: {ProfileId}", profileId);
            SingleInstanceManager.SendCommandToPrimaryInstance($"{IpcCommands.LaunchProfilePrefix}{profileId}");
        }

        var subscriptionUrl = CommandLineParser.ExtractSubscriptionUrl(args);
        if (!string.IsNullOrEmpty(subscriptionUrl))
        {
            bootstrapLogger.LogInformation("Forwarding subscribe command to primary instance: {Url}", subscriptionUrl);
            SingleInstanceManager.SendCommandToPrimaryInstance($"{IpcCommands.SubscribePrefix}{subscriptionUrl}");
        }

        var profileShareUri = CommandLineParser.ExtractProfileShareUri(args);
        if (!string.IsNullOrEmpty(profileShareUri))
        {
            bootstrapLogger.LogInformation("Forwarding import-profile command to primary instance");
            SingleInstanceManager.SendCommandToPrimaryInstance($"{IpcCommands.ImportProfilePrefix}{profileShareUri}");
        }
    }
}
