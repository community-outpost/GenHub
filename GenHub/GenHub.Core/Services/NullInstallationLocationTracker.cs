using GenHub.Core.Interfaces.Storage;

namespace GenHub.Core.Services;

/// <summary>
/// Default no-op implementation of <see cref="IInstallationLocationTracker"/> for non-Windows platforms.
/// </summary>
public sealed class NullInstallationLocationTracker : IInstallationLocationTracker
{
    /// <inheritdoc />
    public void RecordInstallLocation()
    {
    }

    /// <inheritdoc />
    public string? GetRegisteredCustomInstallPath() => null;

    /// <inheritdoc />
    public void ClearCustomInstallPath()
    {
    }
}
