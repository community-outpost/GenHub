using Avalonia.Headless.XUnit;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="GenHotkeysViewModel"/> hotkey assignment, bulk application, presets, and conflict navigation.
/// </summary>
public class GenHotkeysViewModelTests
{
    private readonly Mock<ITechTreeService> _mockTechTree;
    private readonly Mock<IHotkeyProfileStorageService> _mockProfileStorage;
    private readonly Mock<IHotkeyPackageService> _mockPackageService;
    private readonly Mock<ILogger<GenHotkeysViewModel>> _mockLogger;
    private readonly Mock<IDialogService> _mockDialogService;
    private readonly Mock<INotificationService> _mockNotificationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenHotkeysViewModelTests"/> class.
    /// </summary>
    public GenHotkeysViewModelTests()
    {
        _mockTechTree = new Mock<ITechTreeService>();
        _mockProfileStorage = new Mock<IHotkeyProfileStorageService>();
        _mockPackageService = new Mock<IHotkeyPackageService>();
        _mockLogger = new Mock<ILogger<GenHotkeysViewModel>>();
        _mockDialogService = new Mock<IDialogService>();
        _mockNotificationService = new Mock<INotificationService>();
    }

    /// <summary>
    /// Verifies that ResetKeyToDefault restores default hotkey and removes custom mapping from the profile.
    /// </summary>
    [Fact]
    public void ResetKeyToDefault_RestoresDefaultHotkey_AndClearsProfileMapping()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var profile = new HotkeyProfile { Name = "Test Profile" };
        profile.KeyMappings["CONTROLBAR:ConstructAmericaInfantryRanger"] = 'F';
        vm.SelectedProfile = profile;

        var action = new HotkeyActionViewModel
        {
            DisplayName = "Ranger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRanger",
            DefaultHotkey = 'R',
            Hotkey = 'F',
        };

        vm.SelectAction(action);
        vm.ResetKeyToDefault();

