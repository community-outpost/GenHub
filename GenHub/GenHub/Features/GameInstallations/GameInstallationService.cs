using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;

namespace GenHub.Features.GameInstallations;

/// <summary>
/// Provides services for managing game installations.
/// </summary>
public class GameInstallationService(IGameInstallationDetectionOrchestrator detectionOrchestrator) : IGameInstallationService, IDisposable
{
    private readonly IGameInstallationDetectionOrchestrator _detectionOrchestrator = detectionOrchestrator ?? throw new ArgumentNullException(nameof(detectionOrchestrator));
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private IReadOnlyList<GameInstallation>? _cachedInstallations;
    private bool _disposed = false;

    /// <summary>
    /// Gets a game installation by its ID.
    /// </summary>
    /// <param name="installationId">The installation ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="OperationResult{GameInstallation}"/> containing the installation or error.</returns>
    public async Task<OperationResult<GameInstallation>> GetInstallationAsync(string installationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installationId))
        {
            return OperationResult<GameInstallation>.CreateFailure("Installation ID cannot be null or empty.");
        }

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
    /// Gets all available game installations.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing all installations or error.</returns>
    public async Task<OperationResult<IReadOnlyList<GameInstallation>>> GetAllInstallationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureCacheInitializedAsync(cancellationToken);

            if (_cachedInstallations == null)
            {
                return OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure("Failed to detect game installations.");
            }

            return OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(_cachedInstallations);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure($"Error retrieving installations: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases resources used by the <see cref="GameInstallationService"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="GameInstallationService"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _cacheLock?.Dispose();
            _disposed = true;
        }
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
                _cachedInstallations = detectionResult.Items;
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
