using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GenHub.Core.Utilities;

/// <summary>
/// Helper methods for checking process privileges.
/// </summary>
public static class PrivilegeHelpers
{
    private static bool? _isAdministrator;

    /// <summary>
    /// Gets a value indicating whether the current process is running as Administrator.
    /// </summary>
    public static bool IsAdministrator
    {
        get
        {
            if (_isAdministrator.HasValue)
            {
                return _isAdministrator.Value;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                _isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            else
            {
                // On non-Windows platforms, we assume false for now or implement specific checks if needed.
                // For this specific issue (Windows UIPI), we only care about Windows Admin.
                _isAdministrator = false;
            }

            return _isAdministrator.Value;
        }
    }
}
