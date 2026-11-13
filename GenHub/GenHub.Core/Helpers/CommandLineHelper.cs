using GenHub.Core.Constants;
using System;
using System.IO;
using System.Text;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides utility methods for command line argument formatting and executable inspection.
/// </summary>
public static class CommandLineHelper
{
    /// <summary>
    /// Encloses a command line argument in double quotes and escapes embedded quotes and backslashes
    /// according to CommandLineToArgvW rules if the argument contains spaces, tabs, or quotes,
    /// or is empty. Trailing backslashes before the closing quote are doubled so they are not parsed
    /// as escaping the closing delimiter.
    /// </summary>
    /// <param name="value">The argument string to quote if necessary.</param>
    /// <returns>The quoted argument, or the original string if quoting is not required.</returns>
    public static string QuoteArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (!NeedsQuoting(value))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length + 16);
        sb.Append('"');

        int i = 0;
        while (i < value.Length)
        {
            char c = value[i++];
            if (c == '\\')
            {
                AppendBackslashSequence(sb, value, ref i);
            }
            else if (c == '"')
            {
                sb.Append('\\').Append('"');
            }
            else
            {
                sb.Append(c);
            }
        }

        sb.Append('"');
        return sb.ToString();
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

    private static bool NeedsQuoting(string value) =>
        value.Contains(' ') || value.Contains('\t') || value.Contains('"');

    private static void AppendBackslashSequence(StringBuilder sb, string value, ref int index)
    {
        int backslashCount = 1;
        while (index < value.Length && value[index] == '\\')
        {
            index++;
            backslashCount++;
        }

        if (index == value.Length)
        {
            // Trailing backslashes before the closing quote must be doubled (2N)
            sb.Append('\\', backslashCount * 2);
        }
        else if (value[index] == '"')
        {
            // Backslashes preceding a quote must be 2N + 1
            sb.Append('\\', (backslashCount * 2) + 1);
            sb.Append('"');
            index++;
        }
        else
        {
            // Backslashes followed by normal characters are emitted as-is
            sb.Append('\\', backslashCount);
        }
    }
}
