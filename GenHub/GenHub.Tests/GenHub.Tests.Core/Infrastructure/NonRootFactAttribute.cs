using System.Runtime.InteropServices;

namespace GenHub.Tests.Core.Infrastructure;

/// <summary>Runs a test everywhere except a Unix run as root, where permission bits do not block file operations.</summary>
public sealed partial class NonRootFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="NonRootFactAttribute"/> class.</summary>
    public NonRootFactAttribute()
    {
        if (!OperatingSystem.IsWindows() && GetEffectiveUserId() == 0)
        {
            Skip = "Root bypasses Unix permission bits.";
        }
    }

    /// <summary>Reads the effective user ID, including when running through sudo.</summary>
    /// <returns>Zero for root, otherwise the effective user ID.</returns>
    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
}
