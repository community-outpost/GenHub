using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
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
    private CancellationTokenSource? _scanCts;

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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    public async Task RescanAsync(CancellationToken cancellationToken = default)
    {
        if (IsScanning)
        {
            return;
        }

        _scanCts?.Dispose();
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        IsScanning = true;
        try
        {
            Status = "Scanning registry, Steam, and EA App directories...";

            await Task.Delay(1000, _scanCts.Token);

            PopulateDefaultItems();
            UpdateState();
            Status = "GenHub detected 2 installations on your system.";

            _notificationService?.Show(new NotificationMessage(
                NotificationType.Success,
                "Scan Wizard",
                "Scan complete: Found 2 game installations. (Simulated)",
                3000));
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled.";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
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
        if (IsScanning)
        {
            _scanCts?.Cancel();
        }

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

        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrEmpty(basePath))
        {
            basePath = @"C:\Program Files (x86)";
        }

        var generalsPath = Path.Combine(basePath, "EA Games", "Command and Conquer Generals");
        var zeroHourPath = Path.Combine(basePath, "EA Games", "Command and Conquer Generals Zero Hour");

        Items.Add(new ScanWizardDemoItemViewModel(
            "Command & Conquer: Generals",
            generalsPath,
            "v1.08",
            InfoConstants.DemoIconGenerals,
            isSelected: true,
            onSelectionChanged: UpdateState));

        Items.Add(new ScanWizardDemoItemViewModel(
            "Command & Conquer: Generals - Zero Hour",
            zeroHourPath,
            "v1.04",
            InfoConstants.DemoIconZeroHour,
            isSelected: true,
            onSelectionChanged: UpdateState));
    }
}
