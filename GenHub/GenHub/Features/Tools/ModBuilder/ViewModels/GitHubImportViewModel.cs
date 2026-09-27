using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Tools.ModBuilder;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for the GitHub repository import dialog.
/// </summary>
public partial class GitHubImportViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubImportViewModel"/> class.
    /// </summary>
    /// <param name="localizationService">The localization service.</param>
    public GitHubImportViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    /// <summary>
    /// Gets or sets the repository input (owner/repo or GitHub URL).
    /// </summary>
    [ObservableProperty]
    private string _repositoryText = string.Empty;

    /// <summary>
    /// Gets or sets the branch to import.
    /// </summary>
    [ObservableProperty]
    private string _branchText = ModBuilderConstants.GitHubDefaultBranch;

    /// <summary>
    /// Gets or sets the validation error message, if any.
    /// </summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>
    /// Gets a value indicating whether a validation error is shown.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>
    /// Validates the current input and updates the error message.
    /// </summary>
    /// <returns>The parsed repository reference, or null when invalid.</returns>
    public GitHubRepositoryReference? Validate()
    {
        var branch = string.IsNullOrWhiteSpace(BranchText) ? ModBuilderConstants.GitHubDefaultBranch : BranchText.Trim();
        var reference = GitHubRepositoryReference.TryParse(RepositoryText, branch);
        if (reference == null)
        {
            ErrorText = _localizationService.GetString("Tools.ModBuilder.GitHubImport.Validation.InvalidReference");
            return null;
        }

        ErrorText = string.Empty;
        return reference;
    }

    partial void OnRepositoryTextChanged(string value)
    {
        if (!string.IsNullOrEmpty(ErrorText))
        {
            ErrorText = string.Empty;
        }
    }

    partial void OnBranchTextChanged(string value)
    {
        if (!string.IsNullOrEmpty(ErrorText))
        {
            ErrorText = string.Empty;
        }
    }

    partial void OnErrorTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasError));
    }
}
