using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.Services;

/// <summary>
/// Mounts the richest detected game installation (both games preferred, then
/// Zero Hour, then Generals) into the shared asset file system and loads INI
/// subsystems once, clearing texture and model caches on every fresh mount.
/// </summary>
public sealed class WorldBuilderContentService(
    IGameInstallationService installations,
    IGameAssetFileSystem fileSystem,
    ISageIniDatabase iniDatabase,
    ITextureCache textureCache,
    WbModelRenderService modelRenderService,
    ILogger<WorldBuilderContentService> logger) : IWorldBuilderContentService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _mountedInstallationId;

    /// <inheritdoc />
    public bool IsContentReady => _mountedInstallationId != null;

    /// <inheritdoc />
    public string? MountedInstallationId => _mountedInstallationId;

    /// <inheritdoc />
    public async Task<OperationResult<bool>> EnsureContentLoadedAsync(CancellationToken cancellationToken = default)
    {
        var selected = await SelectInstallationAsync(cancellationToken).ConfigureAwait(false);
        if (selected == null)
        {
            return OperationResult<bool>.CreateFailure("No game installations found for WorldBuilder content.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(_mountedInstallationId, selected.Id, StringComparison.Ordinal))
            {
                return OperationResult<bool>.CreateSuccess(false);
            }

            var mounted = await fileSystem
                .MountAsync(new GameAssetMountSpec(InstallationId: selected.Id), cancellationToken)
                .ConfigureAwait(false);
            if (!mounted.Success)
            {
                logger.LogWarning("Failed to mount installation {InstallationId} for WorldBuilder content.", selected.Id);
                return OperationResult<bool>.CreateFailure($"Failed to mount installation {selected.Id}.");
            }

            textureCache.Clear();
            modelRenderService.ClearCache();
            var loaded = await iniDatabase.LoadSubsystemsAsync(fileSystem, cancellationToken).ConfigureAwait(false);
            if (!loaded.Success)
            {
                logger.LogWarning("Failed to load INI subsystems for WorldBuilder content.");
                return OperationResult<bool>.CreateFailure("Failed to load INI subsystems for WorldBuilder content.");
            }

            _mountedInstallationId = selected.Id;
            logger.LogInformation(
                "Mounted installation {DisplayName} for WorldBuilder content ({Files} INI files).",
                selected.DisplayName,
                loaded.Data?.FilesRead ?? 0);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GameInstallation?> SelectInstallationAsync(CancellationToken cancellationToken)
    {
        var result = await installations.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            return null;
        }

        return result.Data
            .Where(installation => installation.HasZeroHour || installation.HasGenerals)
            .OrderByDescending(installation => installation.HasZeroHour && installation.HasGenerals)
            .ThenByDescending(installation => installation.HasZeroHour)
            .ThenBy(installation => installation.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
