namespace GenHub.Core.Features.ActionSets;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;

/// <summary>
/// Defines a set of actions to fix or enhance a game installation.
/// </summary>
public interface IActionSet
{
    /// <summary>
    /// Gets the unique identifier for this action set.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the title of the action set.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Gets a value indicating whether this is a core fix applied by default.
    /// </summary>
    bool IsCoreFix { get; }

    /// <summary>
    /// Gets a value indicating whether this is a crucial fix for game stability.
    /// </summary>
    bool IsCrucialFix { get; }

    /// <summary>
    /// Checks if the action set is applicable to the current system and game installation.
    /// </summary>
    /// <param name="installation">The game installation to check.</param>
    /// <returns>A task representing the asynchronous operation, returning true if applicable.</returns>
    Task<bool> IsApplicableAsync(GameInstallation installation);

    /// <summary>
    /// Checks if the action set has already been applied.
    /// </summary>
    /// <param name="installation">The game installation to check.</param>
    /// <returns>A task representing the asynchronous operation, returning true if applied.</returns>
    Task<bool> IsAppliedAsync(GameInstallation installation);

    /// <summary>
    /// Applies the action set patches.
    /// </summary>
    /// <param name="installation">The game installation to patch.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation, returning the result of the action.</returns>
    Task<ActionSetResult> ApplyAsync(GameInstallation installation, CancellationToken ct = default);

    /// <summary>
    /// Undoes the action set patches if possible.
    /// </summary>
    /// <param name="installation">The game installation to revert.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation, returning the result of the undo operation.</returns>
    Task<ActionSetResult> UndoAsync(GameInstallation installation, CancellationToken ct = default);
}

/// <summary>
/// Represents the result of an action set operation.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="ErrorMessage">Error message if the operation failed.</param>
/// <param name="Details">Detailed list of actions taken during the operation.</param>
public record ActionSetResult(bool Success, string? ErrorMessage = null, List<string>? Details = null)
{
    /// <summary>
    /// Gets the details list, creating one if needed.
    /// </summary>
    public List<string> Details { get; init; } = Details ?? [];

    /// <summary>
    /// Creates a new ActionSetResult with an additional detail message.
    /// </summary>
    /// <param name="detail">The detail message to add.</param>
    /// <returns>A new ActionSetResult with the detail added.</returns>
    public ActionSetResult WithDetail(string detail)
    {
        var newDetails = new List<string>(Details) { detail };
        return this with { Details = newDetails };
    }

    /// <summary>
    /// Creates a successful result with the given details.
    /// </summary>
    /// <param name="details">The details of what was done.</param>
    /// <returns>A successful ActionSetResult.</returns>
    public static ActionSetResult SuccessWithDetails(params string[] details) =>
        new(true, null, [.. details]);

    /// <summary>
    /// Creates a failed result with the given error and optional details.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="details">Optional details of what was attempted.</param>
    /// <returns>A failed ActionSetResult.</returns>
    public static ActionSetResult FailureWithDetails(string error, params string[] details) =>
        new(false, error, [.. details]);

    /// <summary>
    /// Formats the details as a multi-line string for display.
    /// </summary>
    /// <returns>A formatted string of all details.</returns>
    public string FormatDetails() => Details.Count > 0
        ? string.Join("\n", Details)
        : "No details available.";
}
