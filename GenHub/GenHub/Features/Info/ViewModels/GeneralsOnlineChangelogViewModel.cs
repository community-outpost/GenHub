using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Info;
using GenHub.Features.Info.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for displaying Generals Online patch notes.
/// </summary>
public partial class GeneralsOnlineChangelogViewModel(IGeneralsOnlinePatchNotesService patchNotesService, ILogger<GeneralsOnlineChangelogViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Gets the collection of patch notes.
    /// </summary>
    public ObservableCollection<PatchNote> PatchNotes { get; } = [];

    /// <summary>
    /// Loads the patch notes from the website.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LoadPatchNotesAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;
            PatchNotes.Clear();

            var notes = await patchNotesService.GetPatchNotesAsync();

            if (notes != null)
            {
                foreach (var note in notes)
                {
                    PatchNotes.Add(note);
                }
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = "An error occurred while loading patch notes.";
            logger.LogError(ex, "Error loading patch notes");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads the details for a specific patch note.
    /// </summary>
    /// <param name="patchNote">The patch note to load details for.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task LoadDetailsAsync(PatchNote patchNote)
    {
        if (patchNote.IsDetailsLoaded || patchNote.IsLoadingDetails) return;

        await patchNotesService.GetPatchDetailsAsync(patchNote);
    }

    /// <summary>
    /// Toggles the expansion state of a patch note and loads details if needed.
    /// </summary>
    /// <param name="patchNote">The patch note to toggle.</param>
    [RelayCommand]
    public void ToggleExpansion(PatchNote patchNote)
    {
        patchNote.IsExpanded = !patchNote.IsExpanded;

        if (patchNote.IsExpanded && !patchNote.IsDetailsLoaded)
        {
            _ = LoadDetailsAsync(patchNote);
        }
    }

    /// <summary>
    /// Opens the release on the website.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    [RelayCommand]
    public void OpenReleaseUrl(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            logger.LogWarning("Invalid or unsafe URL: {Url}", url);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open release URL: {Url}", url);
        }
    }
}
