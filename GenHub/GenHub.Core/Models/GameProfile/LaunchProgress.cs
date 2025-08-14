namespace GenHub.Core.Models.GameProfile;

/// <summary>Represents the progress of a game launch operation.</summary>
public class LaunchProgress
{
    /// <summary>Gets or sets the current launch phase.</summary>
    public LaunchPhase Phase { get; set; }

    /// <summary>Gets or sets the percentage completion (0-100).</summary>
    public int PercentComplete { get; set; }
}
