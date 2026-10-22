using System;
using System.IO;

namespace GenHub.Tests.Core.Features.UI;

/// <summary>
/// Helper utilities for locating solution and project assets during UI and static analysis tests.
/// </summary>
public static class UiTestPathHelper
{
    /// <summary>
    /// Traverses parent directories starting from the test assembly base directory or current directory
    /// to locate the root directory containing <c>GenHub.sln</c>.
    /// </summary>
    /// <returns>The absolute path to the directory containing <c>GenHub.sln</c>.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when <c>GenHub.sln</c> cannot be found in the directory hierarchy.</exception>
    public static string FindSolutionDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GenHub.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GenHub.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate directory containing GenHub.sln from: " + AppContext.BaseDirectory);
    }
}
