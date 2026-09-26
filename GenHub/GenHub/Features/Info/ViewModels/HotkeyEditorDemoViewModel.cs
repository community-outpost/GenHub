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
/// ViewModel for the interactive Hotkey Editor demo with a placeholder command card.
/// </summary>
public partial class HotkeyEditorDemoViewModel : ObservableObject
{
    private readonly INotificationService? _notificationService;
    private readonly ILocalizationService? _localizationService;

    [ObservableProperty]
    private string _selectedFaction = "USA";

    [ObservableProperty]
    private HotkeyDemoSlot? _selectedSlot;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private int _conflictCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyEditorDemoViewModel"/> class.
    /// </summary>
    /// <param name="notificationService">Optional notification service for demo actions.</param>
    /// <param name="localizationService">Optional localization service for demo strings.</param>
    public HotkeyEditorDemoViewModel(
        INotificationService? notificationService = null,
        ILocalizationService? localizationService = null)
    {
        _notificationService = notificationService;
        _localizationService = localizationService;
        Factions = new List<string> { "USA", "China", "GLA" };
        Slots = new ObservableCollection<HotkeyDemoSlot>();
        KeyPalette = new List<string> { "Q", "W", "E", "R", "A", "S", "D", "F", "Z", "X", "C", "V" };
        SeedPlaceholders();
        StatusMessage = DemoText("Info.Demo.Tools.HotkeyEditor.Status.Ready", "Select a slot, then pick a key to rebind it.");
        UpdateConflicts();
    }

    /// <summary>
    /// Gets the placeholder factions.
    /// </summary>
    public IReadOnlyList<string> Factions { get; }

    /// <summary>
    /// Gets the placeholder command card slots.
    /// </summary>
    public ObservableCollection<HotkeyDemoSlot> Slots { get; }

    /// <summary>
    /// Gets the assignable key palette.
    /// </summary>
    public IReadOnlyList<string> KeyPalette { get; }

    /// <summary>
    /// Gets a value indicating whether any conflicts exist.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses generated observable property ConflictCount in partial view model")]
    public bool HasConflicts => ConflictCount > 0;

    /// <summary>
    /// Assigns a key to the selected slot.
    /// </summary>
    /// <param name="key">The key to assign.</param>
    [RelayCommand]
    private void AssignKey(string? key)
    {
        if (SelectedSlot == null || string.IsNullOrEmpty(key))
        {
            return;
        }

        SelectedSlot.Hotkey = key;
        UpdateConflicts();
        StatusMessage = ConflictCount > 0
            ? DemoText("Info.Demo.Tools.HotkeyEditor.Status.Conflict", "Conflict: slots share a key. Pick distinct keys to clear it.")
            : DemoText("Info.Demo.Tools.HotkeyEditor.Status.Clear", "Key assigned. No conflicts.");
    }

    /// <summary>
    /// Clears the hotkey from the selected slot.
    /// </summary>
    [RelayCommand]
    private void ClearKey()
    {
        if (SelectedSlot == null)
        {
            return;
        }

        SelectedSlot.Hotkey = string.Empty;
        UpdateConflicts();
        StatusMessage = ConflictCount > 0
            ? DemoText("Info.Demo.Tools.HotkeyEditor.Status.Conflict", "Conflict: slots share a key. Pick distinct keys to clear it.")
            : DemoText("Info.Demo.Tools.HotkeyEditor.Status.Clear", "Key cleared. No conflicts.");
    }

    /// <summary>
    /// Simulates packaging the bindings into an addon manifest.
    /// </summary>
    [RelayCommand]
    private void CreateAddon()
    {
        _notificationService?.ShowSuccess(
            DemoText("Info.Demo.Tools.HotkeyEditor.Toast.Title", "Demo"),
            DemoText("Info.Demo.Tools.HotkeyEditor.Toast.AddonMessage", "Hotkey addon manifest created. (Simulated)"),
            NotificationDurations.Short);
    }

    partial void OnSelectedFactionChanged(string value)
    {
        SeedPlaceholders();
        SelectedSlot = null;
        UpdateConflicts();
        StatusMessage = DemoText("Info.Demo.Tools.HotkeyEditor.Status.Ready", "Select a slot, then pick a key to rebind it.");
    }

    private void SeedPlaceholders()
    {
        Slots.Clear();
        var names = new[] { "Dozer", "Ranger", "Humvee", "Crusader", "Paladin", "Ambulance", "Fire Base", "Avenger", "Tomahawk", "Comanche", "Particle Cannon", "Supply Drop" };
        var keys = new[] { "Q", "W", "E", "R", "A", "S", "D", "F", "Z", "X", "C", "Q" };
        for (var i = 0; i < names.Length; i++)
        {
            Slots.Add(new HotkeyDemoSlot(names[i], keys[i]));
        }
    }

    private void UpdateConflicts()
    {
        var duplicates = Slots
            .Where(s => !string.IsNullOrEmpty(s.Hotkey))
            .GroupBy(s => s.Hotkey, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .ToHashSet();

        foreach (var slot in Slots)
        {
            slot.HasConflict = duplicates.Contains(slot);
        }

        ConflictCount = duplicates.Count;
        OnPropertyChanged(nameof(HasConflicts));
    }

    private string DemoText(string key, string fallback) =>
        LocalizationConverterHelper.GetLocalizedOrDefault(_localizationService, key, fallback);
}
