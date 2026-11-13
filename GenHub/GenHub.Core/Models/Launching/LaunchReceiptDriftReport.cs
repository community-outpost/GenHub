namespace GenHub.Core.Models.Launching;

/// <summary>
/// Outcome of cheaply revalidating a launch receipt against the current on-disk state.
/// </summary>
public class LaunchReceiptDriftReport
{
    /// <summary>Gets or sets the path the receipt was looked for at.</summary>
    public string ReceiptPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a receipt was present. An absent receipt is
    /// not an error; there is simply nothing to compare against.
    /// </summary>
    public bool HasReceipt { get; set; }

    /// <summary>
    /// Gets or sets the parsed receipt when one was present and readable, so the upcoming
    /// launch's configuration can be compared against it after the workspace — and the
    /// receipt file with it — has been rebuilt.
    /// </summary>
    public LaunchReceipt? Receipt { get; set; }

    /// <summary>Gets or sets the description of each field that drifted since the receipt was recorded.</summary>
    public List<string> DriftedFields { get; set; } = [];

    /// <summary>Gets a value indicating whether any drift was detected.</summary>
    public bool HasDrift => DriftedFields.Count > 0;
}
