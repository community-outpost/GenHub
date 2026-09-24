using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Validation;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for editing/renaming a catalog and setting its optional icon.
/// </summary>
/// <param name="currentName">The current catalog name.</param>
/// <param name="onComplete">Callback invoked with the dialog result.</param>
/// <param name="canDelete">Whether the catalog can be deleted.</param>
/// <param name="onDelete">Optional delete callback.</param>
/// <param name="currentIconUrl">Optional current icon URL.</param>
[SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI.")]
public partial class RenameCatalogDialogViewModel(
    string currentName,
    Action<RenameCatalogResult?> onComplete,
    bool canDelete = false,
    Func<Task<bool>>? onDelete = null,
    string? currentIconUrl = null) : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [LocalizedRequired("Tools.PublisherStudio.Validation.CatalogNameRequired", "Catalog name is required")]
    [LocalizedMinLength(1, "Tools.PublisherStudio.Validation.CatalogNameNotEmpty", "Catalog name cannot be empty")]
    private string _catalogName = currentName ?? string.Empty;

    [ObservableProperty]
    private string? _iconUrl = currentIconUrl;

    [ObservableProperty]
    private bool _canDelete = canDelete;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private bool _isValid;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (onDelete != null)
        {
            var deleted = await onDelete();
            if (deleted)
            {
                onComplete(null);
            }
        }
    }

    partial void OnCatalogNameChanged(string value)
    {
        Validate();
    }

    private void Validate()
    {
        ValidateAllProperties();
        IsValid = !HasErrors;
        ValidationError = HasErrors
            ? string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage ?? string.Empty))
            : null;
    }

    [RelayCommand]
    private void Save()
    {
        Validate();
        if (HasErrors) return;
        onComplete(new RenameCatalogResult(CatalogName.Trim(), string.IsNullOrWhiteSpace(IconUrl) ? null : IconUrl.Trim()));
    }

    [RelayCommand]
    private void Cancel()
    {
        onComplete(null);
    }
}
