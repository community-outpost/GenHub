using GenHub.Linux.Infrastructure.DependencyInjection;
using GenHub.Tests.Shared;

namespace GenHub.Tests.Linux.Infrastructure.DependencyInjection;

/// <summary>
/// Verifies the Linux host's real service container is complete.
/// </summary>
public class LinuxCompositionRootTests
{
    /// <summary>
    /// Builds the container exactly as <c>GenHub.Linux.Program.Main</c> does and asserts
    /// every required service resolves.
    /// </summary>
    [Fact]
    public void LinuxHost_ResolvesEveryRequiredService()
    {
        CompositionRootAssertions.AssertHostContainerIsComplete(
            services => services.AddLinuxServices());
    }
}
