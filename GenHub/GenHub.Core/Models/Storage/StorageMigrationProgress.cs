namespace GenHub.Core.Models.Storage;

/// <summary>
/// Represents progress updates during an installation and storage migration operation.
/// </summary>
public class StorageMigrationProgress
{
    /// <summary>
    /// Gets or sets the name of the current migration stage.
    /// </summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the progress completion percentage (0 - 100).
    /// </summary>
    public double Percentage { get; set; }

    /// <summary>
    /// Gets or sets a descriptive status message for the current operation.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
