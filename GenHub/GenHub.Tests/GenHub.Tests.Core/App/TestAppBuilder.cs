using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using Microsoft.Extensions.DependencyInjection;
using Moq;

[assembly: AvaloniaTestApplication(typeof(GenHub.Tests.Core.App.TestAppBuilder))]

namespace GenHub.Tests.Core.App;

/// <summary>
/// Configures the GenHub application for cross-platform headless lifecycle tests.
/// </summary>
internal static class TestAppBuilder
{
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
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

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
