using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GeneralsOnline;

/// <summary>
/// Matches local game profiles against Generals Online lobby CRC values.
/// </summary>
public interface IGeneralsOnlineCompatibilityService
{
    /// <summary>
    /// Evaluates one profile against one lobby by comparing executable and INI CRC values.
    /// </summary>
    /// <param name="profile">The local profile.</param>
    /// <param name="lobby">The lobby carrying host CRC values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The compatibility verdict.</returns>
    Task<OperationResult<GeneralsOnlineProfileMatch>> MatchProfileAsync(
        GameProfile profile,
        GeneralsOnlineLobby lobby,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates every local profile against one lobby, compatible profiles first.
    /// </summary>
    /// <param name="lobby">The lobby carrying host CRC values.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ranked compatibility verdicts.</returns>
    Task<OperationResult<IReadOnlyList<GeneralsOnlineProfileMatch>>> RankProfilesAsync(
        GeneralsOnlineLobby lobby,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops cached profile CRC values so the next match re-hashes.
    /// </summary>
    /// <param name="profileId">The profile id, or null to drop the whole cache.</param>
    void InvalidateCache(string? profileId = null);
}
