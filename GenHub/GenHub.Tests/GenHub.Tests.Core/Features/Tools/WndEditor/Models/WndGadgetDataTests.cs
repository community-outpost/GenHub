using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for the gadget data records.
/// </summary>
public sealed class WndGadgetDataTests
{
    /// <summary>
    /// Tests that static text data round-trips and accepts boolean dialects.
    /// </summary>
    [Fact]
    public void StaticTextData_RoundTrips()
    {
        // Act
        var parsed = WndStaticTextData.TryParse("CENTERED: 1", out var data);

        // Assert
        parsed.Should().BeTrue();
        data.Should().Be(new WndStaticTextData(true));
        data!.ToString().Should().Be("CENTERED: 1");

        // Dialect check: yes/no accepted
        WndStaticTextData.TryParse("CENTERED: yes", out var legacyData).Should().BeTrue();
        legacyData!.Centered.Should().BeTrue();
        WndStaticTextData.TryParse("nonsense", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that text entry data round-trips.
    /// </summary>
    [Fact]
    public void TextEntryData_RoundTrips()
    {
        // Arrange
        const string value = "MAXLEN: 59, SECRETTEXT: 0, NUMERICALONLY: 0, ALPHANUMERICALONLY: 0, ASCIIONLY: 0";

        // Act
        var parsed = WndTextEntryData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data.Should().Be(new WndTextEntryData(59, false, false, false, false));
        data!.ToString().Should().Be(value);

        // Dialect check: yes/no accepted
        const string legacy = "MAXLEN: 59, SECRETTEXT: no, NUMERICALONLY: no, ALPHANUMERICALONLY: no, ASCIIONLY: no";
        WndTextEntryData.TryParse(legacy, out var legacyData).Should().BeTrue();
        legacyData.Should().Be(data);

        WndTextEntryData.TryParse("MAXLEN: 59", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that slider data round-trips.
    /// </summary>
    [Fact]
    public void SliderData_RoundTrips()
    {
        // Act
        var parsed = WndSliderData.TryParse("MINVALUE: 0, MAXVALUE: 100", out var data);

        // Assert
        parsed.Should().BeTrue();
        data.Should().Be(new WndSliderData(0, 100));
        data!.ToString().Should().Be("MINVALUE: 0, MAXVALUE: 100");
        WndSliderData.TryParse("MINVALUE: 0", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that list box data round-trips with the optional flag.
    /// </summary>
    [Fact]
    public void ListboxData_WithOptionalFlag_RoundTrips()
    {
        // Arrange
        const string value = "LENGTH: 30, AUTOSCROLL: 0, ScrollIfAtEnd: 0, AUTOPURGE: 0, SCROLLBAR: 1, MULTISELECT: 0, COLUMNS: 1, FORCESELECT: 0";

        // Act
        var parsed = WndListboxData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data!.ScrollIfAtEnd.Should().Be(false);
        data.ToString().Should().Be(value);

        // Dialect check: yes/no accepted
        const string legacy = "LENGTH: 30, AUTOSCROLL: no, ScrollIfAtEnd: no, AUTOPURGE: no, SCROLLBAR: yes, MULTISELECT: no, COLUMNS: 1, FORCESELECT: no";
        WndListboxData.TryParse(legacy, out var legacyData).Should().BeTrue();
        legacyData!.ScrollIfAtEnd.Should().Be(false);
    }

    /// <summary>
    /// Tests that list box data round-trips without the optional flag and with columns.
    /// </summary>
    [Fact]
    public void ListboxData_WithColumns_RoundTrips()
    {
        // Arrange
        const string value = "LENGTH: 30, AUTOSCROLL: 0, AUTOPURGE: 0, SCROLLBAR: 1, MULTISELECT: 0, COLUMNS: 2, COLUMNSWIDTH%: 60, COLUMNSWIDTH%: 40, FORCESELECT: 0";

        // Act
        var parsed = WndListboxData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data!.ScrollIfAtEnd.Should().BeNull();
        data.ColumnWidths.Should().Equal(60, 40);
        data.ToString().Should().Be(value);

        // Dialect check: COLUMNS label accepted as width
        const string columnsLabel = "LENGTH: 30, AUTOSCROLL: 0, AUTOPURGE: 0, SCROLLBAR: 1, MULTISELECT: 0, COLUMNS: 2, COLUMNS: 60, COLUMNS: 40, FORCESELECT: 0";
        WndListboxData.TryParse(columnsLabel, out var colData).Should().BeTrue();
        colData!.ColumnWidths.Should().Equal(60, 40);
        colData.ToString().Should().Be(value);

        WndListboxData.TryParse("LENGTH: 30", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that combo box data round-trips.
    /// </summary>
    [Fact]
    public void ComboBoxData_RoundTrips()
    {
        // Arrange
        const string value = "ISEDITABLE: 0, MAXCHARS: 59, MAXDISPLAY: 4, ASCIIONLY: 0, LETTERSANDNUMBERS: 0";

        // Act
        var parsed = WndComboBoxData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data.Should().Be(new WndComboBoxData(false, 59, 4, false, false));
        data!.ToString().Should().Be(value);

        // Dialect check: LETTERSANDNUMBERSONLY accepted
        const string legacy = "ISEDITABLE: no, MAXCHARS: 59, MAXDISPLAY: 4, ASCIIONLY: no, LETTERSANDNUMBERSONLY: no";
        WndComboBoxData.TryParse(legacy, out var legacyData).Should().BeTrue();
        legacyData.Should().Be(data);

        WndComboBoxData.TryParse("ISEDITABLE: 0", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that radio button data round-trips.
    /// </summary>
    [Fact]
    public void RadioButtonData_RoundTrips()
    {
        // Act
        var parsed = WndRadioButtonData.TryParse("GROUP: 1", out var data);

        // Assert
        parsed.Should().BeTrue();
        data.Should().Be(new WndRadioButtonData(1));
        data!.ToString().Should().Be("GROUP: 1");
        WndRadioButtonData.TryParse("GROUP: one", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that tab control data round-trips.
    /// </summary>
    [Fact]
    public void TabControlData_RoundTrips()
    {
        // Arrange
        const string value = "TABORIENTATION: 0, TABEDGE: 0, TABWIDTH: 100, TABHEIGHT: 20, TABCOUNT: 2, PANEBORDER: 1, PANEDISABLED: 2, 0, 0";

        // Act
        var parsed = WndTabControlData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data!.PaneDisabled.Should().Equal(false, false);
        data.ToString().Should().Be(value);

        // Dialect check: yes/no accepted
        const string legacy = "TABORIENTATION: 0, TABEDGE: 0, TABWIDTH: 100, TABHEIGHT: 20, TABCOUNT: 2, PANEBORDER: 1, PANEDISABLED: 2, no, no";
        WndTabControlData.TryParse(legacy, out var legacyData).Should().BeTrue();
        legacyData!.PaneDisabled.Should().Equal(false, false);

        WndTabControlData.TryParse("TABORIENTATION: 0", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests constructor validation for ListboxData column count mismatch.
    /// </summary>
    [Fact]
    public void ListboxData_ConstructorMismatchedColumns_ThrowsArgumentException()
    {
        var act = () => new WndListboxData(30, false, null, false, true, false, 2, [60], false);
        act.Should().Throw<System.ArgumentException>();
    }

    /// <summary>
    /// Tests constructor validation for TabControlData pane disabled count mismatch.
    /// </summary>
    [Fact]
    public void TabControlData_ConstructorMismatchedPaneDisabled_ThrowsArgumentException()
    {
        var act = () => new WndTabControlData(0, 0, 100, 20, 2, 1, [false]);
        act.Should().Throw<System.ArgumentException>();
    }

    /// <summary>
    /// Tests that TabControlData returns false when disabled count does not match tab count.
    /// </summary>
    [Fact]
    public void TabControlData_MismatchedDisabledCount_ReturnsFalse()
    {
        const string malformed = "TABORIENTATION: 0, TABEDGE: 0, TABWIDTH: 100, TABHEIGHT: 20, TABCOUNT: 2, PANEBORDER: 1, PANEDISABLED: 1, 0";
        WndTabControlData.TryParse(malformed, out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that image offsets round-trip.
    /// </summary>
    [Fact]
    public void ImageOffset_RoundTrips()
    {
        // Act
        var parsed = WndImageOffset.TryParse("4 8", out var offset);

        // Assert
        parsed.Should().BeTrue();
        offset.Should().Be(new WndImageOffset(4, 8));
        offset!.ToString().Should().Be("4 8");
        WndImageOffset.TryParse("4", out _).Should().BeFalse();
    }
}
