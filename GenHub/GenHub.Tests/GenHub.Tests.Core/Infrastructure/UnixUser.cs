using System.Runtime.InteropServices;

namespace GenHub.Tests.Core.Infrastructure;

/// <summary>Reports the identity of the user running the tests on Unix.</summary>
internal static partial class UnixUser
{
    /// <summary>Gets a value indicating whether the tests run as root on Unix, including through sudo.</summary>
    internal static bool IsRoot => !OperatingSystem.IsWindows() && GetEffectiveUserId() == 0;

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetEffectiveUserId();
}
