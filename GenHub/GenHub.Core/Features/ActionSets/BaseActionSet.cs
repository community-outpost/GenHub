namespace GenHub.Core.Features.ActionSets;

using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Abstract base class for action sets, providing common functionality.
/// </summary>
public abstract class BaseActionSet : IActionSet
{
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseActionSet"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    protected BaseActionSet(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public abstract string Id { get; }

    /// <inheritdoc/>
    public abstract string Title { get; }

    /// <inheritdoc/>
    public abstract bool IsCoreFix { get; }

    /// <inheritdoc/>
    public abstract bool IsCrucialFix { get; }

    /// <inheritdoc/>
    public abstract Task<bool> IsApplicableAsync(GameInstallation installation);

    /// <inheritdoc/>
    public abstract Task<bool> IsAppliedAsync(GameInstallation installation);

    /// <inheritdoc/>
    public async Task<ActionSetResult> ApplyAsync(GameInstallation installation, CancellationToken ct = default)
    {
        _logger.LogInformation("Applying ActionSet {Title} ({Id}) to {InstallationPath}...", Title, Id, installation.InstallationPath);
        try
        {
            var result = await ApplyInternalAsync(installation, ct);
            if (result.Success)
            {
                _logger.LogInformation("Successfully applied ActionSet {Title} ({Id})", Title, Id);
            }
            else
            {
                _logger.LogWarning("Failed to apply ActionSet {Title} ({Id}): {Error}", Title, Id, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying ActionSet {Title} ({Id})", Title, Id);
            return new ActionSetResult(false, ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<ActionSetResult> UndoAsync(GameInstallation installation, CancellationToken ct = default)
    {
        _logger.LogInformation("Undoing ActionSet {Title} ({Id}) from {InstallationPath}...", Title, Id, installation.InstallationPath);
        try
        {
            var result = await UndoInternalAsync(installation, ct);
            if (result.Success)
            {
                _logger.LogInformation("Successfully undid ActionSet {Title} ({Id})", Title, Id);
            }
            else
            {
                _logger.LogWarning("Failed to undo ActionSet {Title} ({Id}): {Error}", Title, Id, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undoing ActionSet {Title} ({Id})", Title, Id);
            return new ActionSetResult(false, ex.Message);
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

    /// <summary>
    /// Helper to return a successful result.
    /// </summary>
    /// <returns>A successful ActionSetResult.</returns>
    protected ActionSetResult Success() => new(true);

    /// <summary>
    /// Helper to return a failed result.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>A failed ActionSetResult.</returns>
    protected ActionSetResult Failure(string message) => new(false, message);
}
