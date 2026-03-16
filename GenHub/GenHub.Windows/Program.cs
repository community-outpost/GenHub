using Avalonia;
using DotNetEnv;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Windows.Infrastructure.DependencyInjection;
using GenHub.Windows.Infrastructure.SingleInstance;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
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
        // Load environment variables (locally)
        try
        {
            Env.TraversePath().Load();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load environment variables: {ex}");
        }

        // Initialize Velopack - must be first to handle install/update hooks
        VelopackApp.Build().Run();

        using var bootstrapLoggerFactory = LoggingModule.CreateBootstrapLoggerFactory();
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<Program>();

        // Extract profile ID from args if present (for IPC forwarding)
        var profileId = CommandLineParser.ExtractProfileId(args);

        // Extract subscription URL from args if present (for IPC forwarding)
        var subscriptionUrl = CommandLineParser.ExtractSubscriptionUrl(args);

        // Check for multi-instance mode (useful for debugging with multiple instances)
        bool multiInstance = args.Contains("--multi-instance", StringComparer.OrdinalIgnoreCase) ||
                             args.Contains("-m", StringComparer.OrdinalIgnoreCase) ||
                             Environment.GetEnvironmentVariable("GENHUB_MULTI_INSTANCE") == "1";

        if (!multiInstance)
        {
            // Initialize single-instance manager
            _singleInstanceManager = new SingleInstanceManager(bootstrapLoggerFactory.CreateLogger<SingleInstanceManager>());

            if (!_singleInstanceManager.IsFirstInstance)
            {
                // Forward launch command to primary instance if we have a profile ID
                if (!string.IsNullOrEmpty(profileId))
                {
                    bootstrapLogger.LogInformation("Forwarding launch-profile command to primary instance: {ProfileId}", profileId);
                    SingleInstanceManager.SendCommandToPrimaryInstance($"{IpcCommands.LaunchProfilePrefix}{profileId}");
                }

                // Forward subscribe command to primary instance if we have a subscription URL
                if (!string.IsNullOrEmpty(subscriptionUrl))
                {
                    bootstrapLogger.LogInformation("Forwarding subscribe command to primary instance: {Url}", subscriptionUrl);
                    SingleInstanceManager.SendCommandToPrimaryInstance($"{IpcCommands.SubscribePrefix}{subscriptionUrl}");
                }

                // Focus the existing instance
                SingleInstanceManager.FocusPrimaryInstance();

                // Exit this secondary instance
                _singleInstanceManager.Dispose();
                return;
            }
        }
        else
        {
            bootstrapLogger.LogInformation("Multi-instance mode enabled - skipping single-instance check");
        }

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
                bootstrapLogger.LogCritical(configEx, "Failed to configure application services");
                throw;
            }

            var serviceProvider = services.BuildServiceProvider();
            AppLocator.Services = serviceProvider;

            // Store the single instance manager in the service locator for App to access
            AppLocator.SingleInstanceManager = _singleInstanceManager;

            BuildAvaloniaApp(serviceProvider).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            bootstrapLogger.LogCritical(ex, "Application terminated unexpectedly");
            throw;
        }
        finally
        {
            _singleInstanceManager?.Dispose();
        }
    }

    /// <summary>
    /// Avalonia configuration.
    /// </summary>
    /// <param name="serviceProvider">The application's dependency injection service provider.</param>
    /// <returns>The <see cref="AppBuilder"/>.</returns>
    /// <remarks>
    /// Don't remove; also used by visual designer.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp(IServiceProvider serviceProvider)
        => AppBuilder.Configure(() => new App(serviceProvider))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
