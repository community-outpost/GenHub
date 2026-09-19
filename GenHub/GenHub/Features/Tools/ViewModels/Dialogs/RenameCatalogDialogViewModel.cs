using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Validation;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for renaming a catalog.
/// </summary>
[SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI.")]
public partial class RenameCatalogDialogViewModel(
    string currentName,
    Action<string?> onComplete) : ObservableValidator
{
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [LocalizedRequired("Tools.PublisherStudio.Validation.CatalogNameRequired", "Catalog name is required")]
    [LocalizedMinLength(1, "Tools.PublisherStudio.Validation.CatalogNameNotEmpty", "Catalog name cannot be empty")]
    private string _catalogName = currentName ?? string.Empty;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private bool _isValid;

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
        onComplete(CatalogName.Trim());
    }

    [RelayCommand]
    private void Cancel()
    {
        onComplete(null);
    }
}
