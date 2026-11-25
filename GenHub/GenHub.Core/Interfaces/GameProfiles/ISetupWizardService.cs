using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Service for running the Setup Wizard to handle detected game content.
/// </summary>
public interface ISetupWizardService
{
    /// <summary>
    /// Runs the setup wizard for the given installations.
    /// </summary>
    /// <param name="installations">The list of detected game installations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the setup wizard execution.</returns>
    Task<SetupWizardResult> RunSetupWizardAsync(IEnumerable<GameInstallation> installations, CancellationToken cancellationToken = default);
}
