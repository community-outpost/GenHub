using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GenHub.Core.Utilities;

/// <summary>
/// Helper methods for checking process privileges.
/// </summary>
public static class PrivilegeHelpers
{
    private static readonly Lazy<bool> _isAdministrator = new(CheckAdministratorPrivileges);

    /// <summary>
    /// Gets a value indicating whether the current process is running as Administrator.
    /// </summary>
    public static bool IsAdministrator => _isAdministrator.Value;

    private static bool CheckAdministratorPrivileges()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
