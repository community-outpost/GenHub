using Avalonia.Data;
using Avalonia.Headless.XUnit;
using GenHub.Common.Markup;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace GenHub.Tests.Core.App;

/// <summary>
/// Unit tests for the <see cref="App"/> lifecycle and DI requirements.
/// </summary>
public class AppLifecycleTests
{
    /// <summary>
    /// Verifies that the <see cref="App"/> constructor throws if the service provider is null.
    /// </summary>
    [Fact]
    public void App_Constructor_RequiresServiceProvider()
    {
        // Act & Assert
        var ex = Assert.ThrowsAny<System.Reflection.TargetInvocationException>(() =>
            Activator.CreateInstance(Type.GetType("GenHub.App, GenHub")!, new object?[] { null! }));
        Assert.IsType<ArgumentNullException>(ex.InnerException);
    }

    /// <summary>
    /// Verifies that the <see cref="App"/> constructor throws if IUserSettingsService is not registered.
    /// </summary>
    [Fact]
    public void App_Constructor_RequiresUserSettingsService()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var ex = Assert.ThrowsAny<System.Reflection.TargetInvocationException>(() =>
            Activator.CreateInstance(Type.GetType("GenHub.App, GenHub")!, serviceProvider));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    /// <summary>
    /// Verifies that the <see cref="App"/> constructor does not throw when all required services are registered.
    /// </summary>
    [Fact]
    public void App_Constructor_WithValidServices_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        var mockUserSettingsService = new Mock<IUserSettingsService>();
        var mockConfigurationProvider = new Mock<IConfigurationProviderService>();
        var mockLocalizationService = new Mock<ILocalizationService>();
        var mockProfileLauncherFacade = new Mock<IProfileLauncherFacade>();

        services.AddSingleton(typeof(IUserSettingsService), mockUserSettingsService.Object);
        services.AddSingleton(typeof(IConfigurationProviderService), mockConfigurationProvider.Object);
        services.AddSingleton(typeof(ILocalizationService), mockLocalizationService.Object);
        services.AddSingleton(typeof(IProfileLauncherFacade), mockProfileLauncherFacade.Object);

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var appType = Type.GetType("GenHub.App, GenHub")!;
        var app = Activator.CreateInstance(appType, serviceProvider);
        Assert.NotNull(app);
    }

    /// <summary>
    /// Verifies that application XAML loading exposes localization to markup extensions afterward.
    /// </summary>
    [AvaloniaFact]
    public void App_Initialize_ExposesLocalizationServiceToMarkupExtensions()
    {
        var app = Assert.IsType<global::GenHub.App>(Avalonia.Application.Current);
        Assert.Same(
            TestAppBuilder.LocalizationService,
            app.Resources[LocalizationConstants.ResourceServiceKey]);

        var extension = new LocalizeExtension("App.Name");
        var binding = Assert.IsType<Binding>(extension.ProvideValue(Mock.Of<IServiceProvider>()));
        Assert.Same(TestAppBuilder.LocalizationService, binding.Source);
    }
}
