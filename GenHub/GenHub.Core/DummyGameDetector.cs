namespace GenHub.Core;

/// <summary>
/// A dummy implementation of <see cref="IGameDetector"/> that does nothing.
/// </summary>
public class DummyGameDetector : IGameDetector
{
    /// <inheritdoc/>
    public List<IGameInstallation> Installations => [];

    /// <inheritdoc/>
    public void Detect()
    {
        throw new System.NotImplementedException();
    }
}