using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>Reports tests that start Windows processes as skipped on other hosts.</summary>
public sealed class WindowsProcessFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="WindowsProcessFactAttribute"/> class.</summary>
    public WindowsProcessFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test starts a Windows process.";
        }
    }
}
