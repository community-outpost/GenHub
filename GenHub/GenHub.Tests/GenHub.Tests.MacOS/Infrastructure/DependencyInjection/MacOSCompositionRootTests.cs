using System.Runtime.Versioning;
using GenHub.MacOS.Infrastructure.DependencyInjection;
using GenHub.Tests.Shared;

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
}
