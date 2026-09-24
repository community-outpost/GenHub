using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>Reports shell-fixture tests as skipped on hosts without a Unix shell.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="UnixFactAttribute"/> class.</summary>
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "This test uses a Unix shell fixture.";
        }
    }
}
