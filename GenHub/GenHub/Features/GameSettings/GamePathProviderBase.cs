using System.IO;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.GameSettings;

/// <summary>
/// Shared behaviour for <see cref="IGamePathProvider"/> implementations.
/// <para>
/// Every platform uses the same leaf folder name — "Command and Conquer Generals Data"
/// or "Command and Conquer Generals Zero Hour Data" — and differs only in the base
/// directory those sit under. Subclasses supply the base; this class owns the rest.
/// </para>
/// <para>
/// The paths are dictated by the game engine, not chosen by GenHub. See
/// <c>GlobalData::BuildUserDataPathFromRegistry</c> in the GeneralsGameCode tree:
/// Windows resolves Documents via <c>SHGetKnownFolderPath</c>, macOS uses
/// <c>~/Library/Application Support</c>, and Linux uses <c>XDG_DATA_HOME</c> with a
/// <c>~/.local/share</c> fallback. Writing Options.ini anywhere else means the engine
/// silently never reads it, the launch still succeeds, and every profile setting is
/// discarded without an error.
/// </para>
/// </summary>
public abstract class GamePathProviderBase : IGamePathProvider
{
    /// <inheritdoc/>
    public string GetOptionsDirectory(GameType gameType)
    {
        var folderName = gameType == GameType.ZeroHour
            ? GameSettingsConstants.FolderNames.ZeroHour
            : GameSettingsConstants.FolderNames.Generals;

        return Path.Combine(GetUserDataBaseDirectory(), folderName);
    }

    /// <summary>
    /// Gets the platform's base directory for per-user game data, without the
    /// game-specific leaf folder.
    /// </summary>
    /// <returns>An absolute directory path.</returns>
    protected abstract string GetUserDataBaseDirectory();
}
