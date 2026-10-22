using FluentAssertions;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.Services;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Services;

/// <summary>
/// Unit tests for <see cref="WndEditorAssetService"/>.
/// </summary>
public sealed class WndEditorAssetServiceTests
{
    /// <summary>
    /// Tests that the properties expose the underlying injected services.
    /// </summary>
    [Fact]
    public void Properties_ExposeInjectedServices()
    {
        // Arrange
        var mockImages = new Mock<IWndImageAssetService>();
        var mockStrings = new Mock<IWndStringTableService>();

        // Act
        var service = new WndEditorAssetService(mockImages.Object, mockStrings.Object);

        // Assert
        service.Images.Should().BeSameAs(mockImages.Object);
        service.Strings.Should().BeSameAs(mockStrings.Object);
    }

    /// <summary>
    /// Tests that InvalidateCache invalidates both image and string services.
    /// </summary>
    [Fact]
    public void InvalidateCache_DelegatesToBothServices()
    {
        // Arrange
        var mockImages = new Mock<IWndImageAssetService>();
        var mockStrings = new Mock<IWndStringTableService>();
        var service = new WndEditorAssetService(mockImages.Object, mockStrings.Object);

        // Act
        service.InvalidateCache();

        // Assert
        mockImages.Verify(s => s.InvalidateCache(), Times.Once);
        mockStrings.Verify(s => s.InvalidateCache(), Times.Once);
    }
}
