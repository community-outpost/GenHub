using System.Globalization;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.DependencyInjection;

/// <summary>
/// Tests localization dependency injection registration.
/// </summary>
[Collection(LocalizationCultureCollection.Name)]
public sealed class LocalizationModuleTests : IDisposable
{
    private readonly CultureInfo? _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
    private readonly CultureInfo _originalThreadUiCulture = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Restores process-wide UI culture defaults changed by localization resolution.
    /// </summary>
    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalThreadUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
    }

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
