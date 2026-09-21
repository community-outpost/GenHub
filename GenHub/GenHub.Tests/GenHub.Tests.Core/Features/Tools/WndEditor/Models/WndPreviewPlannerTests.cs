using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.WndEditor;
using System.Collections.Generic;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndPreviewPlanner"/>.
/// </summary>
public sealed class WndPreviewPlannerTests
{
    /// <summary>
    /// Tests that a button with middle art plans a three-piece bar.
    /// </summary>
    [Fact]
    public void Plan_ButtonWithMiddleImage_PlansThreePiece()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.PushButton };
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("Left", 0), ("Middle", 5), ("Right", 6)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.IsThreePiece.Should().BeTrue();
        plan.LeftImage.Should().Be("Left");
        plan.CenterImage.Should().Be("Middle");
        plan.RightImage.Should().Be("Right");
        plan.SingleImage.Should().BeNull();
        plan.ReferencedImages.Should().BeEquivalentTo("Left", "Middle", "Right");
    }

    /// <summary>
    /// Tests that a button without middle art plans a single stretched image.
    /// </summary>
    [Fact]
    public void Plan_ButtonWithoutMiddleImage_PlansSingle()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.PushButton };
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("Whole", 0)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.IsThreePiece.Should().BeFalse();
        plan.SingleImage.Should().Be("Whole");
    }

    /// <summary>
    /// Tests that a generic window only shows its image with the IMAGE status flag.
    /// </summary>
    /// <param name="status">The status value.</param>
    /// <param name="expectImage">Whether the image is planned.</param>
    [Theory]
    [InlineData("ENABLED+IMAGE", true)]
    [InlineData("ENABLED", false)]
    [InlineData(null, false)]
    public void Plan_GenericWindow_GatesImageOnStatusFlag(string? status, bool expectImage)
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.User };
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("Backdrop", 0)));
        if (status != null)
        {
            window.SetProperty(WndConstants.PropertyKeys.Status, status);
        }

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.SingleImage.Should().Be(expectImage ? "Backdrop" : null);
        plan.FillColor.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that static text plans a text overlay without images.
    /// </summary>
    [Fact]
    public void Plan_StaticText_PlansTextOnly()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.StaticText };
        window.SetProperty(WndConstants.PropertyKeys.Text, "\"Hello\"");
        window.SetProperty(WndConstants.PropertyKeys.StaticTextData, "CENTERED: 1");

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.Text.Should().Be("Hello");
        plan.TextCentered.Should().BeTrue();
        plan.SingleImage.Should().BeNull();
        plan.IsThreePiece.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a text entry with center art plans a three-piece bar.
    /// </summary>
    [Fact]
    public void Plan_TextEntryWithCenterImage_PlansThreePiece()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.EntryField };
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("EdgeL", 0), ("EdgeR", 1), ("Fill", 2)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.IsThreePiece.Should().BeTrue();
        plan.LeftImage.Should().Be("EdgeL");
        plan.CenterImage.Should().Be("Fill");
        plan.RightImage.Should().Be("EdgeR");
    }

    /// <summary>
    /// Tests that hidden windows are flagged for dimming.
    /// </summary>
    [Fact]
    public void Plan_HiddenWindow_SetsIsHidden()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.User };
        window.SetProperty(WndConstants.PropertyKeys.Status, "ENABLED+HIDDEN");

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.IsHidden.Should().BeTrue();
    }

    private static string DrawDataWith(params (string Name, int Index)[] images)
    {
        var entries = new List<WndDrawDataEntry>();
        for (var i = 0; i < WndConstants.DrawData.EntryCount; i++)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }

        foreach (var (name, index) in images)
        {
            entries[index] = new WndDrawDataEntry(name, new WndRgbaColor(10, 20, 30, 255), new WndRgbaColor(40, 50, 60, 255));
        }

        return new WndDrawDataSet(entries).ToString();
    }
}
