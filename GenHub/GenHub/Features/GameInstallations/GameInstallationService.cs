using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;

namespace GenHub.Features.GameInstallations
{
    /// <summary>
    /// Provides services for managing game installations.
    /// </summary>
    public class GameInstallationService : IGameInstallationService
    {
        private readonly IGameInstallationDetectionOrchestrator _detectionOrchestrator;
        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        private IReadOnlyList<GameInstallation>? _cachedInstallations;

        /// <summary>
        /// Initializes a new instance of the <see cref="GameInstallationService"/> class.
        /// </summary>
        /// <param name="detectionOrchestrator">The detection orchestrator.</param>
        public GameInstallationService(IGameInstallationDetectionOrchestrator detectionOrchestrator)
        {
            _detectionOrchestrator = detectionOrchestrator;
        }

        /// <summary>
        /// Gets a game installation by its ID.
        /// </summary>
        /// <param name="installationId">The installation ID.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>An <see cref="OperationResult{GameInstallation}"/> containing the installation or error.</returns>
        public async Task<OperationResult<GameInstallation>> GetInstallationAsync(string installationId, CancellationToken cancellationToken = default)
        {
            await EnsureCacheInitializedAsync(cancellationToken);

            if (_cachedInstallations == null)
            {
                return OperationResult<GameInstallation>.CreateFailure("Failed to detect game installations.");
            }

            var installation = _cachedInstallations.FirstOrDefault(i => i.Id == installationId);

            if (installation == null)
            {
                return OperationResult<GameInstallation>.CreateFailure($"Game installation with ID '{installationId}' not found.");
            }

            return OperationResult<GameInstallation>.CreateSuccess(installation);
        }

        /// <summary>
        /// Ensures the installation cache is initialized.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task EnsureCacheInitializedAsync(CancellationToken cancellationToken)
        {
            if (_cachedInstallations != null) return;

            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                if (_cachedInstallations != null) return;

                var detectionResult = await _detectionOrchestrator.DetectAllInstallationsAsync(cancellationToken);
                if (detectionResult.Success)
                {
                    _cachedInstallations = detectionResult.Installations;
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }
    }
}
