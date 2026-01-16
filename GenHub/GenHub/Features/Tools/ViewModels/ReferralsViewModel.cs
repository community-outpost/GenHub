using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for managing publisher referrals (recommendations).
/// </summary>
public partial class ReferralsViewModel : ObservableObject
{
    private readonly PublisherStudioProject _project;
    private readonly PublisherStudioViewModel _parentViewModel;
    private readonly ILogger _logger;
    private readonly IPublisherStudioDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<PublisherReferral> _referrals = [];

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ReferralsViewModel"/> class.
    /// </summary>
    /// <param name="project">The publisher studio project.</param>
    /// <param name="parentViewModel">The parent view model.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="dialogService">The dialog service.</param>
    public ReferralsViewModel(
        PublisherStudioProject project,
        PublisherStudioViewModel parentViewModel,
        ILogger logger,
        IPublisherStudioDialogService dialogService)
    {
        _project = project;
        _parentViewModel = parentViewModel;
        _logger = logger;
        _dialogService = dialogService;

        LoadReferrals();
    }

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

    private void LoadReferrals()
    {
        Referrals.Clear();
        foreach (var referral in _project.Catalog.Referrals)
        {
            Referrals.Add(referral);
        }
    }

    /// <summary>
    /// Saves the edited referral back to the project.
    /// </summary>
    [RelayCommand]
    private void SaveEdit()
    {
        if (SelectedReferral == null)
        {
            return;
        }

        SelectedReferral.PublisherId = EditPublisherId.ToLowerInvariant().Trim();
        SelectedReferral.CatalogUrl = EditCatalogUrl.Trim();
        SelectedReferral.Note = string.IsNullOrWhiteSpace(EditNote) ? null : EditNote.Trim();

        _parentViewModel.MarkDirty();
        _logger.LogInformation("Updated referral: {PublisherId}", SelectedReferral.PublisherId);
    }

    /// <summary>
    /// Adds a new referral.
    /// </summary>
    [RelayCommand]
    private async Task AddReferralAsync()
    {
        var referral = await _dialogService.ShowAddReferralDialogAsync();
        if (referral != null)
        {
            _project.Catalog.Referrals.Add(referral);
            Referrals.Add(referral);
            SelectedReferral = referral;

            _parentViewModel.MarkDirty();
            _logger.LogInformation("Added referral to publisher: {PublisherId}", referral.PublisherId);
        }
    }

    /// <summary>
    /// Deletes the selected referral.
    /// </summary>
    [RelayCommand]
    private void DeleteReferral()
    {
        if (SelectedReferral == null)
        {
            return;
        }

        var publisherId = SelectedReferral.PublisherId;

        _project.Catalog.Referrals.Remove(SelectedReferral);
        Referrals.Remove(SelectedReferral);

        _parentViewModel.MarkDirty();
        _logger.LogInformation("Deleted referral: {PublisherId}", publisherId);

        SelectedReferral = Referrals.FirstOrDefault();
    }
}
