using System.Runtime.InteropServices;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>Runs permission-denial tests only where Unix permission bits are enforced.</summary>
public sealed partial class NonRootUnixFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="NonRootUnixFactAttribute"/> class.</summary>
    public NonRootUnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip = "This test requires Unix file permissions.";
        }
        else if (GetEffectiveUserId() == 0)
        {
            Skip = "Root bypasses Unix permission bits.";
        }
    }

    /// <summary>Reads the effective user ID, including when running through sudo.</summary>
    /// <returns>Zero for root, otherwise the effective user ID.</returns>
    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
}
