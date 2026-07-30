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
                return System.IO.Directory.Exists(configured) ? configured : null;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var defaultDirectory = Path.Combine(home, "TheSuperHackers", "GeneralsZH");

            return File.Exists(Path.Combine(defaultDirectory, BinaryName)) ? defaultDirectory : null;
        }
    }
}
