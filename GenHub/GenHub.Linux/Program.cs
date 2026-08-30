using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Avalonia;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Linux.Features.Shortcuts;
using GenHub.Linux.Infrastructure.DependencyInjection;
using GenHub.Linux.Infrastructure.SingleInstance;
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
    private static LinuxSingleInstanceManager? _singleInstanceManager;

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

        using var bootstrapLoggerFactory = LoggingModule.CreateBootstrapLoggerFactory();
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<Program>();

        bool multiInstance = args.Contains(CommandLineConstants.MultiInstanceArg, StringComparer.OrdinalIgnoreCase) ||
                             args.Contains(CommandLineConstants.MultiInstanceShortArg, StringComparer.OrdinalIgnoreCase) ||
                             Environment.GetEnvironmentVariable(CommandLineConstants.MultiInstanceEnvVar) == CommandLineConstants.MultiInstanceEnvEnabledValue;

        if (!multiInstance)
        {
            _singleInstanceManager = LinuxSingleInstanceManager.TryCreatePrimary(bootstrapLoggerFactory.CreateLogger<LinuxSingleInstanceManager>());
            if (_singleInstanceManager == null)
            {
                // Secondary instance: forward command to primary and exit
                LinuxSingleInstanceManager.SendCommandToPrimaryInstance(args, bootstrapLogger);
                return;
            }
        }

        // Register genhub:// protocol handler on Linux desktop
        LinuxUriSchemeRegistrar.Register(bootstrapLogger);

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
}