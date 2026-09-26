using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Infrastructure.Converters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// ViewModel for the interactive WND Editor demo with placeholder window layouts.
/// </summary>
public partial class WndEditorDemoViewModel : ObservableObject
{
    private const string ToastTitleKey = "Info.Demo.Tools.WndEditor.Toast.Title";
    private const string ButtonControlType = "Button";

    private readonly INotificationService? _notificationService;
    private readonly ILocalizationService? _localizationService;
    private int _newWindowCounter;

    [ObservableProperty]
    private string? _selectedFile;

    [ObservableProperty]
    private WndDemoNode? _selectedNode;

    [ObservableProperty]
    private double _zoom = 1.0;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private int _nodeCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndEditorDemoViewModel"/> class.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="localizationService">Optional localization service for demo strings.</param>
    public WndEditorDemoViewModel(
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
    {
        _notificationService = notificationService;
        _localizationService = localizationService;
        Files = new ObservableCollection<string> { "MainMenu.wnd", "OptionsMenu.wnd" };
        RootNodes = new ObservableCollection<WndDemoNode>();
        CanvasNodes = new ObservableCollection<WndDemoNode>();
        SelectedFile = Files[0];
        StatusMessage = DemoText("Info.Demo.Tools.WndEditor.Status.Ready", "Select a window to edit its geometry.");
        ValidationMessage = DemoText("Info.Demo.Tools.WndEditor.Status.NotValidated", "Not validated yet.");
    }

    /// <summary>
    /// Gets the placeholder document files.
    /// </summary>
    public ObservableCollection<string> Files { get; }

    /// <summary>
    /// Gets the root window nodes of the active placeholder document.
    /// </summary>
    public ObservableCollection<WndDemoNode> RootNodes { get; }

    /// <summary>
    /// Gets the flattened nodes rendered on the canvas preview.
    /// </summary>
    public ObservableCollection<WndDemoNode> CanvasNodes { get; }

    /// <summary>
    /// Gets the zoom level as a display percentage.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses generated observable property Zoom in partial view model")]
    public string ZoomDisplay => $"{Math.Round(Zoom * 100)}%";

    private static bool RemoveFromParents(IEnumerable<WndDemoNode> parents, WndDemoNode target) =>
        parents.Any(parent => parent.Children.Remove(target) || RemoveFromParents(parent.Children, target));

    private static IEnumerable<WndDemoNode> Flatten(WndDemoNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    /// <summary>
    /// Creates a new placeholder document.
    /// </summary>
    [RelayCommand]
    private void NewDocument()
    {
        RootNodes.Clear();
        RootNodes.Add(new WndDemoNode("UntitledScreen", "Window", 0, 0, 800, 600));
        _newWindowCounter = 0;
        RefreshCanvas();
        SelectedNode = RootNodes[0];
        Notify("Info.Demo.Tools.WndEditor.Toast.NewMessage", "Created a new untitled layout. (Simulated)");
    }

    /// <summary>
    /// Simulates saving the placeholder document.
    /// </summary>
    [RelayCommand]
    private void SaveDocument()
    {
        Notify("Info.Demo.Tools.WndEditor.Toast.SaveMessage", "Document saved. (Simulated)");
    }

    /// <summary>
    /// Validates the placeholder document and reports the outcome.
    /// </summary>
    [RelayCommand]
    private void ValidateDocument()
    {
        var orphans = CanvasNodes.Count(n => n.Width <= 0 || n.Height <= 0);
        if (orphans > 0)
        {
            ValidationMessage = DemoText("Info.Demo.Tools.WndEditor.Status.Invalid", "Some windows have empty geometry.");
            _notificationService?.ShowWarning(
                DemoText(ToastTitleKey, "Demo"),
                ValidationMessage,
                NotificationDurations.Short);
            return;
        }

        ValidationMessage = DemoText("Info.Demo.Tools.WndEditor.Status.Valid", "Document is valid: 0 errors.");
        _notificationService?.ShowSuccess(
            DemoText(ToastTitleKey, "Demo"),
            ValidationMessage,
            NotificationDurations.Short);
    }

    /// <summary>
    /// Adds a child window to the selected node.
    /// </summary>
    [RelayCommand]
    private void AddChildWindow()
    {
        var parent = SelectedNode ?? RootNodes.FirstOrDefault();
        if (parent == null)
        {
            return;
        }

        _newWindowCounter++;
        var child = new WndDemoNode($"NewButton{_newWindowCounter}", ButtonControlType, parent.X + 20, parent.Y + 20, 120, 32);
        parent.Children.Add(child);
        RefreshCanvas();
        SelectedNode = child;
        StatusMessage = DemoText("Info.Demo.Tools.WndEditor.Status.Added", "Child window added.");
    }

    /// <summary>
    /// Deletes the selected window.
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedWindow()
    {
        if (SelectedNode == null || RootNodes.Contains(SelectedNode))
        {
            _notificationService?.ShowWarning(
                DemoText(ToastTitleKey, "Demo"),
                DemoText("Info.Demo.Tools.WndEditor.Toast.DeleteRootMessage", "Select a child window to delete. The root screen cannot be removed."),
                NotificationDurations.Short);
            return;
        }

        var removed = SelectedNode;
        if (RemoveFromParents(RootNodes, removed))
        {
            RefreshCanvas();
            SelectedNode = RootNodes.FirstOrDefault();
            StatusMessage = DemoText("Info.Demo.Tools.WndEditor.Status.Deleted", "Window deleted.");
        }
    }

    /// <summary>
    /// Simulates linking a mod folder for asset previews.
    /// </summary>
    [RelayCommand]
    private void LinkModFolder()
    {
        Notify("Info.Demo.Tools.WndEditor.Toast.LinkMessage", "Linked sample mod folder. (Simulated)");
    }

    /// <summary>
    /// Simulates importing a texture into the placeholder project.
    /// </summary>
    [RelayCommand]
    private void ImportTexture()
    {
        Notify("Info.Demo.Tools.WndEditor.Toast.ImportMessage", "Imported sample texture. (Simulated)");
    }

    /// <summary>
    /// Zooms the canvas preview in one step.
    /// </summary>
    [RelayCommand]
    private void ZoomIn()
    {
        Zoom = Math.Min(Zoom * 1.25, 2.0);
    }

    /// <summary>
    /// Zooms the canvas preview out one step.
    /// </summary>
    [RelayCommand]
    private void ZoomOut()
    {
        Zoom = Math.Max(Zoom / 1.25, 0.5);
    }

    /// <summary>
    /// Resets the canvas preview zoom.
    /// </summary>
    [RelayCommand]
    private void ResetZoom()
    {
        Zoom = 1.0;
    }

    partial void OnSelectedFileChanged(string? value)
    {
        LoadPlaceholders(value);
        SelectedNode = RootNodes.FirstOrDefault();
    }

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomDisplay));
    }

    private void LoadPlaceholders(string? file)
    {
        RootNodes.Clear();
        _newWindowCounter = 0;
        if (string.Equals(file, "OptionsMenu.wnd", StringComparison.Ordinal))
        {
            var root = new WndDemoNode("OptionsScreen", "Window", 0, 0, 800, 600);
            root.Children.Add(new WndDemoNode("VideoTab", "TabButton", 40, 90, 140, 36));
            root.Children.Add(new WndDemoNode("AudioTab", "TabButton", 190, 90, 140, 36));
            root.Children.Add(new WndDemoNode("ResolutionList", "ComboBox", 40, 150, 290, 30));
            root.Children.Add(new WndDemoNode("VolumeSlider", "Slider", 40, 200, 290, 28));
            root.Children.Add(new WndDemoNode("BtnBack", ButtonControlType, 40, 520, 120, 36));
            RootNodes.Add(root);
        }
        else
        {
            var root = new WndDemoNode("MainMenuScreen", "Window", 0, 0, 800, 600);
            root.Children.Add(new WndDemoNode("TitleLabel", "StaticText", 250, 60, 300, 48));
            root.Children.Add(new WndDemoNode("BtnSinglePlayer", ButtonControlType, 300, 180, 200, 40));
            root.Children.Add(new WndDemoNode("BtnMultiPlayer", ButtonControlType, 300, 230, 200, 40));
            root.Children.Add(new WndDemoNode("BtnOptions", ButtonControlType, 300, 280, 200, 40));
            root.Children.Add(new WndDemoNode("BtnExit", ButtonControlType, 300, 330, 200, 40));
            RootNodes.Add(root);
        }

        RefreshCanvas();
    }

    private void RefreshCanvas()
    {
        CanvasNodes.Clear();
        foreach (var node in RootNodes.SelectMany(Flatten))
        {
            CanvasNodes.Add(node);
        }

        NodeCount = CanvasNodes.Count;
    }

    private void Notify(string messageKey, string messageFallback)
    {
        _notificationService?.ShowInfo(
            DemoText(ToastTitleKey, "Demo"),
            DemoText(messageKey, messageFallback),
            NotificationDurations.Short);
    }

    private string DemoText(string key, string fallback) =>
        LocalizationConverterHelper.GetLocalizedOrDefault(_localizationService, key, fallback);
}
