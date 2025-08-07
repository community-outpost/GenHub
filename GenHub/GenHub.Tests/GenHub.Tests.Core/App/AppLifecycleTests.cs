using GenHub;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

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
            Activator.CreateInstance(Type.GetType("GenHub.App, GenHub") !, new object?[] { null! }));
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
            Activator.CreateInstance(Type.GetType("GenHub.App, GenHub") !, serviceProvider));
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
        var mockConfigService = new Mock<IUserSettingsService>();
        services.AddSingleton(mockConfigService.Object);
        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        var appType = Type.GetType("GenHub.App, GenHub") !;
        var app = Activator.CreateInstance(appType, serviceProvider);
        Assert.NotNull(app);
    }
}
