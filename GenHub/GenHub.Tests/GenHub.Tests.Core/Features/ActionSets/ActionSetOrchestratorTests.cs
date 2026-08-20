namespace GenHub.Tests.Core.Features.ActionSets;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ActionSetOrchestrator"/>.
/// </summary>
public class ActionSetOrchestratorTests
{
    private readonly Mock<ILogger<ActionSetOrchestrator>> _loggerMock = new();

    /// <summary>
    /// Verifies that when a fix fails in a batch, partial success count is returned in OperationResult.Data.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test.</returns>
    [Fact]
    public async Task ApplyActionSetsAsync_WhenFixFails_ReturnsPartialSuccessCount()
    {
        var fix1 = new Mock<IActionSet>();
        fix1.SetupGet(f => f.Id).Returns("Fix1");
        fix1.SetupGet(f => f.Title).Returns("Fix 1");
        fix1.SetupGet(f => f.IsCrucialFix).Returns(false);
        fix1.Setup(f => f.IsApplicableAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fix1.Setup(f => f.IsAppliedAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        fix1.Setup(f => f.ApplyAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ActionSetResult(true));

        var fix2 = new Mock<IActionSet>();
        fix2.SetupGet(f => f.Id).Returns("Fix2");
        fix2.SetupGet(f => f.Title).Returns("Fix 2");
        fix2.SetupGet(f => f.IsCrucialFix).Returns(false);
        fix2.Setup(f => f.IsApplicableAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fix2.Setup(f => f.IsAppliedAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        fix2.Setup(f => f.ApplyAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ActionSetResult(false, "Fix2 failed"));

        var orchestrator = new ActionSetOrchestrator([fix1.Object, fix2.Object], [], _loggerMock.Object);
        var installation = new GameInstallation("C:\\TestPath", GameInstallationType.Steam);

        var result = await orchestrator.ApplyActionSetsAsync(installation, [fix1.Object, fix2.Object]);

        Assert.False(result.Success);
        Assert.Equal(1, result.Data);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Verifies that when a crucial fix fails, sequence aborts and partial success count is returned.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test.</returns>
    [Fact]
    public async Task ApplyActionSetsAsync_WhenCrucialFixFails_AbortsAndReturnsPartialSuccessCount()
    {
        var fix1 = new Mock<IActionSet>();
        fix1.SetupGet(f => f.Id).Returns("Fix1");
        fix1.SetupGet(f => f.Title).Returns("Fix 1");
        fix1.SetupGet(f => f.IsCrucialFix).Returns(false);
        fix1.Setup(f => f.IsApplicableAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fix1.Setup(f => f.IsAppliedAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        fix1.Setup(f => f.ApplyAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ActionSetResult(true));

        var fix2 = new Mock<IActionSet>();
        fix2.SetupGet(f => f.Id).Returns("CrucialFix2");
        fix2.SetupGet(f => f.Title).Returns("Crucial Fix 2");
        fix2.SetupGet(f => f.IsCrucialFix).Returns(true);
        fix2.Setup(f => f.IsApplicableAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        fix2.Setup(f => f.IsAppliedAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        fix2.Setup(f => f.ApplyAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ActionSetResult(false, "Crucial failure"));

        var fix3 = new Mock<IActionSet>();
        fix3.SetupGet(f => f.Id).Returns("Fix3");
        fix3.SetupGet(f => f.Title).Returns("Fix 3");

        var orchestrator = new ActionSetOrchestrator([fix1.Object, fix2.Object, fix3.Object], [], _loggerMock.Object);
        var installation = new GameInstallation("C:\\TestPath", GameInstallationType.Steam);

        var result = await orchestrator.ApplyActionSetsAsync(installation, [fix1.Object, fix2.Object, fix3.Object]);

        Assert.False(result.Success);
        Assert.Equal(1, result.Data);
        fix3.Verify(f => f.ApplyAsync(It.IsAny<GameInstallation>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
