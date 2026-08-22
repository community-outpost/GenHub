namespace GenHub.Core.Features.ActionSets;

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Abstract base class for action sets, providing common functionality.
/// </summary>
public abstract class BaseActionSet(ILogger logger) : IActionSet
{
    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public abstract string Title { get; }

    /// <inheritdoc/>
    public virtual string Description => Title;

    /// <inheritdoc/>
    public virtual string DetailedDescription => string.Empty;

    /// <inheritdoc/>
    public virtual string Category => IsCoreFix ? "Core & Stability" : "Compatibility";

    /// <inheritdoc/>
    public abstract bool IsCoreFix { get; }

    /// <inheritdoc/>
    public abstract bool IsCrucialFix { get; }

    /// <summary>
    /// Gets the logger instance.
    /// </summary>
    protected ILogger Logger => logger;

    /// <inheritdoc/>
    public virtual Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
        => Task.FromResult(true);

    /// <inheritdoc/>
    public virtual Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <inheritdoc/>
    public async Task<ActionSetResult> ApplyAsync(GameInstallation installation, CancellationToken ct = default)
    {
        logger.LogInformation("Applying ActionSet {Title} ({Id}) to {InstallationPath}...", Title, Id, installation.InstallationPath);
        try
        {
            var result = await ApplyInternalAsync(installation, ct);
            if (result.Success)
            {
                logger.LogInformation("Successfully applied ActionSet {Title} ({Id})", Title, Id);
            }
            else
            {
                logger.LogWarning("Failed to apply ActionSet {Title} ({Id}): {Error}", Title, Id, result.ErrorMessage);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying ActionSet {Title} ({Id})", Title, Id);
            return new ActionSetResult(false, ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<ActionSetResult> UndoAsync(GameInstallation installation, CancellationToken ct = default)
    {
        logger.LogInformation("Undoing ActionSet {Title} ({Id}) from {InstallationPath}...", Title, Id, installation.InstallationPath);
        try
        {
            var result = await UndoInternalAsync(installation, ct);
            if (result.Success)
            {
                logger.LogInformation("Successfully undid ActionSet {Title} ({Id})", Title, Id);
            }
            else
            {
                logger.LogWarning("Failed to undo ActionSet {Title} ({Id}): {Error}", Title, Id, result.ErrorMessage);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error undoing ActionSet {Title} ({Id})", Title, Id);
            return new ActionSetResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Helper to return a successful result.
    /// </summary>
    /// <returns>A successful ActionSetResult.</returns>
    protected static ActionSetResult Success() => new(true);

    /// <summary>
    /// Helper to return a failed result.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>A failed ActionSetResult.</returns>
    protected static ActionSetResult Failure(string message) => new(false, message);

    /// <summary>
    /// Checks if the marker file exists on disk.
    /// </summary>
    /// <param name="markerPath">The marker file path.</param>
    /// <returns><c>true</c> if the marker exists; otherwise, <c>false</c>.</returns>
    protected static bool MarkerExists(string markerPath) => File.Exists(markerPath);

    /// <summary>
    /// Writes a marker file with the current UTC timestamp.
    /// </summary>
    /// <param name="markerPath">The marker file path.</param>
    protected static void WriteMarkerFile(string markerPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(markerPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(markerPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignored - marker write non-fatal
        }
    }

    /// <summary>
    /// Deletes a marker file if it exists on disk.
    /// </summary>
    /// <param name="markerPath">The marker file path.</param>
    protected static void DeleteMarkerFile(string markerPath)
    {
        DeleteFileSafely(markerPath);
    }

    /// <summary>
    /// Safely deletes a file if it exists, clearing read-only attributes.
    /// </summary>
    /// <param name="path">The file path to delete.</param>
    protected static void DeleteFileSafely(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignored - cleanup failure non-fatal
        }
    }

    /// <summary>
    /// Implements the specific application logic.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    protected abstract Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct);

    /// <summary>
    /// Implements the specific undo logic.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    protected abstract Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct);
}
