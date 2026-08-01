using System;
using System.IO;

namespace GenHub.Features.GameSettings;

/// <summary>
/// macOS implementation of <see cref="Core.Interfaces.GameSettings.IGamePathProvider"/>.
/// <para>
/// Resolves to <c>~/Library/Application Support/Command and Conquer Generals Zero Hour Data</c>,
/// matching <c>GlobalData::BuildUserDataPathFromRegistry</c> in the game engine.
/// </para>
/// <para>
/// Note there is no vendor subdirectory and the leaf name is identical to Windows.
/// This is deliberately <em>not</em> the <c>SDL_GetPrefPath</c> convention
/// (<c>~/Library/Application Support/&lt;org&gt;/&lt;app&gt;/</c>) that an SDL3-based
/// port might be expected to use.
/// </para>
/// </summary>
public sealed class MacOSGamePathProvider : GamePathProviderBase
{
    /// <inheritdoc/>
    protected override string GetUserDataBaseDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Environment.SpecialFolder.ApplicationData maps to ~/.config on macOS, which is
        // the Linux convention rather than the Apple one, so it is not used here.
        return string.IsNullOrEmpty(home)
            ? Directory.GetCurrentDirectory()
            : Path.Combine(home, "Library", "Application Support");
    }
}
