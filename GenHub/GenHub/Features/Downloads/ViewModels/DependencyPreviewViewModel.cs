using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Results;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for the dependency preview dialog that shows dependencies before adding content to a profile.
/// </summary>
public partial class DependencyPreviewViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyPreviewViewModel"/> class.
    /// </summary>
    /// <param name="contentName">The name of the content being added.</param>
    /// <param name="resolutionResult">The dependency resolution result.</param>
    public DependencyPreviewViewModel(string contentName, DependencyResolutionResult resolutionResult)
    {
        ContentName = contentName;
        ResolutionResult = resolutionResult;

        // Populate resolved dependencies
        foreach (var manifest in resolutionResult.ResolvedManifests)
        {
            ResolvedDependencies.Add(new DependencyItemViewModel
            {
                Name = manifest.Name,
                Id = manifest.Id.Value,
                IsResolved = true,
                Description = $"{manifest.ContentType} for {manifest.TargetGame}",
            });
        }

        // Populate missing dependencies
        foreach (var missingId in resolutionResult.MissingContentIds)
        {
            MissingDependencies.Add(new DependencyItemViewModel
            {
                Name = missingId,
                Id = missingId,
                IsResolved = false,
                Description = "This dependency is required but not available in your library.",
            });
        }
    }

    /// <summary>
    /// Gets the name of the content being added.
    /// </summary>
    public string ContentName { get; }

    /// <summary>
    /// Gets the dependency resolution result.
    /// </summary>
    public DependencyResolutionResult ResolutionResult { get; }

    /// <summary>
    /// Gets the collection of resolved dependencies.
    /// </summary>
    public ObservableCollection<DependencyItemViewModel> ResolvedDependencies { get; } = [];

    /// <summary>
    /// Gets the collection of missing dependencies.
    /// </summary>
    public ObservableCollection<DependencyItemViewModel> MissingDependencies { get; } = [];

    /// <summary>
    /// Gets a value indicating whether there are missing dependencies.
    /// </summary>
    public bool HasMissingDependencies => MissingDependencies.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the user can proceed with adding the content.
    /// </summary>
    public bool CanProceed => !HasMissingDependencies || UserAcceptedRisk;

    /// <summary>
    /// Gets the total count of dependencies.
    /// </summary>
    public int TotalDependencyCount => ResolvedDependencies.Count + MissingDependencies.Count;

    /// <summary>
    /// Gets or sets a value indicating whether the user has accepted the risk of missing dependencies.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanProceed))]
    [NotifyCanExecuteChangedFor(nameof(ProceedCommand))]
    private bool _userAcceptedRisk;

    /// <summary>
    /// Gets or sets a value indicating whether the user chose to proceed.
    /// </summary>
    [ObservableProperty]
    private bool _userChoseToProceed;

    /// <summary>
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event EventHandler? RequestClose;

    /// <summary>
    /// Command to proceed with adding the content.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanProceed))]
    private void Proceed()
    {
        UserChoseToProceed = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Command to cancel the operation.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        UserChoseToProceed = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
