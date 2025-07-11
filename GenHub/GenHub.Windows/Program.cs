using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Windows;

/// <summary>
/// Main class for main entry point.
/// </summary>
public class Program
{
    private const string MutexName = "Global\\GenHub";
    private const int SW_RESTORE = 9; // Windows API constant to restore a window
    private static Mutex? mutex;

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
        // Check if another instance is running and focus that instance instead of creating a new one.
        if (IsAnotherInstanceRunning())
        {
            FocusRunningInstance();

            // End this program if another instance already exists
            return;
        }

        using var bootstrapLoggerFactory = LoggingModule.CreateBootstrapLoggerFactory();
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger<Program>();

        try
        {
            bootstrapLogger.LogInformation("Starting GenHub Windows application");

            var services = new ServiceCollection();

            // Register shared services
            services.ConfigureApplicationServices();

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

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Checks if another instance is already running by attempting to acquire a named <see cref="Mutex" />.
    /// </summary>
    /// <returns>True if another instance already owns the <see cref="Mutex" />.</returns>
    private static bool IsAnotherInstanceRunning()
    {
        mutex = new Mutex(true, MutexName, out bool createdNew);
        return !createdNew;
    }

    /// <summary>
    /// Brings the main window of the current running instance to the foreground and restores it if minimized.
    /// </summary>
    private static void FocusRunningInstance()
    {
        // Find a process of the same name
        var process = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).FirstOrDefault();

        // If no process is found, we can't restore it
        if(process == null)
            return;

        // Restore the window if minimized and bring it to the foreground
        var windowHandle = process.MainWindowHandle;
        ShowWindow(windowHandle, SW_RESTORE);
        SetForegroundWindow(windowHandle);
    }
}
