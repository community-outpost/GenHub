using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for managing publisher referrals (recommendations).
/// </summary>
public partial class ReferralsViewModel(
    PublisherStudioProject project,
    PublisherStudioViewModel parentViewModel,
    ILogger logger,
    IPublisherStudioDialogService dialogService,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<PublisherReferral> _referrals = project?.Catalog?.Referrals != null
        ? [.. project.Catalog.Referrals]
        : [];

    [ObservableProperty]
    private PublisherReferral? _selectedReferral;

    // Wrapper properties for editing that properly notify changes
    [ObservableProperty]
    private string _editPublisherId = string.Empty;

    [ObservableProperty]
    private string _editCatalogUrl = string.Empty;

    [ObservableProperty]
    private string _editNote = string.Empty;

    [ObservableProperty]
    private bool _isEditing;

    partial void OnSelectedReferralChanged(PublisherReferral? value)
    {
        if (value != null)
        {
            EditPublisherId = value.PublisherId;
            EditCatalogUrl = value.CatalogUrl;
            EditNote = value.Note ?? string.Empty;
            IsEditing = true;
        }
        else
        {
            EditPublisherId = string.Empty;
            EditCatalogUrl = string.Empty;
            EditNote = string.Empty;
            IsEditing = false;
        }
    }

    /// <summary>
    /// Saves the edited referral back to the project.
    /// </summary>
    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (SelectedReferral == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EditPublisherId))
        {
            logger.LogWarning("Cannot save referral with empty publisher ID");
            notificationService?.ShowWarning(
                localizationService?.GetString("Tools.PublisherStudio.Referrals.ValidationTitle") ?? "Invalid Referral",
                localizationService?.GetString("Tools.PublisherStudio.Referrals.PublisherIdRequired") ?? "Publisher ID is required.",
                NotificationDurations.Medium);
            return;
        }

        var trimmedUrl = EditCatalogUrl?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedUrl) ||
            !Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            logger.LogWarning("Cannot save referral with invalid catalog URL: {Url}", EditCatalogUrl);
            notificationService?.ShowWarning(
                localizationService?.GetString("Tools.PublisherStudio.Referrals.ValidationTitle") ?? "Invalid Referral",
                localizationService?.GetString("Tools.PublisherStudio.Referrals.CatalogUrlInvalid") ?? "Catalog URL must be a valid http or https URL.",
                NotificationDurations.Medium);
            return;
        }

        SelectedReferral.PublisherId = EditPublisherId.ToLowerInvariant().Trim();
        SelectedReferral.CatalogUrl = trimmedUrl;
        SelectedReferral.Note = string.IsNullOrWhiteSpace(EditNote) ? null : EditNote.Trim();

        parentViewModel?.MarkDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Updated referral: {PublisherId}", SelectedReferral.PublisherId);
    }

    /// <summary>
    /// Adds a new referral.
    /// </summary>
    [RelayCommand]
    private async Task AddReferralAsync()
    {
        var referral = await dialogService.ShowAddReferralDialogAsync();
        if (referral != null)
        {
            if (project?.Catalog != null)
            {
                project.Catalog.Referrals ??= [];
                project.Catalog.Referrals.Add(referral);
            }

            Referrals.Add(referral);
            SelectedReferral = referral;

            parentViewModel?.MarkDirty();
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectAsync();
            }

            logger.LogInformation("Added referral to publisher: {PublisherId}", referral.PublisherId);
        }
    }

    /// <summary>
    /// Deletes the selected referral.
    /// </summary>
    [RelayCommand]
    private async Task DeleteReferralAsync()
    {
        if (SelectedReferral == null)
        {
            return;
        }

        var publisherId = SelectedReferral.PublisherId;

        project?.Catalog?.Referrals?.Remove(SelectedReferral);
        Referrals.Remove(SelectedReferral);

        parentViewModel?.MarkDirty();
        if (parentViewModel != null)
        {
            await parentViewModel.SaveProjectAsync();
        }

        logger.LogInformation("Deleted referral: {PublisherId}", publisherId);

        SelectedReferral = Referrals.FirstOrDefault();
    }

    /// <summary>
    /// Reloads the referrals list from the current project.
    /// </summary>
    public void LoadFromProject()
    {
        Referrals.Clear();
        var refs = project?.Referrals ?? project?.Catalog?.Referrals;
        if (refs != null)
        {
            foreach (var r in refs)
            {
                Referrals.Add(r);
            }
        }
        SelectedReferral = Referrals.FirstOrDefault();
    }
}
