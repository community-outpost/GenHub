using GenHub.Core.Models.Providers;
using GenHub.Core.Services.Providers;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Content.Providers;

/// <summary>
/// Unit tests for <see cref="ProviderDefinitionLoader"/>.
/// </summary>
public class ProviderDefinitionLoaderTests : IDisposable
{
    private readonly Mock<ILogger<ProviderDefinitionLoader>> _loggerMock;
    private readonly string _testProvidersDirectory;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderDefinitionLoaderTests"/> class.
    /// </summary>
    public ProviderDefinitionLoaderTests()
    {
        _loggerMock = new Mock<ILogger<ProviderDefinitionLoader>>();
        _testProvidersDirectory = Path.Combine(Path.GetTempPath(), "GenHub.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testProvidersDirectory);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that LoadProvidersAsync loads all valid provider JSON files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task LoadProvidersAsync_LoadsValidProviders_Successfully()
    {
        // Arrange
        var provider1Json = @"{
            ""providerId"": ""test-provider-1"",
            ""publisherType"": ""test"",
            ""displayName"": ""Test Provider 1"",
            ""enabled"": true
        }";

        var provider2Json = @"{
            ""providerId"": ""test-provider-2"",
            ""publisherType"": ""test"",
            ""displayName"": ""Test Provider 2"",
            ""enabled"": true
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "test1.provider.json"),
            provider1Json);

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "test2.provider.json"),
            provider2Json);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);

        // Act
        var result = await loader.LoadProvidersAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Count());
        Assert.Contains(result.Data, p => p.ProviderId == "test-provider-1");
        Assert.Contains(result.Data, p => p.ProviderId == "test-provider-2");
    }

    /// <summary>
    /// Verifies that GetProvider returns the correct provider after loading.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetProvider_ReturnsCorrectProvider_AfterLoading()
    {
        // Arrange
        var providerJson = @"{
            ""providerId"": ""my-provider"",
            ""publisherType"": ""test"",
            ""displayName"": ""My Provider"",
            ""description"": ""Test description"",
            ""enabled"": true,
            ""endpoints"": {
                ""catalogUrl"": ""https://example.com/catalog"",
                ""websiteUrl"": ""https://example.com""
            }
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "my.provider.json"),
            providerJson);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);
        await loader.LoadProvidersAsync();

        // Act
        var provider = loader.GetProvider("my-provider");

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("my-provider", provider.ProviderId);
        Assert.Equal("My Provider", provider.DisplayName);
        Assert.Equal("Test description", provider.Description);
        Assert.Equal("https://example.com/catalog", provider.Endpoints.CatalogUrl);
        Assert.Equal("https://example.com", provider.Endpoints.WebsiteUrl);
    }

    /// <summary>
    /// Verifies that GetProvider auto-loads providers on first access.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetProvider_AutoLoadsProviders_WhenNotInitialized()
    {
        // Arrange
        var providerJson = @"{
            ""providerId"": ""auto-load-test"",
            ""publisherType"": ""test"",
            ""displayName"": ""Auto Load Test"",
            ""enabled"": true
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "auto.provider.json"),
            providerJson);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);

        // Act - call GetProvider without calling LoadProvidersAsync first
        var provider = loader.GetProvider("auto-load-test");

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("auto-load-test", provider.ProviderId);
    }

    /// <summary>
    /// Verifies that GetProvider returns null for non-existent provider.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetProvider_ReturnsNull_ForNonExistentProvider()
    {
        // Arrange
        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);
        await loader.LoadProvidersAsync();

        // Act
        var provider = loader.GetProvider("non-existent");

        // Assert
        Assert.Null(provider);
    }

    /// <summary>
    /// Verifies that GetProvider is case-insensitive.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetProvider_IsCaseInsensitive()
    {
        // Arrange
        var providerJson = @"{
            ""providerId"": ""Case-Sensitive-Test"",
            ""publisherType"": ""test"",
            ""displayName"": ""Case Test"",
            ""enabled"": true
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "case.provider.json"),
            providerJson);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);
        await loader.LoadProvidersAsync();

        // Act & Assert
        Assert.NotNull(loader.GetProvider("case-sensitive-test"));
        Assert.NotNull(loader.GetProvider("CASE-SENSITIVE-TEST"));
        Assert.NotNull(loader.GetProvider("Case-Sensitive-Test"));
    }

    /// <summary>
    /// Verifies that LoadProvidersAsync handles invalid JSON gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task LoadProvidersAsync_HandlesInvalidJson_Gracefully()
    {
        // Arrange
        var validJson = @"{
            ""providerId"": ""valid-provider"",
            ""publisherType"": ""test"",
            ""displayName"": ""Valid Provider"",
            ""enabled"": true
        }";

        var invalidJson = "{ this is not valid json";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "valid.provider.json"),
            validJson);

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "invalid.provider.json"),
            invalidJson);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);

        // Act
        var result = await loader.LoadProvidersAsync();

        // Assert - should still succeed and load the valid provider
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal("valid-provider", result.Data.First().ProviderId);
    }

    /// <summary>
    /// Verifies that LoadProvidersAsync handles missing providerId gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task LoadProvidersAsync_HandlesMissingProviderId_Gracefully()
    {
        // Arrange
        var validJson = @"{
            ""providerId"": ""valid-provider"",
            ""publisherType"": ""test"",
            ""displayName"": ""Valid Provider"",
            ""enabled"": true
        }";

        var missingIdJson = @"{
            ""publisherType"": ""test"",
            ""displayName"": ""Missing ID Provider"",
            ""enabled"": true
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "valid.provider.json"),
            validJson);

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "missing-id.provider.json"),
            missingIdJson);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);

        // Act
        var result = await loader.LoadProvidersAsync();

        // Assert - should still succeed and load the valid provider
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal("valid-provider", result.Data.First().ProviderId);
    }

    /// <summary>
    /// Verifies that ReloadProvidersAsync clears and reloads all providers.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ReloadProvidersAsync_ClearsAndReloads_Successfully()
    {
        // Arrange
        var initialJson = @"{
            ""providerId"": ""initial-provider"",
            ""publisherType"": ""test"",
            ""displayName"": ""Initial Provider"",
            ""enabled"": true
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "initial.provider.json"),
            initialJson);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);
        await loader.LoadProvidersAsync();

        // Add a new provider file
        var newJson = @"{
            ""providerId"": ""new-provider"",
            ""publisherType"": ""test"",
            ""displayName"": ""New Provider"",
            ""enabled"": true
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "new.provider.json"),
            newJson);

        // Act
        var result = await loader.ReloadProvidersAsync();

        // Assert
        Assert.True(result.Success);
        var allProviders = loader.GetAllProviders().ToList();
        Assert.Equal(2, allProviders.Count);
        Assert.Contains(allProviders, p => p.ProviderId == "initial-provider");
        Assert.Contains(allProviders, p => p.ProviderId == "new-provider");
    }

    /// <summary>
    /// Verifies that AddCustomProvider adds a provider correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task AddCustomProvider_AddsProvider_Successfully()
    {
        // Arrange
        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);
        await loader.LoadProvidersAsync();

        var customProvider = new ProviderDefinition
        {
            ProviderId = "custom-provider",
            PublisherType = "custom",
            DisplayName = "Custom Provider",
            Enabled = true,
        };

        // Act
        var result = loader.AddCustomProvider(customProvider);

        // Assert
        Assert.True(result.Success);
        var retrieved = loader.GetProvider("custom-provider");
        Assert.NotNull(retrieved);
        Assert.Equal("Custom Provider", retrieved.DisplayName);
    }

    /// <summary>
    /// Verifies that RemoveCustomProvider removes a provider correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RemoveCustomProvider_RemovesProvider_Successfully()
    {
        // Arrange
        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);
        await loader.LoadProvidersAsync();

        var customProvider = new ProviderDefinition
        {
            ProviderId = "removable-provider",
            PublisherType = "custom",
            DisplayName = "Removable Provider",
            Enabled = true,
        };

        loader.AddCustomProvider(customProvider);
        Assert.NotNull(loader.GetProvider("removable-provider"));

        // Act
        var result = loader.RemoveCustomProvider("removable-provider");

        // Assert
        Assert.True(result.Success);
        Assert.Null(loader.GetProvider("removable-provider"));
    }

    /// <summary>
    /// Verifies that GetAllProviders returns only enabled providers.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetAllProviders_ReturnsOnlyEnabledProviders()
    {
        // Arrange
        var enabledJson = @"{
            ""providerId"": ""enabled-provider"",
            ""publisherType"": ""test"",
            ""displayName"": ""Enabled Provider"",
            ""enabled"": true
        }";

        var disabledJson = @"{
            ""providerId"": ""disabled-provider"",
            ""publisherType"": ""test"",
            ""displayName"": ""Disabled Provider"",
            ""enabled"": false
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "enabled.provider.json"),
            enabledJson);

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "disabled.provider.json"),
            disabledJson);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);
        await loader.LoadProvidersAsync();

        // Act
        var enabledProviders = loader.GetAllProviders().ToList();

        // Assert
        Assert.Single(enabledProviders);
        Assert.Equal("enabled-provider", enabledProviders.First().ProviderId);
    }

    /// <summary>
    /// Verifies that GetProvidersByType returns correctly filtered providers.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetProvidersByType_ReturnsCorrectlyFilteredProviders()
    {
        // Arrange
        var staticJson = @"{
            ""providerId"": ""static-provider"",
            ""publisherType"": ""test"",
            ""displayName"": ""Static Provider"",
            ""providerType"": ""Static"",
            ""enabled"": true
        }";

        var dynamicJson = @"{
            ""providerId"": ""dynamic-provider"",
            ""publisherType"": ""test"",
            ""displayName"": ""Dynamic Provider"",
            ""providerType"": ""Dynamic"",
            ""enabled"": true
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "static.provider.json"),
            staticJson);

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "dynamic.provider.json"),
            dynamicJson);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);
        await loader.LoadProvidersAsync();

        // Act
        var staticProviders = loader.GetProvidersByType(ProviderType.Static).ToList();
        var dynamicProviders = loader.GetProvidersByType(ProviderType.Dynamic).ToList();

        // Assert
        Assert.Single(staticProviders);
        Assert.Equal("static-provider", staticProviders.First().ProviderId);

        Assert.Single(dynamicProviders);
        Assert.Equal("dynamic-provider", dynamicProviders.First().ProviderId);
    }

    /// <summary>
    /// Verifies that endpoints with custom values are correctly parsed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task LoadProvidersAsync_ParsesCustomEndpoints_Correctly()
    {
        // Arrange
        var providerJson = @"{
            ""providerId"": ""custom-endpoints-test"",
            ""publisherType"": ""test"",
            ""displayName"": ""Custom Endpoints Test"",
            ""enabled"": true,
            ""endpoints"": {
                ""catalogUrl"": ""https://example.com/catalog"",
                ""websiteUrl"": ""https://example.com"",
                ""custom"": {
                    ""patchPageUrl"": ""https://example.com/patch"",
                    ""mirrorUrl"": ""https://mirror.example.com""
                }
            }
        }";

        await File.WriteAllTextAsync(
            Path.Combine(_testProvidersDirectory, "custom.provider.json"),
            providerJson);

        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);
        await loader.LoadProvidersAsync();

        // Act
        var provider = loader.GetProvider("custom-endpoints-test");

        // Assert
        Assert.NotNull(provider);
        Assert.Equal("https://example.com/catalog", provider.Endpoints.CatalogUrl);
        Assert.Equal("https://example.com", provider.Endpoints.WebsiteUrl);
        Assert.Equal("https://example.com/patch", provider.Endpoints.GetEndpoint("patchPageUrl"));
        Assert.Equal("https://mirror.example.com", provider.Endpoints.GetEndpoint("mirrorUrl"));
    }

    /// <summary>
    /// Verifies that LoadProvidersAsync handles empty directory gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task LoadProvidersAsync_HandlesEmptyDirectory_Gracefully()
    {
        // Arrange - directory is already empty
        var loader = new ProviderDefinitionLoader(_loggerMock.Object, _testProvidersDirectory);

        // Act
        var result = await loader.LoadProvidersAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// Verifies that LoadProvidersAsync handles non-existent directory gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task LoadProvidersAsync_HandlesNonExistentDirectory_Gracefully()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "GenHub.Tests", "NonExistent", Guid.NewGuid().ToString());
        var loader = new ProviderDefinitionLoader(_loggerMock.Object, nonExistentPath);

        // Act
        var result = await loader.LoadProvidersAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// Releases resources used by the test class.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Clean up test directory
            try
            {
                if (Directory.Exists(_testProvidersDirectory))
                {
                    Directory.Delete(_testProvidersDirectory, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        _disposed = true;
    }
}