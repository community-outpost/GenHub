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
/// ViewModel for the workspace filesystem magic demo.
/// </summary>
public partial class WorkspaceDemoViewModel : ObservableObject
{
    private readonly INotificationService? _notificationService;

    [ObservableProperty]
    private string _status = "Ready to simulate...";

    [ObservableProperty]
    private bool _isSimulating;

    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceDemoViewModel"/> class.
    /// </summary>
    /// <param name="notificationService">Optional notification service.</param>
    public WorkspaceDemoViewModel(INotificationService? notificationService = null)
    {
        _notificationService = notificationService;
    }

    /// <summary>
    /// Gets the list of operations being simulated.
    /// </summary>
    public ObservableCollection<WorkspaceOperation> Operations { get; } = [];

    /// <summary>
    /// Starts the workspace simulation process.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    public async Task StartSimulationAsync()
    {
        if (IsSimulating)
        {
            return;
        }

        IsSimulating = true;
        Operations.Clear();
        Progress = 0;
        Status = "Initializing Workspace...";

        await Task.Delay(800);

        var steps = new (string Status, string Source, string Target, string Type)[]
        {
            ("Linking core game files...", "C:\\Games\\Zero Hour\\generals.exe", "Workspaces\\Profile1\\generals.exe", "Hardlink"),
            ("Linking data archives...", "C:\\Games\\Zero Hour\\Data\\INI.big", "Workspaces\\Profile1\\Data\\INI.big", "Hardlink"),
            ("Mapping user data folder...", "Documents\\ZH Data\\Maps", "Workspaces\\Profile1\\Data\\Maps", "Symlink / Junction"),
            ("Injecting Mod files...", "Mods\\RotR\\art.big", "Workspaces\\Profile1\\art.big", "Hardlink"),
            ("Redirecting options...", "Profiles\\Profile1\\Options.ini", "Workspaces\\Profile1\\Options.ini", "Copy"),
        };

        for (int i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            Status = step.Status;

            Operations.Add(new WorkspaceOperation
            {
                Source = step.Source,
                Target = step.Target,
                Type = step.Type,
            });

            Progress = (double)(i + 1) / steps.Length * 100;
            await Task.Delay(600);
        }

        Status = "Workspace Ready!";
        IsSimulating = false;

        _notificationService?.Show(new NotificationMessage(
            NotificationType.Success,
            "Demo",
            "Workspace simulation complete! Notice how most files use 'Hardlinks' which take zero extra disk space.",
            5000));
    }
}
