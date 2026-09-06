using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Regression coverage for dependency-injection registration of custom tab providers.
/// </summary>
public sealed class TabProviderRegistryTests
{
    /// <summary>
    /// Verifies providers registered with DI are available as soon as the registry is resolved.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ResolveRegistry_RegistersAllTabProvidersAndReturnsTheirTabsAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITabProvider, TestTabProvider>();
        services.AddSingleton<ITabProviderRegistry, TabProviderRegistry>();

        await using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<ITabProviderRegistry>();

        var providers = registry.GetAllProviders();
        var tabs = await registry.GetTabsForContentAsync(new ContentSearchResult
        {
            Id = "test-content",
            ProviderName = "Test publisher",
        });

        Assert.Single(providers);
        Assert.Equal("test-tabs", providers[0].ProviderId);
        Assert.Single(tabs);
        Assert.Equal("Test publisher tab", tabs[0].Header);
    }

    private sealed class TestTabProvider : ITabProvider
    {
        /// <inheritdoc />
        public string ProviderId => "test-tabs";

        /// <inheritdoc />
        public bool CanProvideTabsFor(ContentSearchResult searchResult) => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CustomTabDefinition>> GetTabsAsync(
            ContentSearchResult searchResult,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CustomTabDefinition> tabs =
            [
                new CustomTabDefinition
                {
                    TabId = "test-publisher-tab",
                    Header = "Test publisher tab",
                },
            ];

            return Task.FromResult(tabs);
        }
    }
}
