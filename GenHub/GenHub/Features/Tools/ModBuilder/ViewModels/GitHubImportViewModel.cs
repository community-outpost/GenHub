using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Tools.ModBuilder;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for the GitHub repository import dialog.
/// </summary>
/// <param name="localizationService">The localization service.</param>
public partial class GitHubImportViewModel(ILocalizationService localizationService) : ObservableObject
{
    /// <summary>
    /// Gets or sets the repository input (owner/repo or GitHub URL).
    /// </summary>
    [ObservableProperty]
    private string _repositoryText = string.Empty;

    /// <summary>
    /// Gets or sets the branch to import.
    /// </summary>
    [ObservableProperty]
    private string _branchText = string.Empty;

    /// <summary>
    /// Gets or sets the validation error message, if any.
    /// </summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a validation error is shown.
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Validates the current input and updates the error message.
    /// </summary>
    /// <returns>The parsed repository reference, or null when invalid.</returns>
    public GitHubRepositoryReference? Validate()
    {
        var branch = string.IsNullOrWhiteSpace(BranchText) ? null : BranchText.Trim();
        var reference = GitHubRepositoryReference.TryParse(RepositoryText, branch);
        if (reference == null)
        {
            ErrorText = localizationService.GetString("Tools.ModBuilder.GitHubImport.Validation.InvalidReference");
            HasError = true;
            return null;
        }

        ErrorText = string.Empty;
        HasError = false;
        BranchText = reference.Branch;
        return reference;
    }

    partial void OnRepositoryTextChanged(string value)
    {
        ClearError();
    }

    partial void OnBranchTextChanged(string value)
    {
        ClearError();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Mutates CommunityToolkit-generated observable properties.")]
    private void ClearError()
    {
        if (HasError)
        {
            ErrorText = string.Empty;
            HasError = false;
        }
    }
}
