using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Launching.Publishers;

/// <summary>
/// Default publisher launch handler for standard platforms and generic community clients.
/// </summary>
public class DefaultPublisherLaunchHandler : IPublisherLaunchHandler
{
    /// <inheritdoc/>
    public string PublisherType => PublisherTypeConstants.Unknown;

    /// <inheritdoc/>
    public bool CanHandle(GameProfile profile) => true;

    /// <inheritdoc/>
    public Task<OperationResult> BeforeLaunchAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.CreateSuccess());
    }

    /// <inheritdoc/>
    public bool SupportsCameraSettingsOverride(GameProfile profile) => true;

    /// <inheritdoc/>
    public void ConfigureLaunchArguments(GameProfile profile, Dictionary<string, string> arguments)
    {
        // Default handler makes no argument modifications.
    }

    /// <inheritdoc/>
    public Task<OperationResult> BeforeProcessStartAsync(
        GameProfile profile,
        GameLaunchConfiguration launchConfig,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult.CreateSuccess());
    }
}
