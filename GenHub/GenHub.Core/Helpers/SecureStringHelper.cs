using System.Runtime.InteropServices;
using System.Security;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helpers for converting between plain strings and secure strings.
/// </summary>
public static class SecureStringHelper
{
    /// <summary>
    /// Creates a read-only secure string from a plain text value.
    /// </summary>
    /// <param name="value">The plain text value.</param>
    /// <returns>A read-only secure string.</returns>
    public static SecureString ToSecureString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var secure = new SecureString();
        foreach (var c in value)
        {
            secure.AppendChar(c);
        }

        secure.MakeReadOnly();
        return secure;
    }

    /// <summary>
    /// Copies a secure string into a plain text value.
    /// </summary>
    /// <param name="secureString">The secure string.</param>
    /// <returns>The plain text value.</returns>
    public static string ToUnsecureString(SecureString secureString)
    {
        ArgumentNullException.ThrowIfNull(secureString);
        var ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
        try
        {
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }
}
