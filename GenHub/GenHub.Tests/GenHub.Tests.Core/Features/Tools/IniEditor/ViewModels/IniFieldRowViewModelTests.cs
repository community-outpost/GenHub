using FluentAssertions;
using GenHub.Core.Models.Tools.IniEditor;
using GenHub.Features.Tools.IniEditor.ViewModels;
using System;
using System.Collections.Generic;

namespace GenHub.Tests.Core.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// Unit tests for <see cref="IniFieldRowViewModel"/> flag editing.
/// </summary>
public sealed class IniFieldRowViewModelTests
{
    /// <summary>
    /// Verifies that adding a single flag appends it and clears the entry box.
    /// </summary>
    [Fact]
    public void AddFlag_SingleFlag_AppendsAndClearsEntry()
    {
        var row = CreateRow("SELECTABLE");

        row.AddFlag("CAN_ATTACK_GROUND");

        row.Value.Should().Be("SELECTABLE CAN_ATTACK_GROUND");
        row.ActiveFlags.Should().BeEquivalentTo("SELECTABLE", "CAN_ATTACK_GROUND");
        row.SelectedFlagToAdd.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that multi-word input is added as separate flags matching the chips shown.
    /// </summary>
    [Fact]
    public void AddFlag_MultiWordInput_AddsEachToken()
    {
        var row = CreateRow("SELECTABLE");

        row.AddFlag("INFANTRY MOBILE");

        row.Value.Should().Be("SELECTABLE INFANTRY MOBILE");
        row.ActiveFlags.Should().BeEquivalentTo("SELECTABLE", "INFANTRY", "MOBILE");
    }

    /// <summary>
    /// Verifies that adding an existing flag leaves the value unchanged.
    /// </summary>
    [Fact]
    public void AddFlag_Duplicate_KeepsValue()
    {
        var row = CreateRow("SELECTABLE");

        row.AddFlag("selectable");

        row.Value.Should().Be("SELECTABLE");
        row.ActiveFlags.Should().ContainSingle().Which.Should().Be("SELECTABLE");
    }

    /// <summary>
    /// Verifies that adding a flag routes through the write-through callbacks with the pre-edit value.
    /// </summary>
    [Fact]
    public void AddFlag_RaisesWriteThroughCallbacks()
    {
        bool changed = false;
        string? committedOldValue = null;
        string? committedNewValue = null;
        var row = CreateRow(
            "SELECTABLE",
            () => changed = true,
            (oldValue, newValue) =>
            {
                committedOldValue = oldValue;
                committedNewValue = newValue;
            });

        row.AddFlag("CAN_ATTACK_GROUND");

        changed.Should().BeTrue();
        committedOldValue.Should().Be("SELECTABLE");
        committedNewValue.Should().Be("SELECTABLE CAN_ATTACK_GROUND");
    }

    /// <summary>
    /// Verifies that a pair value without a percent keeps the whole value as the target.
    /// </summary>
    [Fact]
    public void PercentPair_ValueWithoutPercent_KeepsWholeTarget()
    {
        var row = CreatePairRow("BattleMaster");

        row.IsPercentPair.Should().BeTrue();
        row.PairTarget.Should().Be("BattleMaster");
        row.PairPercent.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that mid-edit text without a percent keeps the whole value as the target.
    /// </summary>
    [Fact]
    public void PercentPair_MidEditWithoutPercent_KeepsWholeTarget()
    {
        var row = CreatePairRow("Battle Master");

        row.PairTarget.Should().Be("Battle Master");
        row.PairPercent.Should().BeEmpty();
        row.Value.Should().Be("Battle Master");
    }

    /// <summary>
    /// Verifies that an empty target recomposes without a leading space.
    /// </summary>
    [Fact]
    public void PercentPair_EmptyTarget_RecomposesWithoutLeadingSpace()
    {
        var row = CreatePairRow("BattleMaster 50%");

        row.PairTarget = string.Empty;
        row.PairPercent = "50";

        row.Value.Should().Be("50%");
    }

    /// <summary>
    /// Verifies that a multiline value splits only its first line into the pair.
    /// </summary>
    [Fact]
    public void PercentPair_MultilineValue_UsesFirstLineForPair()
    {
        var row = CreatePairRow("BattleMaster 50%\n; comment");

        row.PairTarget.Should().Be("BattleMaster");
        row.PairPercent.Should().Be("50");
    }

    private static IniFieldRowViewModel CreateRow(string value)
    {
        return CreateRow(value, () => { }, (_, _) => { });
    }

    private static IniFieldRowViewModel CreateRow(string value, Action onChanged, Action<string, string> onEditCommitted)
    {
        return CreateRow("KindOf", value, onChanged, onEditCommitted);
    }

    private static IniFieldRowViewModel CreatePairRow(string value)
    {
        return CreateRow("ProductionTimeChange", value, () => { }, (_, _) => { });
    }

    private static IniFieldRowViewModel CreateRow(string key, string value, Action onChanged, Action<string, string> onEditCommitted)
    {
        var fields = new List<IniField> { new(key, value) };
        return new IniFieldRowViewModel(fields, 0, new IniFieldMetadata(), onChanged, onEditCommitted);
    }
}
