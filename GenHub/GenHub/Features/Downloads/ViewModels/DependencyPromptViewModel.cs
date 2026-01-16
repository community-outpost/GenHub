using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Content;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for the dependency prompt dialog.
/// Allows users to choose what to do when downloading content with missing dependencies.
/// </summary>
public partial class DependencyPromptViewModel : ObservableObject
{
    private readonly Action<DependencyDecision> _onDecision;

    [ObservableProperty]
    private string _contentName = string.Empty;

    [ObservableProperty]
    private string _contentVersion = string.Empty;

    [ObservableProperty]
    private int _missingCount;

    [ObservableProperty]
    private int _optionalCount;

    [ObservableProperty]
    private bool _canAutoInstallAll;

    [ObservableProperty]
    private string _primaryDependencyName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<MissingDependencyItemViewModel> _dependencies = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyPromptViewModel"/> class.
    /// </summary>
    /// <param name="contentName">Name of the content being downloaded.</param>
    /// <param name="contentVersion">Version of the content being downloaded.</param>
    /// <param name="missingDependencies">List of missing dependencies.</param>
    /// <param name="onDecision">Callback invoked when user makes a decision.</param>
    public DependencyPromptViewModel(
        string contentName,
        string contentVersion,
        System.Collections.Generic.IEnumerable<MissingDependency> missingDependencies,
        Action<DependencyDecision> onDecision)
    {
        ContentName = contentName;
        ContentVersion = contentVersion;
        _onDecision = onDecision ?? throw new ArgumentNullException(nameof(onDecision));

        var missingList = missingDependencies.ToList();
        MissingCount = missingList.Count;
        OptionalCount = missingList.Count(d => d.Dependency.IsOptional);
        CanAutoInstallAll = missingList.All(d => d.CanAutoInstall);

        if (missingList.Count > 0)
        {
            PrimaryDependencyName = missingList[0].Dependency.Name ?? "Unknown";
        }

        // Populate dependencies list
        foreach (var dep in missingList)
        {
            Dependencies.Add(new MissingDependencyItemViewModel(dep));
        }
    }

    /// <summary>
    /// Gets a summary text describing the missing dependencies.
    /// </summary>
    public string SummaryText
    {
        get
        {
            if (MissingCount == 0)
            {
                return "No missing dependencies.";
            }

            var parts = new System.Collections.Generic.List<string>();

            if (OptionalCount > 0)
            {
                parts.Add($"{OptionalCount} optional");
            }

            var requiredCount = MissingCount - OptionalCount;
            if (requiredCount > 0)
            {
                parts.Add($"{requiredCount} required");
            }

            var typeText = parts.Count > 0 ? string.Join(" and ", parts) : "All";
            return $"{MissingCount} missing {typeText} dependenc{(MissingCount == 1 ? "y" : "ies")}.";
        }
    }

    /// <summary>
    /// User chooses to install all dependencies.
    /// </summary>
    [RelayCommand]
    private void InstallAll()
    {
        _onDecision(DependencyDecision.InstallAll);
    }

    /// <summary>
    /// User chooses to skip dependencies and continue.
    /// </summary>
    [RelayCommand]
    private void SkipDependencies()
    {
        _onDecision(DependencyDecision.SkipDependencies);
    }

    /// <summary>
    /// User cancels the download.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _onDecision(DependencyDecision.Cancel);
    }
}
