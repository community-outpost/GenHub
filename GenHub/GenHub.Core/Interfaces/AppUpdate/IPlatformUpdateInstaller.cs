namespace GenHub.Core.Interfaces.AppUpdate;

/// <summary>
/// Marker interface for platform-specific update installers.
/// This is used by the dependency injection container to resolve the correct installer for the current platform.
/// </summary>
public interface IPlatformUpdateInstaller : IUpdateInstaller
{
}
