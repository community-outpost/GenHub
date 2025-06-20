namespace GenHub.Core;

/// <summary>
/// Interface for a game detector that detects game installations.
/// </summary>
public interface IGameDetector
{
    /// <summary>
    /// Gets a list of all detected <see cref="IGameInstallation"/>s.
    /// </summary>
    public List<IGameInstallation> Installations { get; }

    /// <summary>
    /// Detects a <see cref="IGameInstallation"/> and adds it to an internal list.<br/>
    /// To get the list, use <see cref="Installations"/>.
    /// </summary>
    public void Detect();
}