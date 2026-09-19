using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Defines a precondition or environment check for an installation step.
/// Allows domain-specific probes (e.g. system service or installed anti-cheat detection)
/// to determine whether a step is already satisfied.
/// </summary>
public interface IInstallationStepPrecondition
{
    /// <summary>
    /// Determines whether this precondition can handle the specified installation step.
    /// </summary>
    /// <param name="step">The installation step to inspect.</param>
    /// <param name="manifest">The content manifest declaring the step.</param>
    /// <returns><see langword="true"/> if this precondition applies to the step; otherwise, <see langword="false"/>.</returns>
    bool CanHandle(InstallationStep step, ContentManifest manifest);

    /// <summary>
    /// Determines whether the step's goal is already fulfilled in the local environment.
    /// </summary>
    /// <param name="step">The installation step to evaluate.</param>
    /// <param name="manifest">The content manifest declaring the step.</param>
    /// <returns><see langword="true"/> if the step is already fulfilled; otherwise, <see langword="false"/>.</returns>
    bool IsAlreadyFulfilled(InstallationStep step, ContentManifest manifest);
}
