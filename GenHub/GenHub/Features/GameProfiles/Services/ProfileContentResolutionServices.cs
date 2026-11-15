using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Aggregates content resolution services required by <see cref="ProfileContentService"/>.
/// </summary>
public sealed class ProfileContentResolutionServices(
    IDependencyResolver dependencyResolver,
    IGameInstallationService installationService,
    IContentDependencyValidator dependencyValidator)
{
    /// <summary>
    /// Gets the dependency resolver.
    /// </summary>
    public IDependencyResolver DependencyResolver { get; } = dependencyResolver;

    /// <summary>
    /// Gets the game installation service.
    /// </summary>
    public IGameInstallationService InstallationService { get; } = installationService;

    /// <summary>
    /// Gets the validator that reports missing required content.
    /// </summary>
    public IContentDependencyValidator DependencyValidator { get; } = dependencyValidator;
}
