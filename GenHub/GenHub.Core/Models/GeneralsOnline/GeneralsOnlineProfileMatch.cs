namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Compatibility verdict for one local profile against one lobby.
/// </summary>
/// <param name="ProfileId">The evaluated profile id.</param>
/// <param name="ProfileName">The evaluated profile name.</param>
/// <param name="Compatibility">The compatibility verdict.</param>
/// <param name="ProfileExeCrc">The profile executable CRC, or null when unknown.</param>
/// <param name="ProfileIniCrc">The profile INI CRC, or null when unknown.</param>
public sealed record GeneralsOnlineProfileMatch(
    string ProfileId,
    string ProfileName,
    GeneralsOnlineCompatibility Compatibility,
    uint? ProfileExeCrc,
    uint? ProfileIniCrc);
