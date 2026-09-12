using System.Collections.ObjectModel;
using GenHub.Features.Tools.GenHotkeys.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for hotkey conflict detection and mutual exclusion rules.
/// </summary>
public class GenHotkeysConflictTests
{
    /// <summary>
    /// Verifies that Daisy Cutter (Fuel Air Bomb) and MOAB sharing the same hotkey are treated as mutually exclusive upgrades, not conflicts.
    /// </summary>
    [Fact]
    public void DaisyCutter_And_Moab_SharingHotkey_AreNotConflicts()
    {
        var daisyCutter = new HotkeyActionViewModel
        {
            DisplayName = "Fuel Air Bomb",
            IconName = "USADaisyCutter",
            HotkeyString = "CONTROLBAR:DaisyCutter",
            Hotkey = 'B',
        };

        var moab = new HotkeyActionViewModel
        {
            DisplayName = "Mother of All Bombs",
            IconName = "USAMOAB",
            HotkeyString = "CONTROLBAR:MOAB",
            Hotkey = 'B',
        };

        var layout = new ObservableCollection<HotkeyActionViewModel> { daisyCutter, moab };

        var conflictCount = GenHotkeysViewModel.ValidateLayoutConflicts(layout);

        Assert.Equal(0, conflictCount);
        Assert.False(daisyCutter.IsConflict);
        Assert.Null(daisyCutter.ConflictReason);
        Assert.False(moab.IsConflict);
        Assert.Null(moab.ConflictReason);
    }

    /// <summary>
    /// Verifies that China Land Mines and Neutron Mines sharing the same hotkey are treated as mutually exclusive upgrades, not conflicts.
    /// </summary>
    [Fact]
    public void ChinaMines_And_NeutronMines_SharingHotkey_AreNotConflicts()
    {
        var landMines = new HotkeyActionViewModel
        {
            DisplayName = "Land Mines",
            IconName = "PRCLandMine",
            HotkeyString = "CONTROLBAR:UpgradeChinaMines",
            Hotkey = 'M',
        };

        var neutronMines = new HotkeyActionViewModel
        {
            DisplayName = "Neutron Mines",
            IconName = "PRCNeutronMines",
            HotkeyString = "CONTROLBAR:UpgradeEMPMines",
            Hotkey = 'M',
        };

        var layout = new ObservableCollection<HotkeyActionViewModel> { landMines, neutronMines };

        var conflictCount = GenHotkeysViewModel.ValidateLayoutConflicts(layout);

        Assert.Equal(0, conflictCount);
        Assert.False(landMines.IsConflict);
        Assert.Null(landMines.ConflictReason);
        Assert.False(neutronMines.IsConflict);
        Assert.Null(neutronMines.ConflictReason);
    }

    /// <summary>
    /// Verifies that Satellite Hack 1 and Satellite Hack 2 sharing the same hotkey are treated as mutually exclusive upgrades, not conflicts.
    /// </summary>
    [Fact]
    public void SatelliteHack1_And_SatelliteHack2_SharingHotkey_AreNotConflicts()
    {
        var hack1 = new HotkeyActionViewModel
        {
            DisplayName = "Satellite Hack I",
            IconName = "PRCSatelliteHack1",
            HotkeyString = "CONTROLBAR:UpgradeChinaSatelliteHackOne",
            Hotkey = 'H',
        };

        var hack2 = new HotkeyActionViewModel
        {
            DisplayName = "Satellite Hack II",
            IconName = "PRCSatelliteHack2",
            HotkeyString = "CONTROLBAR:UpgradeChinaSatelliteHackTwo",
            Hotkey = 'H',
        };

        var layout = new ObservableCollection<HotkeyActionViewModel> { hack1, hack2 };

        var conflictCount = GenHotkeysViewModel.ValidateLayoutConflicts(layout);

        Assert.Equal(0, conflictCount);
        Assert.False(hack1.IsConflict);
        Assert.Null(hack1.ConflictReason);
        Assert.False(hack2.IsConflict);
        Assert.Null(hack2.ConflictReason);
    }

    /// <summary>
    /// Verifies that when a third action shares the hotkey with mutually exclusive upgrades, a conflict is detected.
    /// </summary>
    [Fact]
    public void DaisyCutter_Moab_And_ThirdAction_TriggersConflict()
    {
        var daisyCutter = new HotkeyActionViewModel
        {
            DisplayName = "Fuel Air Bomb",
            IconName = "USADaisyCutter",
            HotkeyString = "CONTROLBAR:DaisyCutter",
            Hotkey = 'B',
        };

        var moab = new HotkeyActionViewModel
        {
            DisplayName = "Mother of All Bombs",
            IconName = "USAMOAB",
            HotkeyString = "CONTROLBAR:MOAB",
            Hotkey = 'B',
        };

        var dozer = new HotkeyActionViewModel
        {
            DisplayName = "Construction Dozer",
            IconName = "USADozer",
            HotkeyString = "CONTROLBAR:ConstructAmericaDozer",
            Hotkey = 'B',
        };

        var layout = new ObservableCollection<HotkeyActionViewModel> { daisyCutter, moab, dozer };

        var conflictCount = GenHotkeysViewModel.ValidateLayoutConflicts(layout);

        Assert.Equal(3, conflictCount);
        Assert.True(daisyCutter.IsConflict);
        Assert.True(moab.IsConflict);
        Assert.True(dozer.IsConflict);
    }

