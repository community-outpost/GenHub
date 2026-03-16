using System;
using System.IO;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Storage;
using GenHub.Features.Content.Services.Reconciliation;
using GenHub.Features.Storage.Services;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.DependencyInjection;

/// <summary>
/// Tests for GeneralsOnline dependency injection.
/// </summary>
public class GeneralsOnlineDiTests
{
    /// <summary>
    /// Verifies that all Generals Online services are correctly registered and can be resolved.
    /// </summary>
    [Fact]
    public void GeneralsOnlineServices_ShouldBeResolvable()
    {
        // Arrange
        var testTempPath = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(testTempPath);

        try
        {
            var services = new ServiceCollection();

            // Register core dependencies required by ContentPipelineModule
            services.AddLogging();
            services.AddMemoryCache();
            services.AddHttpClient();

            // Register the module under test FIRST, so we can overwrite dependencies with Mocks
            services.AddContentPipelineServices();

            // Mock configuration services
            var configMock = new Mock<IConfigurationProviderService>();
            configMock.Setup(x => x.GetApplicationDataPath()).Returns(testTempPath);
            services.AddSingleton(configMock.Object);

            var appConfigMock = new Mock<IAppConfiguration>();
            appConfigMock.Setup(x => x.GetConfiguredDataPath()).Returns(testTempPath);
            services.AddSingleton(appConfigMock.Object);

            // Mock storage services
            var casOptionsMock = new Mock<Microsoft.Extensions.Options.IOptions<CasConfiguration>>();
            casOptionsMock.Setup(x => x.Value).Returns(new CasConfiguration { CasRootPath = testTempPath });
            services.AddSingleton(casOptionsMock.Object);

            services.AddSingleton(new Mock<ICasService>().Object);
            services.AddSingleton(new Mock<ICasStorage>().Object);
            services.AddSingleton(new Mock<ICasReferenceTracker>().Object);
            services.AddSingleton(new Mock<IContentStorageService>().Object);
            services.AddSingleton(new Mock<IContentManifestPool>().Object);

            // Mock reconciliation services
            services.AddSingleton(new Mock<IContentReconciliationService>().Object);
            services.AddSingleton(new Mock<IContentReconciliationOrchestrator>().Object);
            services.AddSingleton(new Mock<ICasLifecycleManager>().Object);
            services.AddSingleton(new Mock<IReconciliationAuditLog>().Object);

            // Mock other dependencies
            services.AddSingleton(new Mock<IProviderDefinitionLoader>().Object);
            services.AddSingleton(new Mock<ICatalogParserFactory>().Object);
            services.AddSingleton(new Mock<ICatalogParser>().Object);
            services.AddSingleton(new Mock<IDynamicContentCache>().Object);
            services.AddSingleton(new Mock<Octokit.IGitHubClient>().Object);
            services.AddSingleton(new Mock<IGitHubApiClient>().Object);
            services.AddSingleton(new Mock<IContentOrchestrator>().Object);
            services.AddSingleton(new Mock<IFileHashProvider>().Object);
            services.AddSingleton(new Mock<IStreamHashProvider>().Object);

            // Mock UI services
            services.AddSingleton(new Mock<INotificationService>().Object);
            services.AddSingleton(new Mock<IDialogService>().Object);
            services.AddSingleton(new Mock<IUserSettingsService>().Object);
            services.AddSingleton(new Mock<IGameProfileManager>().Object);

            using var serviceProvider = services.BuildServiceProvider();

            // Act & Assert
            // 1. Resolve Reconciler (Consumers) - This failed with InvalidOperationException before fix
            // Create a scope because these should be Scoped services
            using var scope = serviceProvider.CreateScope();

            var reconciler = scope.ServiceProvider.GetService<IGeneralsOnlineProfileReconciler>();
            Assert.NotNull(reconciler);

            // 2. Resolve UpdateService via Interface - This caused the specific exception
            var updateService = scope.ServiceProvider.GetService<IGeneralsOnlineUpdateService>();
            Assert.NotNull(updateService);

            // 3. Verify it's the correct type
            Assert.IsType<GenHub.Features.Content.Services.GeneralsOnline.GeneralsOnlineUpdateService>(updateService);
        }
        finally
        {
            if (Directory.Exists(testTempPath))
            {
                try
                {
                    Directory.Delete(testTempPath, true);
                }
                catch
                {
                    /* Ignore cleanup errors */
                }
            }
        }
    }
}
