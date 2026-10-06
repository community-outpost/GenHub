using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Services.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.Services;
using GenHub.Features.Tools.GenHotkeys.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="HotkeyTooltipResolver"/>, tooltip models, and title breakdowns.
/// </summary>
public class HotkeyTooltipResolverTests
{
    /// <summary>
    /// Verifies that known tactical commands resolve to their retail SAGE tooltip labels.
    /// </summary>
    /// <param name="hotkeyString">The command action hotkey CSF label.</param>
    /// <param name="expectedTooltipLabel">The expected resolved tooltip CSF label.</param>
    [Theory]
    [InlineData("CONTROLBAR:Sell", "CONTROLBAR:ToolTipSell")]
    [InlineData("CONTROLBAR:Stop", "CONTROLBAR:ToolTipStop")]
    [InlineData("CONTROLBAR:Guard", "CONTROLBAR:ToolTipGuard")]
    [InlineData("CONTROLBAR:AttackMove", "CONTROLBAR:ToolTipAttackMove")]
    [InlineData("CONTROLBAR:Evacuate", "CONTROLBAR:ToolTipEvacuate")]
    [InlineData("CONTROLBAR:DisarmMinesAtPosition", "CONTROLBAR:ToolTipDisarmMinesAtPosition")]
    public void ResolveTooltip_TacticalCommands_ResolvesRetailLabel(
        string hotkeyString,
        string expectedTooltipLabel)
    {
        var (resolvedLabel, resolvedText) = HotkeyTooltipResolver.ResolveTooltip(
            hotkeyString,
            iconName: "TestIcon",
            explicitTooltipString: null,
            refCsf: null);

        Assert.Equal(expectedTooltipLabel, resolvedLabel);
        Assert.Null(resolvedText);
    }

    /// <summary>
    /// Verifies that when a CSF file contains the description label, the CSF text is returned.
    /// </summary>
    [Fact]
    public void ResolveTooltip_WithCsfLookup_ReturnsCsfText()
    {
        var csf = new CsfFile();
        csf.SetString("CONTROLBAR:ToolTipStop", "Halt all current actions and orders immediately.");

        var (resolvedLabel, resolvedText) = HotkeyTooltipResolver.ResolveTooltip(
            "CONTROLBAR:Stop",
            iconName: "StopIcon",
            explicitTooltipString: null,
            refCsf: csf);

        Assert.Equal("CONTROLBAR:ToolTipStop", resolvedLabel);
        Assert.Equal("Halt all current actions and orders immediately.", resolvedText);
    }

    /// <summary>
    /// Verifies that SAGE in-game title token breakdown accurately separates the accelerator character.
    /// </summary>
    [Fact]
    public void InGameTitleBreakdown_HotkeyContainedInTitle_SplitsAccurately()
    {
        var vm = new HotkeyActionViewModel
        {
            DisplayName = "Overcharge",
            Hotkey = 'O',
        };

        vm.UpdateInGameTitleBreakdown();

        Assert.True(vm.HasHotkeyInTitle);
        Assert.Equal(string.Empty, vm.TitleBeforeHotkey);
        Assert.Equal("O", vm.TitleHotkeyChar);
        Assert.Equal("vercharge", vm.TitleAfterHotkey);
    }

    /// <summary>
    /// Verifies that SAGE in-game title token breakdown formats bracketed prefix when hotkey character is not in title.
    /// </summary>
    [Fact]
    public void InGameTitleBreakdown_HotkeyNotContainedInTitle_FormatsBracketedPrefix()
    {
        var vm = new HotkeyActionViewModel
        {
            DisplayName = "Dozer",
            Hotkey = 'X',
        };

        vm.UpdateInGameTitleBreakdown();

        Assert.False(vm.HasHotkeyInTitle);
        Assert.Equal("[", vm.TitleBeforeHotkey);
        Assert.Equal("X", vm.TitleHotkeyChar);
        Assert.Equal("] Dozer", vm.TitleAfterHotkey);
    }

    /// <summary>
    /// Verifies that profile normalization treats tooltip and title mapping keys case-insensitively.
    /// </summary>
    [Fact]
    public void HotkeyProfile_MappingDictionaries_AreCaseInsensitive()
    {
        var profile = new HotkeyProfile
        {
            Name = "Custom Test Profile",
        };

        profile.TitleMappings["controlbar:stop"] = "Custom Stop";
        profile.TooltipMappings["controlbar:tooltipstop"] = "Custom Tooltip";
        profile.CustomCameoMappings["saccomdozer"] = @"C:\Cameos\CustomDozer.png";

        profile.NormalizeComparers();

        Assert.True(profile.TitleMappings.ContainsKey("CONTROLBAR:STOP"));
        Assert.Equal("Custom Stop", profile.TitleMappings["CONTROLBAR:STOP"]);

        Assert.True(profile.TooltipMappings.ContainsKey("CONTROLBAR:TOOLTIPSTOP"));
        Assert.Equal("Custom Tooltip", profile.TooltipMappings["CONTROLBAR:TOOLTIPSTOP"]);

        Assert.True(profile.CustomCameoMappings.ContainsKey("SACCOMDOZER"));
        Assert.Equal(@"C:\Cameos\CustomDozer.png", profile.CustomCameoMappings["SACCOMDOZER"]);
    }
}