    /// <summary>
    /// Verifies that two regular actions sharing the same hotkey produce a conflict.
    /// </summary>
    [Fact]
    public void RegularActions_SharingHotkey_TriggersConflict()
    {
        var ranger = new HotkeyActionViewModel
        {
            DisplayName = "Ranger",
            IconName = "USARanger",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryRanger",
            Hotkey = 'R',
        };

        var pathfinder = new HotkeyActionViewModel
        {
            DisplayName = "Pathfinder",
            IconName = "USAPathfinder",
            HotkeyString = "CONTROLBAR:ConstructAmericaInfantryPathfinder",
            Hotkey = 'R',
        };

        var layout = new ObservableCollection<HotkeyActionViewModel> { ranger, pathfinder };

        var conflictCount = GenHotkeysViewModel.ValidateLayoutConflicts(layout);

        Assert.Equal(2, conflictCount);
        Assert.True(ranger.IsConflict);
        Assert.True(pathfinder.IsConflict);
    }

    /// <summary>
    /// Verifies that China Radar and Steal Cash Hack sharing hotkey 'D' are not treated as conflicts
    /// because Radar vanishes upon research in early-game and Cash Hack is a Rank 3 General Power.
    /// </summary>
    [Fact]
    public void ChinaRadar_And_CashHack_SharingHotkey_AreNotConflicts()
    {
        var radar = new HotkeyActionViewModel
        {
            DisplayName = "Radar",
            IconName = "PRCRadarUpgrade",
            HotkeyString = "CONTROLBAR:UpgradeChinaRadar",
            Hotkey = 'D',
        };

        var cashHack = new HotkeyActionViewModel
        {
            DisplayName = "Cash Hack",
            IconName = "PRCBlackLotusCashHack",
            HotkeyString = "CONTROLBAR:StealCashHack",
            Hotkey = 'D',
        };

        var layout = new ObservableCollection<HotkeyActionViewModel> { radar, cashHack };

        var conflictCount = GenHotkeysViewModel.ValidateLayoutConflicts(layout);

        Assert.Equal(0, conflictCount);
        Assert.False(radar.IsConflict);
        Assert.False(cashHack.IsConflict);
    }

    /// <summary>
    /// Verifies that Timed Demo Charge and Detonate Charges sharing hotkey 'D' are not treated as conflicts
    /// because Detonate is inactive until remote charges are placed.
    /// </summary>
    [Fact]
    public void TimedDemo_And_DetonateCharges_SharingHotkey_AreNotConflicts()
    {
        var timed = new HotkeyActionViewModel
        {
            DisplayName = "Timed Demo Charge",
            IconName = "USATimedDemoCharge",
            HotkeyString = "CONTROLBAR:TimedDemoCharge",
            Hotkey = 'D',
        };

        var detonate = new HotkeyActionViewModel
        {
            DisplayName = "Detonate Charges",
            IconName = "USADetonateCharges",
            HotkeyString = "CONTROLBAR:DetonateCharges",
            Hotkey = 'D',
        };

        var layout = new ObservableCollection<HotkeyActionViewModel> { timed, detonate };

        var conflictCount = GenHotkeysViewModel.ValidateLayoutConflicts(layout);

        Assert.Equal(0, conflictCount);
        Assert.False(timed.IsConflict);
        Assert.False(detonate.IsConflict);
    }

    /// <summary>
    /// Verifies that Black Lotus Capture Building and Cash Hack sharing 'C' are detected as real conflicts
    /// (the 2003 retail EA bug where Cash Hack is shadowed on the keyboard).
    /// </summary>
    [Fact]
    public void BlackLotus_Capture_And_CashHack_AreRealConflicts()
    {
        var capture = new HotkeyActionViewModel
        {
            DisplayName = "Capture Building",
            IconName = "PRCBlackLotusCaptureBuilding",
            HotkeyString = "CONTROLBAR:CaptureBuilding",
            Hotkey = 'C',
        };

        var cashHack = new HotkeyActionViewModel
        {
            DisplayName = "Cash Hack",
            IconName = "PRCBlackLotusCashHack",
            HotkeyString = "CONTROLBAR:CashHack",
            Hotkey = 'C',
        };

        var layout = new ObservableCollection<HotkeyActionViewModel> { capture, cashHack };

        var conflictCount = GenHotkeysViewModel.ValidateLayoutConflicts(layout);

        Assert.Equal(2, conflictCount);
        Assert.True(capture.IsConflict);
        Assert.True(cashHack.IsConflict);
    }
}
