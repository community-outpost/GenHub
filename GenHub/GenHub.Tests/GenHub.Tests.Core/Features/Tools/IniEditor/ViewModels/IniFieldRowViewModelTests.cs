using FluentAssertions;
using GenHub.Core.Models.Tools.IniEditor;
using GenHub.Features.Tools.IniEditor.ViewModels;
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

    private static IniFieldRowViewModel CreateRow(string value)
    {
        var fields = new List<IniField> { new("KindOf", value) };
        return new IniFieldRowViewModel(fields, 0, new IniFieldMetadata(), () => { }, (_, _) => { });
    }
}
