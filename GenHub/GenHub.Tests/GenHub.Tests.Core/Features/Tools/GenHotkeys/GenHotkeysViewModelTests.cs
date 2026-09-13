using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
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

        _mockPackageService.Setup(p => p.CreateHotkeysAddonAsync(profile, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<ContentManifest>.CreateSuccess(new ContentManifest { Name = "Hotkeys Addon" }));

        await vm.HandleAddonActionAsync();

        _mockPackageService.Verify(p => p.CreateHotkeysAddonAsync(profile, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Create Addon", vm.AddonButtonText);
    }
}
