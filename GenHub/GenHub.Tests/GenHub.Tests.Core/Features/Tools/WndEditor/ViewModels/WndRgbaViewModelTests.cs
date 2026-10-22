using Avalonia.Media;
using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.ViewModels;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// Unit tests for <see cref="WndRgbaViewModel"/>.
/// </summary>
public sealed class WndRgbaViewModelTests
{
    /// <summary>
    /// Tests that the picker color roundtrips through the channels.
    /// </summary>
    [Fact]
    public void SelectedColor_RoundtripsChannels()
    {
        // Arrange
        var commits = 0;
        var viewModel = new WndRgbaViewModel(new WndRgbaColor(255, 128, 0, 255), () => commits++);

        // Assert
        viewModel.SelectedColor.Should().Be(Color.FromArgb(255, 255, 128, 0));

        // Act
        viewModel.SelectedColor = Color.FromArgb(128, 10, 20, 30);

        // Assert
        viewModel.Current.Should().Be(new WndRgbaColor(10, 20, 30, 128));
        viewModel.HexValue.Should().Be("#0A141E80");
        commits.Should().Be(1);
    }

    /// <summary>
    /// Tests that the hex value uses RGBA channel order.
    /// </summary>
    [Fact]
    public void HexValue_UsesRgbaOrder()
    {
        // Arrange
        var viewModel = new WndRgbaViewModel(new WndRgbaColor(1, 2, 3, 4), () => { });

        // Assert
        viewModel.HexValue.Should().Be("#01020304");
        viewModel.SwatchBrush.Color.Should().Be(Color.FromArgb(4, 1, 2, 3));
    }

    /// <summary>
    /// Tests that setting the same picker color does not commit.
    /// </summary>
    [Fact]
    public void SelectedColor_SameValue_DoesNotCommit()
    {
        // Arrange
        var commits = 0;
        var viewModel = new WndRgbaViewModel(WndRgbaColor.White, () => commits++);

        // Act
        viewModel.SelectedColor = Color.FromArgb(255, 255, 255, 255);

        // Assert
        commits.Should().Be(0);
    }

    /// <summary>
    /// Tests that a picker session commits once when the color changed.
    /// </summary>
    [Fact]
    public void ColorEdit_WithChanges_CommitsOnce()
    {
        // Arrange
        var commits = 0;
        var viewModel = new WndRgbaViewModel(WndRgbaColor.White, () => commits++);

        // Act
        viewModel.BeginColorEdit();
        viewModel.SelectedColor = Color.FromArgb(255, 255, 0, 0);
        viewModel.SelectedColor = Color.FromArgb(255, 0, 255, 0);
        viewModel.SelectedColor = Color.FromArgb(255, 0, 0, 255);
        viewModel.EndColorEdit();

        // Assert
        viewModel.Current.Should().Be(new WndRgbaColor(0, 0, 255, 255));
        commits.Should().Be(1);
    }

    /// <summary>
    /// Tests that a picker session without changes does not commit.
    /// </summary>
    [Fact]
    public void ColorEdit_WithoutChanges_DoesNotCommit()
    {
        // Arrange
        var commits = 0;
        var viewModel = new WndRgbaViewModel(WndRgbaColor.White, () => commits++);

        // Act
        viewModel.BeginColorEdit();
        viewModel.EndColorEdit();

        // Assert
        commits.Should().Be(0);
    }

    /// <summary>
    /// Tests that ending without beginning does not commit.
    /// </summary>
    [Fact]
    public void EndColorEdit_WithoutBegin_DoesNotCommit()
    {
        // Arrange
        var commits = 0;
        var viewModel = new WndRgbaViewModel(WndRgbaColor.White, () => commits++);

        // Act
        viewModel.EndColorEdit();

        // Assert
        commits.Should().Be(0);
    }
}
