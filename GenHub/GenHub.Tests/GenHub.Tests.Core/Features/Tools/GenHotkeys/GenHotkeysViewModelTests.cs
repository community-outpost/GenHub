using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Enums;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="GenHotkeysViewModelTests"/> class.
    /// </summary>
    public GenHotkeysViewModelTests()
    {
        _mockTechTree = new Mock<ITechTreeService>();
        _mockProfileStorage = new Mock<IHotkeyProfileStorageService>();
        _mockPackageService = new Mock<IHotkeyPackageService>();
        _mockLogger = new Mock<ILogger<GenHotkeysViewModel>>();
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
    public async Task ApplyToAllMatchingActionsAsync_PropagatesHotkeyToMatchingActionsAcrossFactions()
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
    public async Task ApplyPresetAsync_Vanilla_SetsPresetAndResetsMappings()
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
}
