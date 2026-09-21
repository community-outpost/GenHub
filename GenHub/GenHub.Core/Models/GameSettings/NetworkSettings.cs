namespace GenHub.Core.Models.GameSettings;

/// <summary>
/// Represents network-related settings in Options.ini.
/// </summary>
public class NetworkSettings
{
    /// <summary>
    /// Gets or sets the IP address for GameSpy/Networking services.
    /// This is typically used for LAN or online play.
    /// </summary>
    public string? GameSpyIPAddress { get; set; }

    /// <summary>
    /// Gets or sets the LAN adapter address shown in the in-game network options.
    /// The game reads this key for LAN lobby binding independently of GameSpyIPAddress.
    /// </summary>
    public string? IPAddress { get; set; }

    /// <summary>Gets or sets additional network properties not explicitly defined. Used to preserve game-specific settings.</summary>
    public Dictionary<string, string> AdditionalProperties { get; set; } = new();
}