        Assert.Equal('R', action.Hotkey);
        Assert.DoesNotContain("CONTROLBAR:ConstructAmericaInfantryRanger", profile.KeyMappings.Keys);
        Assert.DoesNotContain("CONTROLBAR:ConstructAmericaInfantryRanger", profile.ClearedKeys);
    }

    /// <summary>
    /// Verifies that ApplyToAllMatchingActionsAsync applies the selected hotkey across all matching actions.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ApplyToAllMatchingActionsAsync_PropagatesHotkeyToMatchingActionsAcrossFactionsAsync()
    {
        var rangerUsa = new HotkeyAction
        {
            DisplayName = "Ranger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRanger",
            DefaultHotkey = 'R',
        };
        var unitUsa = new HotkeyGameObject
        {
            Name = "AmericaBarracks",
            DisplayName = "USA Barracks",
            KeyboardLayouts = [[rangerUsa]],
        };
        var factionUsa = new HotkeyFaction
        {
            ShortName = "USA",
            DisplayName = "USA",
            GameObjects = [unitUsa],
        };

        var rangerAirForce = new HotkeyAction
        {
            DisplayName = "Ranger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRangerAir",
            DefaultHotkey = 'R',
        };
        var unitAir = new HotkeyGameObject
        {
            Name = "AmericaAirBarracks",
            DisplayName = "Air Force Barracks",
            KeyboardLayouts = [[rangerAirForce]],
        };
        var factionAir = new HotkeyFaction
        {
            ShortName = "AIR",
            DisplayName = "USA Air Force",
            GameObjects = [unitAir],
        };

        _mockTechTree.Setup(t => t.LoadTechTreeAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([factionUsa, factionAir]);

        var profile = new HotkeyProfile { Name = "Test" };
        _mockProfileStorage.Setup(p => p.GetProfilesAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([profile]);

        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        await vm.InitializeAsync(CancellationToken.None);

        var actionVm = new HotkeyActionViewModel
        {
            DisplayName = "Ranger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRanger",
            Hotkey = 'F',
        };

        vm.SelectAction(actionVm);
        await vm.ApplyToAllMatchingActionsAsync(CancellationToken.None);

        Assert.Equal('F', profile.KeyMappings["CONTROLBAR:ConstructAmericaInfantryRanger"]);
        Assert.Equal('F', profile.KeyMappings["CONTROLBAR:ConstructAmericaInfantryRangerAir"]);
    }

    /// <summary>
    /// Verifies that SelectNextConflict cycles through conflicting actions in the current view.
    /// </summary>
    [Fact]
    public void SelectNextConflict_CyclesThroughConflictingActions()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var profile = new HotkeyProfile { Name = "Test" };
        vm.SelectedProfile = profile;

        var act1 = new HotkeyActionViewModel { DisplayName = "Action 1", Hotkey = 'A', IsConflict = true };
        var act2 = new HotkeyActionViewModel { DisplayName = "Action 2", Hotkey = 'A', IsConflict = true };
        var act3 = new HotkeyActionViewModel { DisplayName = "Action 3", Hotkey = 'B', IsConflict = false };

        var gameObj = new HotkeyGameObjectViewModel
        {
            DisplayName = "Test Object",
        };
        gameObj.Layouts.Add(new ObservableCollection<HotkeyActionViewModel> { act1, act2, act3 });

        vm.FilteredGameObjects.Add(gameObj);

        vm.SelectNextConflict();
        Assert.Same(act1, vm.SelectedAction);

        vm.SelectNextConflict();
        Assert.Same(act2, vm.SelectedAction);

        vm.SelectNextConflict();
        Assert.Same(act1, vm.SelectedAction);
    }

    /// <summary>
    /// Verifies that ApplyPresetAsync with Vanilla preset resets mappings and sets BasePreset.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ApplyPresetAsync_Vanilla_SetsPresetAndResetsMappingsAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var profile = new HotkeyProfile { Name = "Test" };
        profile.KeyMappings["SOME_KEY"] = 'Z';
        vm.SelectedProfile = profile;

        await vm.ApplyPresetAsync(GenHotkeysConstants.PresetVanilla, CancellationToken.None);

        Assert.Equal(GenHotkeysConstants.PresetVanilla, profile.BasePreset);
        Assert.Empty(profile.KeyMappings);
        Assert.Empty(profile.ClearedKeys);
    }

    /// <summary>
    /// Verifies that ApplyPresetAsync with Leikeze extracts mappings, and switching back to Vanilla clears mappings and restores default hotkeys.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task ApplyPresetAsync_SwitchBetweenLeikezeAndVanilla_RestoresDefaultHotkeysAsync()
    {
        var dozerAction = new HotkeyAction
        {
            DisplayName = "Construction Dozer",
            HotkeyString = "CONTROLBAR:ConstructAmericaDozer",
            DefaultHotkey = 'D',
            Hotkey = 'D',
        };
        var cc = new HotkeyGameObject
        {
            Name = "AmericaCommandCenter",
            DisplayName = "USA Command Center",
            KeyboardLayouts = [[dozerAction]],
        };
        var factionUsa = new HotkeyFaction
        {
            ShortName = "USA",
            DisplayName = "USA",
            GameObjects = [cc],
        };

        _mockTechTree.Setup(t => t.LoadTechTreeAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([factionUsa]);

        var profile = new HotkeyProfile { Name = "Test Profile", TargetGame = GameType.ZeroHour };
        _mockProfileStorage.Setup(p => p.GetProfilesAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([profile]);
        _mockProfileStorage.Setup(p => p.SaveProfileAsync(It.IsAny<HotkeyProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        await vm.InitializeAsync(CancellationToken.None);

        var actionVm = vm.FilteredGameObjects.SelectMany(g => g.Layouts).SelectMany(l => l)
            .First(a => a.HotkeyString == "CONTROLBAR:ConstructAmericaDozer");

        // Initial state: Vanilla defaults
        Assert.Equal('D', actionVm.DefaultHotkey);
        Assert.Equal('D', actionVm.Hotkey);

        // Apply Leikeze preset: Dozer hotkey becomes 'F', DefaultHotkey remains 'D'
        await vm.ApplyPresetAsync(GenHotkeysConstants.PresetLeikeze, CancellationToken.None);

        Assert.Equal(GenHotkeysConstants.PresetLeikeze, profile.BasePreset);
        Assert.Equal('F', profile.KeyMappings["CONTROLBAR:ConstructAmericaDozer"]);
        Assert.Equal('F', actionVm.Hotkey);
        Assert.Equal('D', actionVm.DefaultHotkey);

        // Apply Vanilla preset: KeyMappings cleared, Dozer hotkey restored to 'D', DefaultHotkey remains 'D'
        await vm.ApplyPresetAsync(GenHotkeysConstants.PresetVanilla, CancellationToken.None);

        Assert.Equal(GenHotkeysConstants.PresetVanilla, profile.BasePreset);
        Assert.Empty(profile.KeyMappings);
        Assert.Equal('D', actionVm.Hotkey);
        Assert.Equal('D', actionVm.DefaultHotkey);
    }

    /// <summary>
    /// Verifies that RenameCurrentProfileAsync updates the profile name and preserves ComboBox selection.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RenameCurrentProfileAsync_PreservesSelectedProfileAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var profile = new HotkeyProfile { Name = "Old Name" };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;
        vm.RenameProfileText = "New Name";

        _mockProfileStorage.Setup(s => s.SaveProfileAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);

        await vm.RenameCurrentProfileAsync();

        Assert.Equal("New Name", profile.Name);
        Assert.Same(profile, vm.SelectedProfile);
    }

    /// <summary>
    /// Verifies that RenameCurrentProfileAsync re-sorts profiles and refreshes selection.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RenameCurrentProfileAsync_SortsProfilesAndRefreshesSelectionAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var profileA = new HotkeyProfile { Name = "Alpha" };
        var profileB = new HotkeyProfile { Name = "Beta" };
        vm.Profiles.Add(profileA);
        vm.Profiles.Add(profileB);
        vm.SelectedProfile = profileA;
        vm.RenameProfileText = "Zeta";

        _mockProfileStorage.Setup(s => s.SaveProfileAsync(profileA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profileA);

        await vm.RenameCurrentProfileAsync();

        Assert.Equal("Zeta", profileA.Name);
        Assert.Same(profileA, vm.SelectedProfile);
        Assert.Equal("Beta", vm.Profiles[0].Name);
        Assert.Equal("Zeta", vm.Profiles[1].Name);
        Assert.Equal("Zeta", vm.RenameProfileText);
    }

    /// <summary>
    /// Verifies that CreateNewProfileAsync adds a sorted profile and selects it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateNewProfileAsync_AddsSortsAndSelectsNewProfileAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var existing = new HotkeyProfile { Name = "Profile B" };
        vm.Profiles.Add(existing);
        vm.SelectedProfile = existing;
        vm.NewProfileName = "Profile A";

        _mockProfileStorage.Setup(s => s.SaveProfileAsync(It.IsAny<HotkeyProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HotkeyProfile p, CancellationToken _) => p);

        await vm.CreateNewProfileAsync();

        Assert.Equal(2, vm.Profiles.Count);
        Assert.Equal("Profile A", vm.Profiles[0].Name);
        Assert.Equal("Profile B", vm.Profiles[1].Name);
        Assert.NotNull(vm.SelectedProfile);
        Assert.Equal("Profile A", vm.SelectedProfile.Name);
        Assert.Empty(vm.NewProfileName);
        Assert.Equal("Profile A", vm.RenameProfileText);
    }

    /// <summary>
    /// Verifies that CreateNewProfileAsync does not create duplicate profiles and sets status message.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateNewProfileAsync_WhenNameAlreadyExists_SetsStatusMessageAndDoesNotCreateAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var existing = new HotkeyProfile { Name = "Profile A" };
        vm.Profiles.Add(existing);
        vm.SelectedProfile = existing;
        vm.NewProfileName = "profile a";

        await vm.CreateNewProfileAsync();

        Assert.Single(vm.Profiles);
        Assert.Contains("already exists", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that CreateNewProfileAsync does not add a profile if saving returns null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateNewProfileAsync_WhenSaveReturnsNull_DoesNotAddProfileAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        vm.Dispose();
        vm.NewProfileName = "New Profile";

        await vm.CreateNewProfileAsync();

        Assert.Empty(vm.Profiles);
        Assert.Contains("Failed to save profile", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that RenameCurrentProfileAsync does not allow duplicate profile names.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RenameCurrentProfileAsync_WhenNameAlreadyExists_SetsStatusMessageAndDoesNotRenameAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var profileA = new HotkeyProfile { Name = "Profile A" };
        var profileB = new HotkeyProfile { Name = "Profile B" };
        vm.Profiles.Add(profileA);
        vm.Profiles.Add(profileB);
        vm.SelectedProfile = profileA;
        vm.RenameProfileText = "profile b";

        await vm.RenameCurrentProfileAsync();

        Assert.Equal("Profile A", profileA.Name);
        Assert.Contains("already exists", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that RenameCurrentProfileAsync sets status message when new name is empty or whitespace.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RenameCurrentProfileAsync_WhenNameIsEmpty_SetsStatusMessageAndDoesNotRenameAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var profileA = new HotkeyProfile { Name = "Profile A" };
        vm.Profiles.Add(profileA);
        vm.SelectedProfile = profileA;
        vm.RenameProfileText = "   ";

        await vm.RenameCurrentProfileAsync();

        Assert.Equal("Profile A", profileA.Name);
        Assert.Contains("cannot be empty", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that DeleteCurrentProfileAsync prompts confirmation and deletes when confirmed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteCurrentProfileAsync_WhenConfirmed_DeletesProfileAsync()
    {
        _mockDialogService.Setup(d => d.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(true);

        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object,
            dialogService: _mockDialogService.Object);

        var profile1 = new HotkeyProfile { Name = "Profile 1" };
        var profile2 = new HotkeyProfile { Name = "Profile 2" };
        vm.Profiles.Add(profile1);
        vm.Profiles.Add(profile2);
        vm.SelectedProfile = profile1;

        _mockProfileStorage.Setup(s => s.DeleteProfileAsync(profile1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await vm.DeleteCurrentProfileAsync();

        Assert.DoesNotContain(profile1, vm.Profiles);
        Assert.Same(profile2, vm.SelectedProfile);
        _mockDialogService.Verify(d => d.ShowConfirmationAsync("Delete Profile", It.IsAny<string>(), "Delete", "Cancel", null), Times.Once);
    }

    /// <summary>
    /// Verifies that DeleteCurrentProfileAsync prompts confirmation and cancels without deleting when rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteCurrentProfileAsync_WhenCancelled_DoesNotDeleteProfileAsync()
    {
        _mockDialogService.Setup(d => d.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(false);

        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object,
            dialogService: _mockDialogService.Object);

        var profile1 = new HotkeyProfile { Name = "Profile 1" };
        var profile2 = new HotkeyProfile { Name = "Profile 2" };
        vm.Profiles.Add(profile1);
        vm.Profiles.Add(profile2);
        vm.SelectedProfile = profile1;

        await vm.DeleteCurrentProfileAsync();

        Assert.Contains(profile1, vm.Profiles);
        Assert.Same(profile1, vm.SelectedProfile);
        _mockProfileStorage.Verify(s => s.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that DeleteCurrentProfileAsync fails closed and does not delete when dialog service is null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteCurrentProfileAsync_WhenDialogServiceIsNull_DoesNotDeleteProfileAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object,
            dialogService: null);

        var profile1 = new HotkeyProfile { Name = "Profile 1" };
        var profile2 = new HotkeyProfile { Name = "Profile 2" };
        vm.Profiles.Add(profile1);
        vm.Profiles.Add(profile2);
        vm.SelectedProfile = profile1;

        await vm.DeleteCurrentProfileAsync();

        Assert.Contains(profile1, vm.Profiles);
        Assert.Same(profile1, vm.SelectedProfile);
        _mockProfileStorage.Verify(s => s.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that HandleAddonActionAsync always invokes ExportAddonAsync and creates the addon.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task HandleAddonActionAsync_AlwaysExportsAddonAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        var profile = new HotkeyProfile { Name = "Custom Profile" };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        _mockPackageService.Setup(p => p.CreateHotkeysAddonAsync(profile, It.IsAny<IProgress<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<ContentManifest>.CreateSuccess(new ContentManifest { Name = "Hotkeys Addon" }));

        Assert.Equal("Create Addon", vm.AddonButtonText);

        await vm.HandleAddonActionAsync();

        _mockPackageService.Verify(p => p.CreateHotkeysAddonAsync(profile, It.IsAny<IProgress<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Update Addon", vm.AddonButtonText);
    }

    /// <summary>
    /// Verifies that HandleAddonActionAsync passes AddonManifestId when updating an existing addon.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task HandleAddonActionAsync_WhenProfileHasManifestId_UpdatesAddonAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        const string manifestId = "1.108.local.addon.hotkeys";
        var profile = new HotkeyProfile { Name = "Custom Profile", AddonManifestId = manifestId };
        vm.Profiles.Add(profile);
        vm.SelectedProfile = profile;

        _mockPackageService.Setup(p => p.CreateHotkeysAddonAsync(profile, It.IsAny<IProgress<string>>(), manifestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<ContentManifest>.CreateSuccess(new ContentManifest { Id = manifestId, Name = "Hotkeys Addon" }));

        Assert.Equal("Create Addon", vm.AddonButtonText);

        await vm.HandleAddonActionAsync();

        _mockPackageService.Verify(p => p.CreateHotkeysAddonAsync(profile, It.IsAny<IProgress<string>>(), manifestId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Update Addon", vm.AddonButtonText);
    }

    /// <summary>
    /// Verifies that AssignKey assigns the hotkey and dispatches a success notification toast.
    /// </summary>
    [Fact]
    public void AssignKey_AssignsHotkey_AndDispatchesNotificationToast()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object,
            notificationService: _mockNotificationService.Object);

        var profile = new HotkeyProfile { Name = "Test Profile" };
        vm.SelectedProfile = profile;

        var action = new HotkeyActionViewModel
        {
            DisplayName = "Ranger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRanger",
            DefaultHotkey = 'R',
            Hotkey = 'R',
        };

        vm.SelectAction(action);
        vm.AssignKey('g');

        Assert.Equal('G', action.Hotkey);
        Assert.Equal('G', profile.KeyMappings["CONTROLBAR:ConstructAmericaInfantryRanger"]);
        _mockNotificationService.Verify(
            n => n.ShowSuccess("Hotkey Assigned", It.Is<string>(s => s.Contains("Assigned 'G' to 'Ranger'")), NotificationDurations.Short, false),
            Times.Once);
    }

    /// <summary>
    /// Verifies that ApplyToAllMatchingActionsAsync applies hotkey across matching actions and dispatches a success notification toast.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyToAllMatchingActionsAsync_PropagatesHotkey_AndDispatchesNotificationToastAsync()
    {
        var rangerUsa = new HotkeyAction
        {
            DisplayName = "Ranger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRanger",
            DefaultHotkey = 'R',
        };
        var unitUsa = new HotkeyGameObject
        {
            Name = "AmericaBarracks",
            DisplayName = "USA Barracks",
            KeyboardLayouts = [[rangerUsa]],
        };
        var factionUsa = new HotkeyFaction
        {
            ShortName = "USA",
            DisplayName = "USA",
            GameObjects = [unitUsa],
        };

        _mockTechTree.Setup(t => t.LoadTechTreeAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([factionUsa]);

        var profile = new HotkeyProfile { Name = "Test" };
        _mockProfileStorage.Setup(p => p.GetProfilesAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([profile]);

        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object,
            notificationService: _mockNotificationService.Object);

        await vm.InitializeAsync(CancellationToken.None);

        var actionVm = new HotkeyActionViewModel
        {
            DisplayName = "Ranger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRanger",
            Hotkey = 'F',
        };

        vm.SelectAction(actionVm);
        await vm.ApplyToAllMatchingActionsAsync(CancellationToken.None);

        _mockNotificationService.Verify(
            n => n.ShowSuccess("Hotkey Applied to All", It.Is<string>(s => s.Contains("Applied hotkey 'F'")), NotificationDurations.Short, false),
            Times.Once);
    }

    /// <summary>
    /// Verifies that ClearKey clears the hotkey and dispatches an info notification toast.
    /// </summary>
    [Fact]
    public void ClearKey_ClearsHotkey_AndDispatchesNotificationToast()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object,
            notificationService: _mockNotificationService.Object);

        var profile = new HotkeyProfile { Name = "Test Profile" };
        profile.KeyMappings["CONTROLBAR:ConstructAmericaInfantryRanger"] = 'F';
        vm.SelectedProfile = profile;

        var action = new HotkeyActionViewModel
        {
            DisplayName = "Ranger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRanger",
            DefaultHotkey = 'R',
            Hotkey = 'F',
        };

        vm.SelectAction(action);
        vm.ClearKey();

        Assert.Null(action.Hotkey);
        _mockNotificationService.Verify(
            n => n.ShowInfo("Hotkey Cleared", It.Is<string>(s => s.Contains("Cleared hotkey for 'Ranger'")), NotificationDurations.Short, false),
            Times.Once);
    }

    /// <summary>
    /// Verifies that ResetKeyToDefault resets the hotkey and dispatches an info notification toast.
    /// </summary>
    [Fact]
    public void ResetKeyToDefault_ResetsHotkey_AndDispatchesNotificationToast()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object,
            notificationService: _mockNotificationService.Object);

        var profile = new HotkeyProfile { Name = "Test Profile" };
        profile.KeyMappings["CONTROLBAR:ConstructAmericaInfantryRanger"] = 'F';
        vm.SelectedProfile = profile;

        var action = new HotkeyActionViewModel
        {
            DisplayName = "Ranger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRanger",
            DefaultHotkey = 'R',
            Hotkey = 'F',
        };

        vm.SelectAction(action);
        vm.ResetKeyToDefault();

        Assert.Equal('R', action.Hotkey);
        _mockNotificationService.Verify(
            n => n.ShowInfo("Hotkey Reset", It.Is<string>(s => s.Contains("Reset 'Ranger' to default hotkey")), NotificationDurations.Short, false),
            Times.Once);
    }

    /// <summary>
    /// Verifies that SelectFactionGroup filters the Factions collection to only factions belonging to the selected group.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectFactionGroup_FiltersFactionsCollection_ToSelectedGroup()
    {
        var factionUsa = new HotkeyFaction { ShortName = "USA", DisplayName = "USA" };
        var factionAir = new HotkeyFaction { ShortName = "AIR", DisplayName = "AIR" };
        var factionChina = new HotkeyFaction { ShortName = "CHINA", DisplayName = "China" };
        var factionTank = new HotkeyFaction { ShortName = "TANK", DisplayName = "Tank" };
        var factionGla = new HotkeyFaction { ShortName = "GLA", DisplayName = "GLA" };
        var factionTox = new HotkeyFaction { ShortName = "TOX", DisplayName = "Tox" };

        _mockTechTree.Setup(t => t.LoadTechTreeAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([factionUsa, factionAir, factionChina, factionTank, factionGla, factionTox]);

        var profile = new HotkeyProfile { Name = "Test Profile", TargetGame = GameType.ZeroHour };
        _mockProfileStorage.Setup(p => p.GetProfilesAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([profile]);

        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        await vm.InitializeAsync(CancellationToken.None);

        // Initial default group is USA
        Assert.Equal(HotkeyFaction.UsaGroup, vm.SelectedFactionGroup);
        Assert.Equal(2, vm.Factions.Count);
        Assert.Contains(factionUsa, vm.Factions);
        Assert.Contains(factionAir, vm.Factions);
        Assert.Equal(factionUsa, vm.SelectedFaction);

        // Switch to China
        vm.SelectFactionGroup(HotkeyFaction.ChinaGroup);
        Assert.Equal(HotkeyFaction.ChinaGroup, vm.SelectedFactionGroup);
        Assert.Equal(2, vm.Factions.Count);
        Assert.Contains(factionChina, vm.Factions);
        Assert.Contains(factionTank, vm.Factions);
        Assert.Equal(factionChina, vm.SelectedFaction);

        // Switch to GLA
        vm.SelectFactionGroup(HotkeyFaction.GlaGroup);
        Assert.Equal(HotkeyFaction.GlaGroup, vm.SelectedFactionGroup);
        Assert.Equal(2, vm.Factions.Count);
        Assert.Contains(factionGla, vm.Factions);
        Assert.Contains(factionTox, vm.Factions);
        Assert.Equal(factionGla, vm.SelectedFaction);
    }

    /// <summary>
    /// Verifies that changing SelectedFaction directly updates SelectedFactionGroup to match.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectedFactionChanged_UpdatesSelectedFactionGroup()
    {
        var factionUsa = new HotkeyFaction { ShortName = "USA", DisplayName = "USA" };
        var factionChina = new HotkeyFaction { ShortName = "CHINA", DisplayName = "China" };

        _mockTechTree.Setup(t => t.LoadTechTreeAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([factionUsa, factionChina]);

        var profile = new HotkeyProfile { Name = "Test Profile", TargetGame = GameType.ZeroHour };
        _mockProfileStorage.Setup(p => p.GetProfilesAsync(It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([profile]);

        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        await vm.InitializeAsync(CancellationToken.None);

        Assert.Equal(HotkeyFaction.UsaGroup, vm.SelectedFactionGroup);

        // Directly set SelectedFaction to a China faction (e.g. via conflict navigation)
        vm.SelectedFaction = factionChina;

        Assert.Equal(HotkeyFaction.ChinaGroup, vm.SelectedFactionGroup);
        Assert.Contains(factionChina, vm.Factions);
        Assert.Equal(factionChina, vm.SelectedFaction);
    }

    /// <summary>
    /// Verifies that AvailableFactionGroups contains USA, China, and GLA.
    /// </summary>
    [Fact]
    public void AvailableFactionGroups_ContainsExpectedGroups()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object);

        Assert.Equal(3, vm.AvailableFactionGroups.Count);
        Assert.Contains(HotkeyFaction.UsaGroup, vm.AvailableFactionGroups);
        Assert.Contains(HotkeyFaction.ChinaGroup, vm.AvailableFactionGroups);
        Assert.Contains(HotkeyFaction.GlaGroup, vm.AvailableFactionGroups);
    }

    /// <summary>
    /// Verifies that ApplyCustomCameoAsync applies a custom image, synchronizes across matching actions and game objects,
    /// updates the profile, and dispatches a success notification toast.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ApplyCustomCameoAsync_WithValidImage_PropagatesToMatchingActionsAndGameObjectsAsync()
    {
        var tempImageFile = Path.Combine(Path.GetTempPath(), $"genhub_test_cameo_vm_{Guid.NewGuid():N}.png");
        try
        {
            using (var testImg = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(60, 48))
            {
                await SixLabors.ImageSharp.ImageExtensions.SaveAsPngAsync(testImg, tempImageFile);
            }

            using var vm = new GenHotkeysViewModel(
                _mockTechTree.Object,
                _mockProfileStorage.Object,
                _mockPackageService.Object,
                _mockLogger.Object,
                notificationService: _mockNotificationService.Object);

            var profile = new HotkeyProfile { Name = "Cameo Test Profile" };
            vm.SelectedProfile = profile;

            var action1 = new HotkeyActionViewModel
            {
                DisplayName = "Construction Dozer",
                IconName = "USADozer",
                HotkeyString = "CONTROLBAR:ConstructAmericaVehicleDozer",
            };
            var action2 = new HotkeyActionViewModel
            {
                DisplayName = "Secondary Dozer Action",
                IconName = "USADozer",
                HotkeyString = "CONTROLBAR:DozerBuild",
            };

            var unitVm = new HotkeyGameObjectViewModel
            {
                Name = "AmericaCommandCenter",
                DisplayName = "Command Center",
                IconName = "USADozer",
            };
            unitVm.Layouts.Add(new ObservableCollection<HotkeyActionViewModel> { action1, action2 });

            vm.FilteredGameObjects.Add(unitVm);

            var result = await vm.ApplyCustomCameoAsync(action1, tempImageFile);

            Assert.True(result);
            Assert.Equal(tempImageFile, action1.CustomImagePath);
            Assert.NotNull(action1.IconBitmap);
            Assert.True(action1.HasCustomImage);
            Assert.True(action1.CanResetCameo);

            Assert.Equal(tempImageFile, action2.CustomImagePath);
            Assert.NotNull(action2.IconBitmap);
            Assert.True(action2.HasCustomImage);

            Assert.NotNull(unitVm.IconBitmap);

            Assert.Equal(tempImageFile, profile.CustomCameoMappings["USADozer"]);
            Assert.Contains("Custom cameo applied", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

            _mockNotificationService.Verify(
                n => n.ShowSuccess(It.IsAny<string>(), It.Is<string>(s => s.Contains("Custom cameo applied")), null, false),
                Times.Once);
        }
        finally
        {
            if (File.Exists(tempImageFile))
            {
                File.Delete(tempImageFile);
            }
        }
    }

    /// <summary>
    /// Verifies that ApplyCustomCameoAsync rejects non-existent files without updating profile and shows an error toast.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ApplyCustomCameoAsync_WithMissingFile_DoesNotUpdateProfileAndShowsErrorToastAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object,
            notificationService: _mockNotificationService.Object);

        var profile = new HotkeyProfile { Name = "Cameo Test Profile" };
        vm.SelectedProfile = profile;

        var action = new HotkeyActionViewModel
        {
            DisplayName = "Construction Dozer",
            IconName = "USADozer",
            HotkeyString = "CONTROLBAR:ConstructAmericaVehicleDozer",
        };

        var result = await vm.ApplyCustomCameoAsync(action, "C:/nonexistent/fake_image.png");

        Assert.False(result);
        Assert.Null(action.CustomImagePath);
        Assert.False(profile.CustomCameoMappings.ContainsKey("USADozer"));
        _mockNotificationService.Verify(
            n => n.ShowError(It.IsAny<string>(), It.Is<string>(s => s.Contains("does not exist")), null, false),
            Times.Once);
    }

    /// <summary>
    /// Verifies that ResetCustomCameoAsync removes the custom cameo from profile, clears CustomImagePath,
    /// and shows an informational toast.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ResetCustomCameoAsync_RemovesCustomCameoAndRestoresDefaultAsync()
    {
        using var vm = new GenHotkeysViewModel(
            _mockTechTree.Object,
            _mockProfileStorage.Object,
            _mockPackageService.Object,
            _mockLogger.Object,
            notificationService: _mockNotificationService.Object);

        var profile = new HotkeyProfile { Name = "Cameo Test Profile" };
        profile.CustomCameoMappings["USADozer"] = "C:/some/custom.png";
        vm.SelectedProfile = profile;

        var action = new HotkeyActionViewModel
        {
            DisplayName = "Construction Dozer",
            IconName = "USADozer",
            HotkeyString = "CONTROLBAR:ConstructAmericaVehicleDozer",
            CustomImagePath = "C:/some/custom.png",
        };
        vm.SelectedAction = action;

        await vm.ResetCustomCameoAsync();

        Assert.Null(action.CustomImagePath);
        Assert.False(profile.CustomCameoMappings.ContainsKey("USADozer"));
        Assert.Contains("Reset cameo", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

        _mockNotificationService.Verify(
            n => n.ShowInfo(It.IsAny<string>(), It.Is<string>(s => s.Contains("Reset cameo")), null, false),
            Times.Once);
    }
}
