using Avalonia.Media;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.ViewModels;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// Unit tests for <see cref="WndCanvasItemViewModel"/> verifying GUI rendering acceptance criteria.
/// </summary>
public sealed class WndCanvasItemViewModelTests
{
    /// <summary>
    /// Acceptance Criteria: Controls without text (e.g. general medallions, graphic buttons)
    /// must not display internal window names when unselected, preventing ugly truncated labels.
    /// </summary>
    [Fact]
    public void ShowNameTag_WhenNotSelected_IsFalseEvenWithoutContentText()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.PushButton };
        window.SetProperty(WndConstants.PropertyKeys.Name, "\"ChallengeMenu.wnd:GeneralPosition0\"");
        var item = new WndCanvasItemViewModel(window);

        // Assert
        item.HasContentText.Should().BeFalse();
        item.IsSelected.Should().BeFalse();
        item.ShowNameTag.Should().BeFalse();
        item.ShowPrimaryNameTag.Should().BeFalse();
    }

    /// <summary>
    /// Acceptance Criteria: Static text without content stays untagged until selected,
    /// so runtime-populated labels render blank like the game.
    /// </summary>
    [Fact]
    public void ShowNameTag_StaticTextWithoutContentWhenNotSelected_IsFalse()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.StaticText };
        window.SetProperty(WndConstants.PropertyKeys.Name, "\"LanGameOptionsMenu.wnd:StaticTextPlayer0\"");
        var item = new WndCanvasItemViewModel(window);

        // Assert
        item.HasContentText.Should().BeFalse();
        item.HasImage.Should().BeFalse();
        item.ShowNameTag.Should().BeFalse();
    }

    /// <summary>
    /// Acceptance Criteria: When a control is selected in the canvas editor, its name tag becomes visible.
    /// </summary>
    [Fact]
    public void ShowNameTag_WhenSelected_IsTrue()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.PushButton };
        window.SetProperty(WndConstants.PropertyKeys.Name, "\"ChallengeMenu.wnd:GeneralPosition0\"");
        var item = new WndCanvasItemViewModel(window)
        {
            IsSelected = true,
        };

        // Assert
        item.IsSelected.Should().BeTrue();
        item.ShowNameTag.Should().BeTrue();
        item.ShowPrimaryNameTag.Should().BeTrue();
    }

    /// <summary>
    /// Acceptance Criteria: Fill shows only when a fill overlay is set and no image is present.
    /// </summary>
    [Fact]
    public void ShowFill_WhenHasFillAndNoImage_IsTrue()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.User };
        var item = new WndCanvasItemViewModel(window)
        {
            FillOverlay = new SolidColorBrush(Colors.Red),
        };

        // Assert
        item.HasFill.Should().BeTrue();
        item.HasImage.Should().BeFalse();
        item.ShowFill.Should().BeTrue();
    }

    /// <summary>
    /// Acceptance Criteria: SAGE layout containers with no image and no fill are fully transparent.
    /// </summary>
    [Fact]
    public void TransparentContainer_HasNoFillAndNoBorderByDefault()
    {
        // Arrange: container window (e.g. CircleAlphaOuter, VersusBackdrop)
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.User };
        window.SetProperty(WndConstants.PropertyKeys.Name, "\"ChallengeLoadScreen.wnd:CircleAlphaOuter\"");
        var item = new WndCanvasItemViewModel(window);

        // Assert
        item.ShowFill.Should().BeFalse();
        item.HasBorderOverlay.Should().BeFalse();
        item.HasImage.Should().BeFalse();
        item.IsSelected.Should().BeFalse();
    }

    /// <summary>
    /// Acceptance Criteria: Engine-hidden windows stay visible but dimmed on the canvas
    /// so the editor matches the runtime layout where scripts reveal them.
    /// </summary>
    /// <param name="isPreviewHidden">Whether the engine would hide the window.</param>
    /// <param name="isSelected">Whether the item is selected for editing.</param>
    /// <param name="expectedVisible">The expected canvas visibility.</param>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void CanvasVisible_MatchesHiddenAndSelectedState(bool isPreviewHidden, bool isSelected, bool expectedVisible)
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.User };
        var item = new WndCanvasItemViewModel(window)
        {
            IsPreviewHidden = isPreviewHidden,
            IsSelected = isSelected,
        };

        // Assert
        item.CanvasVisible.Should().Be(expectedVisible);
    }
}
