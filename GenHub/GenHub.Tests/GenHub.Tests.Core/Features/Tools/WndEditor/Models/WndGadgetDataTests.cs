using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for the gadget data records.
/// </summary>
public sealed class WndGadgetDataTests
{
    /// <summary>
    /// Tests that static text data round-trips.
    /// </summary>
    [Fact]
    public void StaticTextData_RoundTrips()
    {
        // Act
        var parsed = WndStaticTextData.TryParse("CENTERED: yes", out var data);

        // Assert
        parsed.Should().BeTrue();
        data.Should().Be(new WndStaticTextData(true));
        data!.ToString().Should().Be("CENTERED: yes");
        WndStaticTextData.TryParse("nonsense", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that text entry data round-trips.
    /// </summary>
    [Fact]
    public void TextEntryData_RoundTrips()
    {
        // Arrange
        const string value = "MAXLEN: 59, SECRETTEXT: no, NUMERICALONLY: no, ALPHANUMERICALONLY: no, ASCIIONLY: no";

        // Act
        var parsed = WndTextEntryData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data.Should().Be(new WndTextEntryData(59, false, false, false, false));
        data!.ToString().Should().Be(value);
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
        const string value = "LENGTH: 30, AUTOSCROLL: no, ScrollIfAtEnd: no, AUTOPURGE: no, SCROLLBAR: yes, MULTISELECT: no, COLUMNS: 1, FORCESELECT: no";

        // Act
        var parsed = WndListboxData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data!.ScrollIfAtEnd.Should().Be(false);
        data.ToString().Should().Be(value);
    }

    /// <summary>
    /// Tests that list box data round-trips without the optional flag and with columns.
    /// </summary>
    [Fact]
    public void ListboxData_WithColumns_RoundTrips()
    {
        // Arrange
        const string value = "LENGTH: 30, AUTOSCROLL: no, AUTOPURGE: no, SCROLLBAR: yes, MULTISELECT: no, COLUMNS: 2, COLUMNS: 60, COLUMNS: 40, FORCESELECT: no";

        // Act
        var parsed = WndListboxData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data!.ScrollIfAtEnd.Should().BeNull();
        data.ColumnWidths.Should().Equal(60, 40);
        data.ToString().Should().Be(value);
        WndListboxData.TryParse("LENGTH: 30", out _).Should().BeFalse();
    }

    /// <summary>
    /// Tests that combo box data round-trips.
    /// </summary>
    [Fact]
    public void ComboBoxData_RoundTrips()
    {
        // Arrange
        const string value = "ISEDITABLE: no, MAXCHARS: 59, MAXDISPLAY: 4, ASCIIONLY: no, LETTERSANDNUMBERSONLY: no";

        // Act
        var parsed = WndComboBoxData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data.Should().Be(new WndComboBoxData(false, 59, 4, false, false));
        data!.ToString().Should().Be(value);
        WndComboBoxData.TryParse("ISEDITABLE: no", out _).Should().BeFalse();
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
        const string value = "TABORIENTATION: 0, TABEDGE: 0, TABWIDTH: 100, TABHEIGHT: 20, TABCOUNT: 2, PANEBORDER: 1, PANEDISABLED: 2, no, no";

        // Act
        var parsed = WndTabControlData.TryParse(value, out var data);

        // Assert
        parsed.Should().BeTrue();
        data!.PaneDisabled.Should().Equal(false, false);
        data.ToString().Should().Be(value);
        WndTabControlData.TryParse("TABORIENTATION: 0", out _).Should().BeFalse();
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
