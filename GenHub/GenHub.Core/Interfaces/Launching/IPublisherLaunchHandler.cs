using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Launching;

/// <summary>
/// Defines publisher-specific lifecycle hooks and customization for game launches.
/// </summary>
public interface IPublisherLaunchHandler
{
    /// <summary>
    /// Gets the unique publisher identifier supported by this handler.
    /// </summary>
    string PublisherType { get; }

    /// <summary>
    /// Determines whether this handler can handle the specified profile.
    /// </summary>
    /// <param name="profile">The game profile to test.</param>
    /// <returns>True if this handler can handle the profile; otherwise, false.</returns>
    bool CanHandle(GameProfile profile);

    /// <summary>
    /// Executes publisher-specific pre-launch configuration (e.g. settings file synchronization).
    /// </summary>
    /// <remarks>
    /// This hook operates with best-effort semantics. Any non-fatal failure will be logged as a warning
    /// and will not block the game launch, allowing the profile to execute with existing or default configuration.
    /// </remarks>
    /// <param name="profile">The game profile being launched.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<OperationResult> BeforeLaunchAsync(GameProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether the launcher's camera settings override is supported for this profile.
    /// </summary>
    /// <param name="profile">The game profile to test.</param>
    /// <returns>True if camera override is supported; otherwise, false.</returns>
    bool SupportsCameraSettingsOverride(GameProfile profile);

    /// <summary>
    /// Configures or customizes command-line arguments for the game launch.
    /// </summary>
    /// <param name="profile">The game profile being launched.</param>
    /// <param name="arguments">The launch arguments dictionary to modify.</param>
    void ConfigureLaunchArguments(GameProfile profile, Dictionary<string, string> arguments);

    /// <summary>
    /// Executes publisher-specific hooks immediately before process startup.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="BeforeLaunchAsync"/>, a failure here represents a critical process initialization
    /// error that will abort and unregister the launch.
    /// </remarks>
    /// <param name="profile">The game profile being launched.</param>
    /// <param name="launchConfig">The launch configuration for the process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An operation result indicating success or failure.</returns>
    Task<OperationResult> BeforeProcessStartAsync(
        GameProfile profile,
        GameLaunchConfiguration launchConfig,
        CancellationToken cancellationToken = default);
}
