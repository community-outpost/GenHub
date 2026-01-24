namespace GenHub.Tests.Windows.Features.ActionSets;

using System.Threading.Tasks;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Tests for the <see cref="BaseActionSet"/> class.
/// </summary>
public class BaseActionSetTests
{
    private Mock<ILogger> _loggerMock;
    private TestActionSet _testActionSet;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseActionSetTests"/> class.
    /// </summary>
    public BaseActionSetTests()
    {
        _loggerMock = new Mock<ILogger>();
        _testActionSet = new TestActionSet(_loggerMock.Object);
    }

    /// <summary>
    /// Verifies that ApplyAsync logs the action and calls the internal apply method.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ApplyAsync_LogsAndCallsInternal()
    {
        var installation = new GameInstallation("C:\\Test", GenHub.Core.Models.Enums.GameInstallationType.Unknown);

        var result = await _testActionSet.ApplyAsync(installation);

        Assert.True(result.Success);
        Assert.True(_testActionSet.ApplyCalled);

        // Verify logging happened (simplistic check)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString() != null && v.ToString()!.Contains("Applying ActionSet")),
                It.IsAny<System.Exception?>(),
                It.IsAny<System.Func<It.IsAnyType, System.Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private class TestActionSet : BaseActionSet
    {
        public bool ApplyCalled { get; private set; }

        public TestActionSet(ILogger logger)
            : base(logger)
        {
        }

        public override string Id => "Test";

        public override string Title => "Test Action Set";

        public override bool IsCoreFix => false;

        public override bool IsCrucialFix => false;

        public override Task<bool> IsApplicableAsync(GameInstallation installation) => Task.FromResult(true);

        public override Task<bool> IsAppliedAsync(GameInstallation installation) => Task.FromResult(false);

        protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, System.Threading.CancellationToken ct)
        {
            ApplyCalled = true;
            return Task.FromResult(Success());
        }

        protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, System.Threading.CancellationToken ct)
        {
            return Task.FromResult(Success());
        }
    }
}
