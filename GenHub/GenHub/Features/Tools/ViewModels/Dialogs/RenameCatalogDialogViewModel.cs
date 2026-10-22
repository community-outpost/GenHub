using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the rename / edit catalog dialog.
/// </summary>
public partial class RenameCatalogDialogViewModel(
    string currentName,
    Action<RenameCatalogResult?> onComplete,
    bool canDelete = false,
    Func<Task<bool>>? onDelete = null,
    string? currentIconUrl = null,
    Func<string, Task<string?>>? onUploadImage = null) : ObservableValidator
{
    [ObservableProperty]
    [Required(ErrorMessage = "Catalog name is required")]
    [MinLength(1, ErrorMessage = "Catalog name cannot be empty")]
    [MaxLength(100, ErrorMessage = "Catalog name cannot exceed 100 characters")]
    private string _catalogName = currentName;

    [ObservableProperty]
    private string? _iconUrl = currentIconUrl;

    [ObservableProperty]
    private bool _canDelete = canDelete;

    [ObservableProperty]
    private bool _isValid = true;

    [ObservableProperty]
    private string? _validationError;

    /// <summary>
    /// Handles image drop or paste for the catalog icon.
    /// If an upload handler is provided, it uploads the file and stores the URL;
    /// otherwise it stores the local path or URL directly.
    /// </summary>
    /// <param name="fileOrUrl">The dropped file path or URL string.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task HandleIconDropAsync(string fileOrUrl)
    {
        if (string.IsNullOrWhiteSpace(fileOrUrl))
        {
            return;
        }

        var trimmed = fileOrUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            IconUrl = trimmed;
            return;
        }

        if (File.Exists(trimmed))
        {
            if (onUploadImage != null)
            {
                var uploadedUrl = await onUploadImage(trimmed);
                if (!string.IsNullOrWhiteSpace(uploadedUrl))
                {
                    IconUrl = uploadedUrl;
                }

                return;
            }

            IconUrl = trimmed;
        }
    }

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
