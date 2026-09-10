using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for renaming a catalog.
/// </summary>
[SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI.")]
public partial class RenameCatalogDialogViewModel : ObservableValidator
{
    private readonly Action<string> _onComplete;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Catalog name is required")]
    [MinLength(1, ErrorMessage = "Catalog name cannot be empty")]
    private string _catalogName = string.Empty;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private bool _isValid;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenameCatalogDialogViewModel"/> class.
    /// </summary>
    /// <param name="currentName">The current name of the catalog.</param>
    /// <param name="onComplete">Callback invoked with the new name or null if canceled.</param>
    public RenameCatalogDialogViewModel(string currentName, Action<string> onComplete)
    {
        _onComplete = onComplete ?? throw new ArgumentNullException(nameof(onComplete));
        _catalogName = currentName ?? string.Empty;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CatalogName))
            {
                Validate();
            }
        };

        Validate();
    }

    private void Validate()
    {
        ValidateAllProperties();
        IsValid = !HasErrors;
        ValidationError = HasErrors
            ? string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage))
            : null;
    }

    [RelayCommand]
    private void Save()
    {
        Validate();
        if (HasErrors) return;
        _onComplete(CatalogName.Trim());
    }

    [RelayCommand]
    private void Cancel()
    {
        _onComplete(null!);
    }
}
