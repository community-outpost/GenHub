using System;
using System.IO;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Shared symbolic-link fixture support for tests that assert link-specific behavior.
/// Creating links requires privileges the test runner may not hold (Windows without developer
/// mode), so link-dependent tests skip gracefully when creation fails.
/// </summary>
public static class SymlinkTestHelper
{
    /// <summary>
    /// Attempts to create a file symbolic link, reporting failure instead of throwing when the
    /// process lacks the privilege.
    /// </summary>
    /// <param name="linkPath">The link to create.</param>
    /// <param name="targetPath">The file the link points at.</param>
    /// <returns>True when the link was created.</returns>
    public static bool TryCreateFileSymlink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
