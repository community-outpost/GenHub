using GenHub.Core.Models.GameProfile;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Resolves the effective verification file set for a game profile: the manifest-expected
/// base files plus locally available enabled-content overlay archives.
/// </summary>
public interface IProfileVerificationFileSetService
{
    /// <summary>
    /// Resolves the verification file set for a profile without downloading anything.
    /// </summary>
    /// <param name="profile">The game profile to evaluate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved file set, possibly degraded when manifests or files are unavailable.</returns>
    Task<ProfileVerificationFileSet> GetVerificationFileSetAsync(IGameProfile profile, CancellationToken cancellationToken = default);
}
