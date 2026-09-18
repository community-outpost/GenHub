using GenHub.Core.Constants;
using System;
using System.IO;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides utility methods for command line argument formatting and executable inspection.
/// </summary>
public static class CommandLineHelper
{
    /// <summary>
    /// Encloses a command line argument in double quotes and escapes any embedded double quotes
    /// if the argument contains spaces, tabs, or quotes.
    /// </summary>
    /// <param name="value">The argument string to quote if necessary.</param>
    /// <returns>The quoted argument, or the original string if quoting is not required.</returns>
    public static string QuoteArgument(string value)
    {
        if (value.Contains(' ') || value.Contains('\t') || value.Contains('"'))
        {
            return $"\"{value.Replace("\"", "\\\"")}\"";
        }

        return value;
    }

    /// <summary>
    /// Determines whether the specified executable path points to a Windows executable (.exe).
    /// </summary>
    /// <param name="executablePath">The executable path to inspect.</param>
    /// <returns><c>true</c> if the executable path has a Windows executable extension (.exe); otherwise, <c>false</c>.</returns>
    public static bool IsWindowsExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        return WineConstants.WindowsExecutableExtension.Equals(
            Path.GetExtension(executablePath),
            StringComparison.OrdinalIgnoreCase);
    }
}
