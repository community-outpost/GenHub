using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Infrastructure.DependencyInjection;
using GenHub.MacOS.GameInstallations;
using GenHub.MacOS.Infrastructure.DependencyInjection;
using GenHub.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.Versioning;
using Xunit;

namespace GenHub.Tests.MacOS.Infrastructure.DependencyInjection;

/// <summary>
/// Verifies the macOS host's real service container is complete.
/// </summary>
[SupportedOSPlatform("macos")]
[Collection(ApplicationCompositionCollection.Name)]
public class MacOSCompositionRootTests
{
    /// <summary>
    /// Builds the container exactly as <c>GenHub.MacOS.Program.Main</c> does and
    /// asserts every required service resolves, including the detector collection
    /// that would otherwise resolve empty and leave the app finding no games with no
    /// error shown.
    /// </summary>
    [Fact]
    public void MacOSHost_ResolvesEveryRequiredService()
    {
        CompositionRootAssertions.AssertHostContainerIsComplete(
            services => services.AddMacOSServices());
    }

    /// <summary>
    /// Verifies that the macOS host registers <see cref="MacOSInstallationSearchPathProvider"/>
    /// as the concrete implementation for <see cref="IInstallationSearchPathProvider"/>.
    /// </summary>
    [Fact]
    public void MacOSHost_RegistersMacOSInstallationSearchPathProvider()
    {
        using var testEnvironment = new TemporaryApplicationEnvironment();
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(platformServices => platformServices.AddMacOSServices());

        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<MacOSInstallationSearchPathProvider>(
            serviceProvider.GetRequiredService<IInstallationSearchPathProvider>());
    }
}
