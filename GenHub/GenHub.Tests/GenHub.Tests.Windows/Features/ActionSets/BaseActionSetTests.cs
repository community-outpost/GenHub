using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Windows.Features.ActionSets;

/// <summary>
/// Tests for the <see cref="BaseActionSet"/> class.
/// </summary>
public class BaseActionSetTests
{
    private readonly Mock<ILogger> _loggerMock;
    private readonly TestActionSet _testActionSet;

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
    public async Task ApplyAsync_LogsAndCallsInternalAsync()
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

    /// <summary>
    /// Verifies that failure details carry exactly one error prefix.
    /// </summary>
    [Fact]
    public void AddFailureDetail_Error_AddsSinglePrefix()
    {
        var details = new List<string>();

        TestActionSet.AddDetail(details, new InvalidOperationException("boom"));

        Assert.Single(details);
        Assert.Equal("Error: boom", details[0]);
    }

    /// <summary>
    /// Verifies that failure details include the action description.
    /// </summary>
    [Fact]
    public void AddFailureDetail_WithAction_IncludesAction()
    {
        var details = new List<string>();

        TestActionSet.AddDetail(details, new InvalidOperationException("boom"), "restoring files", indent: "  ");

        Assert.Single(details);
        Assert.Equal("  Error: restoring files: boom", details[0]);
    }

    /// <summary>
    /// Verifies that warning details carry exactly one warning prefix.
    /// </summary>
    [Fact]
    public void AddFailureDetail_Warning_AddsSinglePrefix()
    {
        var details = new List<string>();

        TestActionSet.AddDetail(details, new InvalidOperationException("boom"), isWarning: true);

        Assert.Single(details);
        Assert.Equal("Warning: boom", details[0]);
    }

    private class TestActionSet : BaseActionSet
    {
        public bool ApplyCalled { get; private set; }

        public static void AddDetail(List<string> details, Exception ex, string? action = null, bool isWarning = false, string indent = "") =>
            AddFailureDetail(details, ex, action, isWarning, indent);

        public TestActionSet(ILogger logger)
            : base(logger)
        {
        }

        public override string Id => "Test";

        public override string Title => "Test Action Set";

        public override bool IsCoreFix => false;

        public override bool IsCrucialFix => false;

        public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default) => Task.FromResult(true);

        public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default) => Task.FromResult(false);

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
