using GenHub.Tests.Core.Infrastructure;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>Runs permission-denial tests only where Unix permission bits are enforced.</summary>
public sealed class NonRootUnixFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="NonRootUnixFactAttribute"/> class.</summary>
    public NonRootUnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "This test requires Unix file permissions.";
        }
        else if (UnixUser.IsRoot)
        {
            Skip = "Root bypasses Unix permission bits.";
        }
    }
}
