using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the Scan Wizard interactive demo.
/// </summary>
public partial class ScanWizardDemoViewModel : ObservableObject
{
    private readonly INotificationService? _notificationService;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _status = "GenHub detected the following installations on your system.";

    [ObservableProperty]
    private string _confirmLabel = "Import Selected (2)";

    [ObservableProperty]
    private bool _hasSelectedItems = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanWizardDemoViewModel"/> class.
    /// </summary>
    /// <param name="notificationService">Optional notification service.</param>
    public ScanWizardDemoViewModel(INotificationService? notificationService = null)
    {
        _notificationService = notificationService;
        PopulateDefaultItems();
        UpdateState();
    }

    /// <summary>
    /// Gets the collection of detected game items.
    /// </summary>
    public ObservableCollection<ScanWizardDemoItemViewModel> Items { get; } = [];

    /// <summary>
    /// Updates confirmation label and selection state.
    /// </summary>
    public void UpdateState()
    {
        var selectedCount = Items.Count(x => x.IsSelected);
        HasSelectedItems = selectedCount > 0;
        ConfirmLabel = selectedCount > 0 ? $"Import Selected ({selectedCount})" : "Import Selected";
    }

    /// <summary>
    /// Simulates a scan for installed games.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    public async Task RescanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        Status = "Scanning registry, Steam, and EA App directories...";
        Items.Clear();

        await Task.Delay(1000);

        PopulateDefaultItems();
        UpdateState();
        Status = "GenHub detected 2 installations on your system.";
        IsScanning = false;

        _notificationService?.Show(new NotificationMessage(
            NotificationType.Success,
            "Scan Wizard",
            "Scan complete: Found 2 game installations. (Simulated)",
            3000));
    }

    /// <summary>
    /// Simulates importing selected game installations.
    /// </summary>
    [RelayCommand]
    public void Import()
    {
        var selectedCount = Items.Count(x => x.IsSelected);
        if (selectedCount == 0)
        {
            return;
        }

        _notificationService?.Show(new NotificationMessage(
            NotificationType.Success,
            "Scan Wizard",
            $"Successfully imported {selectedCount} game installation{(selectedCount == 1 ? string.Empty : "s")} into your library! (Simulated)",
            4000));
    }

    /// <summary>
    /// Simulates cancelling the scan wizard.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        _notificationService?.Show(new NotificationMessage(
            NotificationType.Info,
            "Scan Wizard",
            "Game detection wizard cancelled. (Simulated)",
            2500));
    }

    /// <summary>
    /// Populates default demo items.
    /// </summary>
    private void PopulateDefaultItems()
    {
        Items.Clear();
        Items.Add(new ScanWizardDemoItemViewModel(
            "Command & Conquer: Generals",
            @"C:\Program Files (x86)\EA Games\Command and Conquer Generals",
            "v1.08",
            "avares://GenHub/Assets/Icons/generals-icon.png",
            isSelected: true,
            onSelectionChanged: UpdateState));

        Items.Add(new ScanWizardDemoItemViewModel(
            "Command & Conquer: Generals - Zero Hour",
            @"C:\Program Files (x86)\EA Games\Command and Conquer Generals Zero Hour",
            "v1.04",
            "avares://GenHub/Assets/Icons/zerohour-icon.png",
            isSelected: true,
            onSelectionChanged: UpdateState));
    }
}
