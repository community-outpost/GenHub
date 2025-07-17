using System.Collections.Generic;
using GenHub.Core;
using GenHub.Services;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Services;

/// <summary>
/// Unit tests for <see cref="GameDetectionService"/>.
/// </summary>
public class GameDetectionServiceTests
{
    /// <summary>
    /// Tests that the properties of <see cref="GameDetectionService"/> reflect the detector's installations.
    /// </summary>
    [Fact]
    public void Properties_ReflectDetectorInstallations()
    {
        var mockInstallation = new Mock<IGameInstallation>();
        mockInstallation.SetupGet(x => x.IsVanillaInstalled).Returns(true);
        mockInstallation.SetupGet(x => x.VanillaGamePath).Returns("C:/Gen/Generals");
        mockInstallation.SetupGet(x => x.IsZeroHourInstalled).Returns(true);
        mockInstallation.SetupGet(x => x.ZeroHourGamePath).Returns("C:/Gen/ZeroHour");

        var mockDetector = new Mock<IGameDetector>();
        mockDetector.SetupGet(x => x.Installations).Returns(new List<IGameInstallation> { mockInstallation.Object });

        var service = new GameDetectionService(mockDetector.Object);
        Assert.True(service.IsVanillaInstalled);
        Assert.Equal("C:/Gen/Generals", service.VanillaGamePath);
        Assert.True(service.IsZeroHourInstalled);
    }

    /// <summary>
    /// Tests that <see cref="GameDetectionService.DetectGames"/> invokes the detector's Detect method.
    /// </summary>
    [Fact]
    public void DetectGames_InvokesDetectorDetect()
    {
        var mockDetector = new Mock<IGameDetector>();
        var service = new GameDetectionService(mockDetector.Object);
        service.DetectGames();
        mockDetector.Verify(x => x.Detect(), Times.Once);
    }
}
