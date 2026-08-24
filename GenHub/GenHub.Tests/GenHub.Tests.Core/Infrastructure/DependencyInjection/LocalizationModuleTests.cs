using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.DependencyInjection;

/// <summary>
/// Tests localization dependency injection registration.
/// </summary>
public sealed class LocalizationModuleTests
{
    /// <summary>
    /// Verifies that localization resolves as one shared service with embedded English resources.
    /// </summary>
    [Fact]
    public void AddLocalizationServices_RegistersSingletonWithDefaultResources()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalizationServices();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ILocalizationService>();
        var second = provider.GetRequiredService<ILocalizationService>();

        Assert.Same(first, second);
        Assert.Equal("en", first.CurrentCulture.Name);
        Assert.Equal("GenHub", first.GetString("App.Name"));
    }
}
