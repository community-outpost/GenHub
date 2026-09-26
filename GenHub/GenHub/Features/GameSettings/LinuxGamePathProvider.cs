using System;
using System.IO;

namespace GenHub.Features.GameSettings;

/// <summary>
/// Linux implementation of <see cref="Core.Interfaces.GameSettings.IGamePathProvider"/>.
/// <para>
/// Resolves to <c>$XDG_DATA_HOME/Command and Conquer Generals Zero Hour Data</c>,
/// falling back to <c>~/.local/share/...</c> when the variable is unset, matching
/// <c>GlobalData::BuildUserDataPathFromRegistry</c> in the game engine.
/// </para>
/// <para>
/// Until this existed, Linux fell through to <c>WindowsGamePathProvider</c> and
/// resolved <c>Environment.SpecialFolder.MyDocuments</c>, which .NET maps to the home
/// directory on Unix. Options.ini therefore landed directly in <c>$HOME</c>.
/// </para>
/// </summary>
public sealed class LinuxGamePathProvider : GamePathProviderBase
{
    /// <inheritdoc/>
    protected override string GetUserDataBaseDirectory()
    {
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return xdgDataHome;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return string.IsNullOrEmpty(home)
            ? Directory.GetCurrentDirectory()
            : Path.Combine(home, ".local", "share");
    }
}
