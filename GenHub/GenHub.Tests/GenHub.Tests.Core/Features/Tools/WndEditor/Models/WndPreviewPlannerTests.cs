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
    /// Tests that a generic window displays its image when draw data is specified.
    /// In SAGE engine, windows with IMAGE status skip winFillRect (FillColor is null).
    /// </summary>
    /// <param name="status">The status value.</param>
    [Theory]
    [InlineData("ENABLED+IMAGE")]
    [InlineData("ENABLED")]
    [InlineData(null)]
    public void Plan_GenericWindow_PlansImageRegardlessOfStatusImageFlag(string? status)
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
        plan.SingleImage.Should().Be("Backdrop");
        if (status?.Contains("IMAGE") == true)
        {
            plan.FillColor.Should().BeNull();
        }
        else
        {
            plan.FillColor.Should().NotBeNull();
        }
    }

    /// <summary>
    /// Tests that static text plans a text overlay without images when no draw data image is specified.
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
    /// Tests that static text with draw data plans both image and text.
    /// </summary>
    [Fact]
    public void Plan_StaticText_WithDrawData_PlansImageAndText()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.StaticText };
        window.SetProperty(WndConstants.PropertyKeys.Text, "\"Hello\"");
        window.SetProperty(WndConstants.PropertyKeys.StaticTextData, "CENTERED: 1");
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("HeaderFrame", 0)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.Text.Should().Be("Hello");
        plan.TextCentered.Should().BeTrue();
        plan.SingleImage.Should().Be("HeaderFrame");
    }

    /// <summary>
    /// Tests that a text entry with center art plans a three-piece bar.
    /// </summary>
    [Fact]
    public void Plan_TextEntryWithCenterImage_PlansThreePiece()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.EntryField };
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("Left", WndConstants.Preview.TextEntryLeftImageIndex), ("Right", WndConstants.Preview.TextEntryRightImageIndex), ("Center", WndConstants.Preview.TextEntryCenterImageIndex)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.IsThreePiece.Should().BeTrue();
        plan.LeftImage.Should().Be("Left");
        plan.CenterImage.Should().Be("Center");
        plan.RightImage.Should().Be("Right");
    }

    /// <summary>
    /// Tests that check boxes and radio buttons plan their box glyph from index one.
    /// </summary>
    /// <param name="controlType">The control type under test.</param>
    [Theory]
    [InlineData(WndConstants.ControlTypes.CheckBox)]
    [InlineData(WndConstants.ControlTypes.RadioButton)]
    public void Plan_BoxControls_PlansBoxGlyph(string controlType)
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = controlType };
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("Box", 1)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.GlyphImage.Should().Be("Box");
        plan.SingleImage.Should().BeNull();
    }

    /// <summary>
    /// Tests that list boxes plan scrollbar sub-images from their sub-draw-data.
    /// </summary>
    [Fact]
    public void Plan_Listbox_PlansScrollbarSubImages()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.ScrollListBox };
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("ListBack", 0)));
        window.SetProperty(WndConstants.SubDrawDataKeys.ListboxEnabledUpButton, DrawDataWith(("ScrollUp", 0)));
        window.SetProperty(WndConstants.SubDrawDataKeys.ListboxEnabledDownButton, DrawDataWith(("ScrollDown", 0)));
        window.SetProperty(WndConstants.SubDrawDataKeys.ListboxEnabledSlider, DrawDataWith(("ScrollThumb", 0)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.SingleImage.Should().Be("ListBack");
        plan.SubImages.Should().NotBeNull();
        plan.SubImages!.ScrollUp.Should().Be("ScrollUp");
        plan.SubImages.ScrollDown.Should().Be("ScrollDown");
        plan.SubImages.ScrollThumb.Should().Be("ScrollThumb");
        plan.ReferencedImages.Should().BeEquivalentTo("ListBack", "ScrollUp", "ScrollDown", "ScrollThumb");
    }

    /// <summary>
    /// Tests that list boxes hide scrollbar art when the scrollbar flag is off.
    /// </summary>
    [Fact]
    public void Plan_ListboxWithoutScrollBar_HidesScrollbar()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.ScrollListBox };
        window.SetProperty(
            WndConstants.PropertyKeys.ListboxData,
            "LENGTH: 10, AUTOSCROLL: 0, AUTOPURGE: 0, SCROLLBAR: 0, MULTISELECT: 0, COLUMNS: 0, FORCESELECT: 0");
        window.SetProperty(WndConstants.SubDrawDataKeys.ListboxEnabledUpButton, DrawDataWith(("ScrollUp", 0)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.SubImages.Should().NotBeNull();
        plan.SubImages!.ScrollUp.Should().BeNull();
    }

    /// <summary>
    /// Tests that combo boxes plan their drop-down button art.
    /// </summary>
    [Fact]
    public void Plan_ComboBox_PlansDropDownButton()
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = WndConstants.ControlTypes.ComboBox };
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("ComboBack", 0)));
        window.SetProperty(WndConstants.SubDrawDataKeys.ComboBoxDropDownButtonEnabled, DrawDataWith(("ComboButton", 0)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.SingleImage.Should().Be("ComboBack");
        plan.SubImages.Should().NotBeNull();
        plan.SubImages!.ComboButton.Should().Be("ComboButton");
    }

    /// <summary>
    /// Tests that sliders plan a three-piece trough with indices zero, two, one.
    /// </summary>
    /// <param name="controlType">The slider control type.</param>
    /// <param name="vertical">Whether the bar is vertical.</param>
    [Theory]
    [InlineData(WndConstants.ControlTypes.HorzSlider, false)]
    [InlineData(WndConstants.ControlTypes.VertSlider, true)]
    public void Plan_Slider_PlansThreePieceTrough(string controlType, bool vertical)
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = controlType };
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("TroughLeft", 0), ("TroughRight", 1), ("TroughCenter", 2)));
        window.SetProperty(WndConstants.SubDrawDataKeys.SliderThumbEnabled, DrawDataWith(("Thumb", 0)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.IsThreePiece.Should().BeTrue();
        plan.LeftImage.Should().Be("TroughLeft");
        plan.CenterImage.Should().Be("TroughCenter");
        plan.RightImage.Should().Be("TroughRight");
        plan.IsVerticalBar.Should().Be(vertical);
        plan.SubImages.Should().NotBeNull();
        plan.SubImages!.SliderThumb.Should().Be("Thumb");
    }

    /// <summary>
    /// Tests that gadget windows show index zero art without requiring the IMAGE flag.
    /// </summary>
    /// <param name="controlType">The gadget control type.</param>
    [Theory]
    [InlineData(WndConstants.ControlTypes.ScrollListBox)]
    [InlineData(WndConstants.ControlTypes.ComboBox)]
    [InlineData(WndConstants.ControlTypes.ProgressBar)]
    public void Plan_GadgetWithoutImageFlag_ShowsIndexZero(string controlType)
    {
        // Arrange
        var window = new WndWindow { ControlTypeName = controlType };
        window.SetProperty(WndConstants.PropertyKeys.Status, "ENABLED");
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("Backdrop", 0)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.SingleImage.Should().Be("Backdrop");
    }

    /// <summary>
    /// Tests that command bar background marker plans default scheme HUD image when SingleImage is null.
    /// </summary>
    [Fact]
    public void Plan_ControlBarBackgroundMarker_PlansDefaultSchemeImage()
    {
        // Arrange
        var window = new WndWindow
        {
            ControlTypeName = WndConstants.ControlTypes.User,
        };
        window.SetProperty(WndConstants.PropertyKeys.Name, "ControlBar.wnd:BackgroundMarker");
        window.SetProperty(WndConstants.PropertyKeys.DrawCallback, "W3DCommandBarBackgroundDraw");

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.SingleImage.Should().Be("InGameUIAmericaBase");
        plan.ReferencedImages.Should().Contain("InGameUIAmericaBase");
    }

    /// <summary>
    /// Tests that command bar background marker with tiny screen dimensions (e.g. 5x5 anchor) suppresses the scheme image
    /// so the HUD graphic is not squished into the bottom corner.
    /// </summary>
    [Fact]
    public void Plan_ControlBarBackgroundMarker_WithTinyScreenRect_SuppressesImage()
    {
        // Arrange
        var window = new WndWindow
        {
            ControlTypeName = WndConstants.ControlTypes.User,
        };
        window.SetProperty(WndConstants.PropertyKeys.Name, "ControlBar.wnd:BackgroundMarker");
        window.SetProperty(WndConstants.PropertyKeys.DrawCallback, "W3DCommandBarBackgroundDraw");
        window.SetProperty(WndConstants.PropertyKeys.ScreenRect, "UPPERLEFT: 8 595, BOTTOMRIGHT: 13 600, CREATIONRESOLUTION: 800 600");

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.SingleImage.Should().BeNull();
    }

    /// <summary>
    /// Tests that full-width ControlBar Munkee window plans the full scheme background image.
    /// </summary>
    [Fact]
    public void Plan_ControlBarMunkee_PlansFullWidthSchemeImage()
    {
        // Arrange
        var window = new WndWindow
        {
            ControlTypeName = WndConstants.ControlTypes.User,
        };
        window.SetProperty(WndConstants.PropertyKeys.Name, "ControlBar.wnd:Munkee");
        window.SetProperty(WndConstants.PropertyKeys.ScreenRect, "UPPERLEFT: 0 414, BOTTOMRIGHT: 799 599, CREATIONRESOLUTION: 800 600");

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.SingleImage.Should().Be("InGameUIAmericaBase");
        plan.ReferencedImages.Should().Contain("InGameUIAmericaBase");
    }

    /// <summary>
    /// Tests that MainMenuRuler automatically references MainMenuBackdrop as underlay.
    /// </summary>
    [Fact]
    public void Plan_MainMenuRuler_PlansBackdropUnderlay()
    {
        // Arrange
        var window = new WndWindow
        {
            ControlTypeName = WndConstants.ControlTypes.User,
        };
        window.SetProperty(WndConstants.PropertyKeys.Name, "LanGameOptionsMenu.wnd:LanGameOptionsMenuParent");
        window.SetProperty(WndConstants.PropertyKeys.EnabledDrawData, DrawDataWith(("MainMenuRuler", 0)));

        // Act
        var plan = WndPreviewPlanner.Plan(window);

        // Assert
        plan.SingleImage.Should().Be("MainMenuRuler");
        plan.UnderlayImage.Should().Be("MainMenuBackdrop");
        plan.ReferencedImages.Should().Contain("MainMenuBackdrop");
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
