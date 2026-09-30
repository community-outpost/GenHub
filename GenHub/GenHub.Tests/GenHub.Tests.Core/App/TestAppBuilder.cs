using Avalonia;
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
    private const string ResetForUnitTestsMethod = "ResetForUnitTests";

    private static readonly Mock<ILocalizationService> LocalizationServiceMock = new();
    private static readonly IServiceProvider ServiceProvider = CreateServiceProvider();

    /// <summary>
    /// Gets the localization service registered in the headless application.
    /// </summary>
    internal static ILocalizationService LocalizationService => LocalizationServiceMock.Object;

    /// <summary>
    /// Creates the Avalonia application builder used by headless tests.
    /// </summary>
    /// <returns>The configured application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure(() => new global::GenHub.App(ServiceProvider))
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .AfterPlatformServicesSetup(_ => EnsureHeadlessDispatcher());

    /// <summary>
    /// Replaces a UI dispatcher that a leaked background thread created before the headless platform registered its own.
    /// </summary>
    /// <remarks>
    /// The headless session resets the process-wide <see cref="Dispatcher.UIThread"/> before each test and lets the
    /// headless platform create it again. Background work left over from an earlier test that reads
    /// <see cref="Dispatcher.UIThread"/> inside that gap creates a dispatcher without a run loop, and the next awaited
    /// UI test then fails in <see cref="Dispatcher.PushFrame"/> with <see cref="PlatformNotSupportedException"/>.
    /// This runs on the session thread once the headless dispatcher is registered, so the test always starts on it.
    /// </remarks>
    private static void EnsureHeadlessDispatcher()
    {
        if (Dispatcher.UIThread.SupportsRunLoops)
        {
            return;
        }

        var reset = typeof(Dispatcher).GetMethod(ResetForUnitTestsMethod, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Avalonia no longer exposes {nameof(Dispatcher)}.{ResetForUnitTestsMethod}.");
        reset.Invoke(null, null);

        if (!Dispatcher.UIThread.SupportsRunLoops)
        {
            throw new InvalidOperationException("The headless UI dispatcher could not be restored.");
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
