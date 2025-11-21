namespace GenHub.Core.Models.GameProfile;

/// <summary>Represents the progress of a game launch operation.</summary>
public class LaunchProgress
{
    private int _percentComplete;

    /// <summary>Gets or sets the current launch phase.</summary>
    public LaunchPhase Phase { get; set; }

    /// <summary>Gets or sets the percentage completion (0-100).</summary>
    public int PercentComplete
    {
        get => _percentComplete;
        set
        {
            if (value < 0 || value > 100)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Percentage must be between 0 and 100.");
            _percentComplete = value;
        }
    }
}