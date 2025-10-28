using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Interfaces.Tools;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Abstract base class for tool view models.
/// Provides common functionality for all tools.
/// </summary>
public abstract partial class ToolViewModelBase : ObservableObject, IToolViewModel
{
    private readonly ILogger? _logger;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolViewModelBase"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    protected ToolViewModelBase(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public virtual async Task InitializeAsync()
    {
        _logger?.LogDebug("Initializing tool: {ToolType}", GetType().Name);
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async Task ActivateAsync()
    {
        _logger?.LogDebug("Activating tool: {ToolType}", GetType().Name);
        IsActive = true;
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async Task DeactivateAsync()
    {
        _logger?.LogDebug("Deactivating tool: {ToolType}", GetType().Name);
        IsActive = false;
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async Task RefreshAsync()
    {
        _logger?.LogDebug("Refreshing tool: {ToolType}", GetType().Name);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Sets the busy state and executes an action.
    /// </summary>
    /// <param name="action">The async action to execute.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    protected async Task ExecuteWithBusyStateAsync(System.Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Logs an error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="args">Message format arguments.</param>
    protected void LogError(string message, params object[] args)
    {
        _logger?.LogError(message, args);
    }

    /// <summary>
    /// Logs an information message.
    /// </summary>
    /// <param name="message">The information message.</param>
    /// <param name="args">Message format arguments.</param>
    protected void LogInformation(string message, params object[] args)
    {
        _logger?.LogInformation(message, args);
    }
}
