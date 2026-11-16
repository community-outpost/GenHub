using Avalonia;
using Avalonia.Controls.Platform;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Reflection;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(GenHub.Tests.Core.App.TestAppBuilder))]
[assembly: CollectionBehavior(CollectionBehavior.CollectionPerAssembly, DisableTestParallelization = true)]

namespace GenHub.Tests.Core.App;

/// <summary>
/// Configures the GenHub application for cross-platform headless lifecycle tests.
/// </summary>
internal static class TestAppBuilder
{
    private static readonly Mock<ILocalizationService> LocalizationServiceMock = new();
    private static readonly IServiceProvider ServiceProvider = CreateServiceProvider();
    private static readonly MethodInfo ResetDispatcher =
        typeof(Dispatcher).GetMethod("ResetForUnitTests", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Avalonia no longer exposes Dispatcher.ResetForUnitTests.");

    /// <summary>
    /// Gets the localization service registered in the headless application.
    /// </summary>
    internal static ILocalizationService LocalizationService => LocalizationServiceMock.Object;

    /// <summary>
    /// Creates the Avalonia application builder used by headless tests.
    /// </summary>
    /// <returns>The configured application builder.</returns>
    public static AppBuilder BuildAvaloniaApp() => BuildWithDispatcherProbe(null);

    /// <summary>Builds the headless app with an optional deterministic startup race probe.</summary>
    /// <param name="afterReset">Runs after resetting the dispatcher, before platform initialization.</param>
    /// <returns>The configured application builder.</returns>
    internal static AppBuilder BuildWithDispatcherProbe(Action? afterReset)
    {
        var builder = AppBuilder.Configure(() => new global::GenHub.App(ServiceProvider))
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
        var initializeHeadless = builder.WindowingSubsystemInitializer
            ?? throw new InvalidOperationException("The headless platform did not register a windowing subsystem.");

        return builder
            .UseWindowingSubsystem(
                () =>
                {
                    // Install a loop-capable implementation before exposing an empty singleton.
                    // Even a background reader in the reset/setup gap now uses the UI thread's loop.
                    RegisterControlledDispatcher();
                    DiscardStaleDispatcher();
                    afterReset?.Invoke();
                    _ = Dispatcher.UIThread;
                    initializeHeadless();
                },
                builder.WindowingSubsystemName!)
            .AfterPlatformServicesSetup(_ => VerifyHeadlessDispatcher());
    }

    /// <summary>
    /// Discards a UI dispatcher that leaked background work created after the session reset it.
    /// </summary>
    /// <remarks>
    /// The headless session resets the process-wide <see cref="Dispatcher.UIThread"/> before each test and the
    /// headless platform creates it again. Background work left over from an earlier test that reads
    /// <see cref="Dispatcher.UIThread"/> inside that gap creates a dispatcher without a run loop. The platform
    /// would then build its compositor and render context on that dispatcher, and the next awaited UI test fails in
    /// <see cref="Dispatcher.PushFrame"/> with <see cref="PlatformNotSupportedException"/>. Registering a controlled implementation before resetting ensures even a racing reader
    /// constructs a dispatcher bound to the headless thread before the compositor is built.
    /// </remarks>
    private static void DiscardStaleDispatcher() => ResetDispatcher.Invoke(null, null);

    private static void RegisterControlledDispatcher()
    {
        // Avalonia hides the locator in its reference assembly, like ResetForUnitTests above.
        var locator = typeof(AvaloniaLocator).GetProperty("CurrentMutable")?.GetValue(null)
            ?? throw new InvalidOperationException("Avalonia no longer exposes its mutable locator.");
        var bind = typeof(AvaloniaLocator).GetMethod("BindToSelf")
            ?? throw new InvalidOperationException("Avalonia no longer exposes BindToSelf.");
        bind.MakeGenericMethod(typeof(IDispatcherImpl)).Invoke(locator, [new ManagedDispatcherImpl(null)]);
    }

    private static void VerifyHeadlessDispatcher()
    {
        if (!Dispatcher.UIThread.SupportsRunLoops)
        {
            throw new InvalidOperationException(
                "Background work replaced the headless UI dispatcher while the headless platform initialized.");
        }
    }

    private static IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IUserSettingsService>());
        services.AddSingleton(Mock.Of<IConfigurationProviderService>());
        services.AddSingleton(LocalizationService);
        services.AddSingleton(Mock.Of<IProfileLauncherFacade>());

        return services.BuildServiceProvider();
    }
}
