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
