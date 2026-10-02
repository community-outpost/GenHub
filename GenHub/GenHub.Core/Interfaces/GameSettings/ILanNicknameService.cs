using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GameSettings;

/// <summary>
/// Reads and writes the LAN player nickname (<c>UserName</c> in
/// <c>Network.ini</c>) for Generals and Zero Hour. The game loads this value
/// when its LAN lobby opens, so writing it before launch is how launchers such
/// as GameRanger set the in-game name.
/// </summary>
public interface ILanNicknameService
{
    /// <summary>
    /// Gets the path to the <c>Network.ini</c> file for the specified game type.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <returns>The full path to the <c>Network.ini</c> file.</returns>
    string GetNetworkFilePath(GameType gameType);

    /// <summary>
    /// Loads the LAN nickname stored in <c>Network.ini</c>.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result containing the nickname, or empty when none is stored.</returns>
    Task<OperationResult<string>> LoadNicknameAsync(GameType gameType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the LAN nickname to <c>Network.ini</c>, preserving all other keys.
    /// A blank nickname fails without touching the file.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <param name="nickname">The nickname to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> SaveNicknameAsync(GameType gameType, string nickname, CancellationToken cancellationToken = default);
}
