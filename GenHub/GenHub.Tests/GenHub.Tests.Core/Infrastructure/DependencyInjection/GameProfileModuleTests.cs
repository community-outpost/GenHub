using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace GenHub.Tests.Core.Infrastructure.DependencyInjection;

/// <summary>
/// Tests for <see cref="GameProfileModule"/>.
/// </summary>
public class GameProfileModuleTests
{
    /// <summary>
    /// Tests that AddGameProfileServices registers all expected services.
    /// </summary>
    [Fact]
    public void AddGameProfileServices_ShouldRegisterAllExpectedServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configProviderMock = new Mock<IConfigurationProviderService>();
        var tempDir = Path.GetTempPath();

        configProviderMock.Setup(x => x.GetWorkspacePath()).Returns(tempDir);
        configProviderMock.Setup(x => x.GetContentStoragePath()).Returns(Path.Combine(tempDir, "Content"));

        // Add required dependencies
        services.AddLogging();
        services.AddSingleton<IConfigurationProviderService>(configProviderMock.Object);

        // Mock missing dependencies
        services.AddScoped(provider => new Mock<IGameInstallationService>().Object);
        services.AddScoped(provider => new Mock<IContentManifestPool>().Object);
        services.AddScoped(provider => new Mock<IContentOrchestrator>().Object);
        services.AddScoped(provider => new Mock<IWorkspaceManager>().Object);
        services.AddScoped(provider => new Mock<ILaunchRegistry>().Object);

        // Act
        services.AddGameProfileServices();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(serviceProvider.GetService<IGameProfileRepository>());
        Assert.NotNull(serviceProvider.GetService<IGameProfileManager>());
        Assert.NotNull(serviceProvider.GetService<IGameProcessManager>());

