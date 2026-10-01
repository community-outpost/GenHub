// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using Avalonia.Controls;
using Avalonia.Input;
using FluentAssertions;
using GenHub.Features.Tools.WorldBuilder.Common;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="WbShortcuts"/>: chord lookup, multibindings, Command
/// normalization, and the text-field focus gate.
/// </summary>
public sealed class WbShortcutsTests
{
    /// <summary>
    /// Verifies representative chords resolve to the documented commands.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    /// <param name="modifiers">The active modifiers.</param>
    /// <param name="expected">The expected command.</param>
    [Theory]
    [InlineData(Key.D0, KeyModifiers.Control, WbCommand.PickAnything)]
    [InlineData(Key.D9, KeyModifiers.Control, WbCommand.PickRoads)]
    [InlineData(Key.Z, KeyModifiers.Control, WbCommand.EditUndo)]
    [InlineData(Key.Z, KeyModifiers.Control | KeyModifiers.Shift, WbCommand.EditRedo)]
    [InlineData(Key.G, KeyModifiers.Control, WbCommand.ViewShowGrid)]
    [InlineData(Key.G, KeyModifiers.Control | KeyModifiers.Shift, WbCommand.ViewSnapToGrid)]
    [InlineData(Key.F1, KeyModifiers.None, WbCommand.WaypointTool)]
    [InlineData(Key.F5, KeyModifiers.None, WbCommand.TeamEdit)]
    [InlineData(Key.F5, KeyModifiers.Shift, WbCommand.ViewResetDevice)]
    [InlineData(Key.A, KeyModifiers.None, WbCommand.TileTool)]
    [InlineData(Key.D1, KeyModifiers.Alt, WbCommand.ViewShowObjects)]
    [InlineData(Key.F11, KeyModifiers.None, WbCommand.ViewToggleFullscreen)]
    [InlineData(Key.Escape, KeyModifiers.None, WbCommand.ViewExitFullscreen)]
    [InlineData(Key.Delete, KeyModifiers.None, WbCommand.EditDeleteSelected)]
    public void TryGetCommand_DocumentedChords_Resolves(Key key, KeyModifiers modifiers, WbCommand expected)
    {
        WbShortcuts.TryGetCommand(key, modifiers).Should().Be(expected);
    }

    /// <summary>
    /// Verifies unbound chords resolve to null.
    /// </summary>
    [Fact]
    public void TryGetCommand_Unbound_ReturnsNull()
    {
        WbShortcuts.TryGetCommand(Key.H, KeyModifiers.Control).Should().BeNull();
        WbShortcuts.TryGetCommand(Key.F6, KeyModifiers.None).Should().BeNull();
    }

    /// <summary>
    /// Verifies the Command modifier normalizes to Control for macOS parity.
    /// </summary>
    [Fact]
    public void TryGetCommand_MetaModifier_NormalizesToControl()
    {
        WbShortcuts.TryGetCommand(Key.S, KeyModifiers.Meta).Should().Be(WbCommand.FileSave);
        WbShortcuts.TryGetCommand(Key.S, KeyModifiers.Meta, normalizeCommandModifier: false).Should().BeNull();
    }

    /// <summary>
    /// Verifies extra modifiers prevent a match.
    /// </summary>
    [Fact]
    public void TryGetCommand_ExtraModifiers_NoMatch()
    {
        WbShortcuts.TryGetCommand(Key.S, KeyModifiers.Control | KeyModifiers.Alt).Should().BeNull();
    }

    /// <summary>
    /// Verifies shortcuts are suppressed from text fields but not elsewhere.
    /// </summary>
    [Fact]
    public void ShouldSuppressForFocusedElement_TextBoxOnly_Suppresses()
    {
        WbShortcuts.ShouldSuppressForFocusedElement(null).Should().BeFalse();
        WbShortcuts.ShouldSuppressForFocusedElement(new Button()).Should().BeFalse();
        WbShortcuts.ShouldSuppressForFocusedElement(new TextBox()).Should().BeTrue();
    }
}
