using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools;

/// <summary>
/// Base interface for tool view models.
/// All tool view models must implement this interface.
/// </summary>
public interface IToolViewModel
{
    /// <summary>
    /// Gets or sets a value indicating whether this tool is currently active/visible.
    /// </summary>
    bool IsActive { get; set; }

    /// <summary>
    /// Gets a value indicating whether the tool is currently busy performing an operation.
    /// </summary>
    bool IsBusy { get; }

    /// <summary>
    /// Initializes the tool view model.
    /// Called when the tool is first activated.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Activates the tool view model.
    /// Called when the user selects this tool.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task ActivateAsync();

    /// <summary>
    /// Deactivates the tool view model.
    /// Called when the user navigates away from this tool.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task DeactivateAsync();

    /// <summary>
    /// Refreshes the tool's data.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task RefreshAsync();
}