        // Note: Facades require additional dependencies, tested separately
    }

    /// <summary>
    /// Tests that AddLaunchingServices registers all expected services.
    /// </summary>
    [Fact]
    public void AddLaunchingServices_ShouldRegisterAllExpectedServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configProviderMock = new Mock<IConfigurationProviderService>();

        // Add required dependencies
        services.AddLogging();
        services.AddSingleton<IConfigurationProviderService>(configProviderMock.Object);

        // Mock dependencies required for manifest services
        services.AddSingleton<IManifestIdService>(new GenHub.Core.Models.Manifest.ManifestIdService());
        services.AddSingleton<IManifestCache>(new Mock<IManifestCache>().Object);

        // Add manifest services (required for GameLauncher)
        services.AddManifestServices();

        // Mock remaining dependencies for GameLauncher
        services.AddSingleton(provider => new Mock<IGameProfileManager>().Object);
        services.AddSingleton(provider => new Mock<IWorkspaceManager>().Object);
        services.AddSingleton(provider => new Mock<IGameProcessManager>().Object);
        services.AddSingleton(provider => new Mock<IContentManifestPool>().Object);
        services.AddSingleton(provider => new Mock<IGameInstallationService>().Object);

        // Act
        services.AddLaunchingServices();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(serviceProvider.GetService<ILaunchRegistry>());

        // Note: GameLauncher requires many dependencies, tested separately
    }

    /// <summary>
    /// Tests that GameProfileRepository is registered as singleton.
    /// </summary>
    [Fact]
    public void AddGameProfileServices_GameProfileRepository_ShouldBeSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var configProviderMock = new Mock<IConfigurationProviderService>();
        var tempDir = Path.GetTempPath();

        configProviderMock.Setup(x => x.GetWorkspacePath()).Returns(tempDir);
        configProviderMock.Setup(x => x.GetContentStoragePath()).Returns(Path.Combine(tempDir, "Content"));

        services.AddLogging();
        services.AddSingleton<IConfigurationProviderService>(configProviderMock.Object);

        // Mock missing dependencies
        services.AddScoped(provider => new Mock<IGameInstallationService>().Object);
        services.AddScoped(provider => new Mock<IContentManifestPool>().Object);
        services.AddScoped(provider => new Mock<IContentOrchestrator>().Object);
        services.AddScoped(provider => new Mock<IWorkspaceManager>().Object);
        services.AddScoped(provider => new Mock<ILaunchRegistry>().Object);

        // Act
        services.AddGameProfileServices();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var instance1 = serviceProvider.GetService<IGameProfileRepository>();
        var instance2 = serviceProvider.GetService<IGameProfileRepository>();
        Assert.Same(instance1, instance2);
    }

    /// <summary>
    /// Tests that LaunchRegistry is registered as singleton.
    /// </summary>
    [Fact]
    public void AddLaunchingServices_LaunchRegistry_ShouldBeSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var configProviderMock = new Mock<IConfigurationProviderService>();

        services.AddLogging();

        // Act
        services.AddLaunchingServices();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var instance1 = serviceProvider.GetService<ILaunchRegistry>();
        var instance2 = serviceProvider.GetService<ILaunchRegistry>();
        Assert.Same(instance1, instance2);
    }

    /// <summary>
    /// Tests that GameProfileManager is registered as scoped.
    /// </summary>
    [Fact]
    public void AddGameProfileServices_GameProfileManager_ShouldBeScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        var configProviderMock = new Mock<IConfigurationProviderService>();
        var tempDir = Path.GetTempPath();

        configProviderMock.Setup(x => x.GetWorkspacePath()).Returns(tempDir);
        configProviderMock.Setup(x => x.GetContentStoragePath()).Returns(Path.Combine(tempDir, "Content"));

        // Add required dependencies
        services.AddLogging();
        services.AddSingleton<IConfigurationProviderService>(configProviderMock.Object);
        services.AddScoped(provider => new Mock<IGameInstallationService>().Object);
        services.AddScoped(provider => new Mock<IContentManifestPool>().Object);
        services.AddScoped(provider => new Mock<IContentOrchestrator>().Object);
        services.AddScoped(provider => new Mock<IWorkspaceManager>().Object);
        services.AddScoped(provider => new Mock<ILaunchRegistry>().Object);

        // Act
        services.AddGameProfileServices();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        using var scope1 = serviceProvider.CreateScope();
        using var scope2 = serviceProvider.CreateScope();

        var instance1 = scope1.ServiceProvider.GetService<IGameProfileManager>();
        var instance2 = scope2.ServiceProvider.GetService<IGameProfileManager>();
        Assert.NotSame(instance1, instance2);

        var sameScope1 = scope1.ServiceProvider.GetService<IGameProfileManager>();
        var sameScope2 = scope1.ServiceProvider.GetService<IGameProfileManager>();
        Assert.Same(sameScope1, sameScope2);
    }

    /// <summary>
    /// Tests that profiles directory is created if it doesn't exist.
    /// </summary>
    [Fact]
    public void AddGameProfileServices_ShouldCreateProfilesDirectory()
    {
        // Arrange
        var services = new ServiceCollection();
        var configProviderMock = new Mock<IConfigurationProviderService>();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        configProviderMock.Setup(x => x.GetWorkspacePath()).Returns(tempDir);
        configProviderMock.Setup(x => x.GetContentStoragePath()).Returns(Path.Combine(tempDir, "Content"));

        services.AddLogging();
        services.AddSingleton<IConfigurationProviderService>(configProviderMock.Object);

        try
        {
            // Act
            services.AddGameProfileServices();
            var serviceProvider = services.BuildServiceProvider();

            // Force service creation to trigger directory creation
            var repository = serviceProvider.GetService<IGameProfileRepository>();

            // Assert
            var expectedProfilesDir = Path.Combine(tempDir, "Profiles");
            Assert.True(Directory.Exists(expectedProfilesDir));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Tests that ProfileLauncherFacade is registered as singleton.
    /// </summary>
    [Fact]
    public void AddGameProfileServices_ProfileLauncherFacade_ShouldBeSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var configProviderMock = new Mock<IConfigurationProviderService>();
        var tempDir = Path.GetTempPath();

        configProviderMock.Setup(x => x.GetWorkspacePath()).Returns(tempDir);
        configProviderMock.Setup(x => x.GetContentStoragePath()).Returns(Path.Combine(tempDir, "Content"));

        services.AddLogging();
        services.AddSingleton<IConfigurationProviderService>(configProviderMock.Object);

        // Mock missing dependencies
        services.AddScoped(provider => new Mock<IGameInstallationService>().Object);
        services.AddScoped(provider => new Mock<IContentManifestPool>().Object);
        services.AddScoped(provider => new Mock<IContentOrchestrator>().Object);
        services.AddScoped(provider => new Mock<IWorkspaceManager>().Object);
        services.AddScoped(provider => new Mock<IGameProcessManager>().Object);
        services.AddSingleton<IGameLauncher>(new Mock<IGameLauncher>().Object);
        services.AddSingleton<ILaunchRegistry>(new Mock<ILaunchRegistry>().Object);

        // Act
        services.AddGameProfileServices();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var instance1 = serviceProvider.GetService<IProfileLauncherFacade>();
        var instance2 = serviceProvider.GetService<IProfileLauncherFacade>();
        Assert.Same(instance1, instance2);
    }

    /// <summary>
    /// Tests that ProfileEditorFacade is registered as singleton.
    /// </summary>
    [Fact]
    public void AddGameProfileServices_ProfileEditorFacade_ShouldBeSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var configProviderMock = new Mock<IConfigurationProviderService>();
        var tempDir = Path.GetTempPath();

        configProviderMock.Setup(x => x.GetWorkspacePath()).Returns(tempDir);
        configProviderMock.Setup(x => x.GetContentStoragePath()).Returns(Path.Combine(tempDir, "Content"));

        services.AddLogging();
        services.AddSingleton<IConfigurationProviderService>(configProviderMock.Object);

        // Mock missing dependencies
        services.AddScoped(provider => new Mock<IGameInstallationService>().Object);
        services.AddScoped(provider => new Mock<IContentManifestPool>().Object);
        services.AddScoped(provider => new Mock<IContentOrchestrator>().Object);
        services.AddScoped(provider => new Mock<IWorkspaceManager>().Object);
        services.AddScoped(provider => new Mock<ILaunchRegistry>().Object);

        // Act
        services.AddGameProfileServices();
        var serviceProvider = services.BuildServiceProvider();

        // Assert
        var instance1 = serviceProvider.GetService<IProfileEditorFacade>();
        var instance2 = serviceProvider.GetService<IProfileEditorFacade>();
        Assert.Same(instance1, instance2);
    }
}