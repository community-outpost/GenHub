using System.Linq;
using System.Runtime.InteropServices;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Locates the local native client these integration tests launch.
/// </summary>
/// <remarks>
/// Shared by every test that spawns the real engine, so discovery stays in one place: all
/// of them skip unless a client is present, and diverging on how it is found would make
/// some skip while others fail.
/// </remarks>
public static class NativeClientFixture
{
    /// <summary>Environment variable overriding the discovered directory.</summary>
    public const string EnvironmentOverride = "GENHUB_NATIVE_CLIENT_DIR";

    /// <summary>The engine executable's filename.</summary>
    public const string BinaryName = "generalszh";

    /// <summary>
    /// Gets the native client directory, or <c>null</c> when these tests should skip.
    /// </summary>
    public static string? Directory
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            var configured = Environment.GetEnvironmentVariable(EnvironmentOverride);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                // Validated the same way as the discovered default below. A directory that
                // exists but holds no engine binary is a misconfigured override, and these
                // tests should skip rather than fail on it.
                return File.Exists(Path.Combine(configured, BinaryName)) ? configured : null;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var defaultDirectory = Path.Combine(home, "TheSuperHackers", "GeneralsZH");

            return File.Exists(Path.Combine(defaultDirectory, BinaryName)) ? defaultDirectory : null;
        }
    }

    /// <summary>
    /// Determines whether a filename is one of the engine's dynamic libraries.
    /// </summary>
    /// <remarks>
    /// Covers macOS <c>.dylib</c> and both Linux forms: unversioned <c>.so</c> and versioned
    /// <c>.so.0</c> / <c>.so.0.1.0</c>, which are the common shape of a shipped Linux library
    /// and which a plain <c>.so</c> suffix test misses.
    /// </remarks>
    /// <param name="fileName">The filename to test.</param>
    /// <returns><c>true</c> when the file is a dynamic library.</returns>
    public static bool IsDynamicLibrary(string fileName)
    {
        if (fileName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Matched on ".so." rather than ".so" so an earlier coincidental ".so" — inside
        // ".sound", say — cannot shadow the real suffix and end the search early.
        var versioned = fileName.IndexOf(".so.", StringComparison.OrdinalIgnoreCase);
        return versioned >= 0
            && fileName[(versioned + 4)..].All(c => char.IsDigit(c) || c == '.');
    }
}
