using GenHub.Tests.Shared;
using GenHub.Windows.Infrastructure.DependencyInjection;

namespace GenHub.Tests.Windows.Infrastructure.DependencyInjection;

/// <summary>
/// Verifies the Windows host's real service container is complete.
/// </summary>
[Collection(ApplicationCompositionCollection.Name)]
public class WindowsCompositionRootTests
{
    /// <summary>
    /// Builds the container exactly as <c>GenHub.Windows.Program.Main</c> does and
    /// asserts every required service resolves.
    /// </summary>
    [Fact]
    public void WindowsHost_ResolvesEveryRequiredService()
    {
        CompositionRootAssertions.AssertHostContainerIsComplete(
            services => services.AddWindowsServices());
    }
}
