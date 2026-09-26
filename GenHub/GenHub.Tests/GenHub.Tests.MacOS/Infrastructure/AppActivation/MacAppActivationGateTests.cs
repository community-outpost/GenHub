using GenHub.MacOS.Infrastructure.AppActivation;
using System.Runtime.Versioning;

namespace GenHub.Tests.MacOS.Infrastructure.AppActivation;

/// <summary>
/// Tests the decisions that keep GenHub from pulling itself to the front on macOS.
/// </summary>
[SupportedOSPlatform("macos")]
public class MacAppActivationGateTests
{
    private const ulong LeftMouseDown = 1;
    private const ulong MouseMoved = 5;
    private const ulong KeyDown = 10;
    private const ulong AppKitDefined = 13;

    /// <summary>Activation passes only while GenHub is active, the user interacts with it, or GenHub handles a request aimed at it.</summary>
    /// <param name="isAppActive">Whether GenHub is already active.</param>
    /// <param name="isRecentUserInput">Whether the user just sent input to GenHub.</param>
    /// <param name="isUserRequestInProgress">Whether GenHub is handling a link or similar request.</param>
    /// <param name="expected">Whether activation should proceed.</param>
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    public void ShouldAllowActivation_OnlyForActiveAppOrUserIntent(bool isAppActive, bool isRecentUserInput, bool isUserRequestInProgress, bool expected)
    {
        Assert.Equal(expected, MacAppActivationGate.ShouldAllowActivation(isAppActive, isRecentUserInput, isUserRequestInProgress));
    }

    /// <summary>Only fresh mouse and key events count as the user reaching for GenHub.</summary>
    /// <param name="eventType">The AppKit event type.</param>
    /// <param name="ageSeconds">How long ago the event was sent.</param>
    /// <param name="expected">Whether the event counts as recent user input.</param>
    [Theory]
    [InlineData(LeftMouseDown, 0.1, true)]
    [InlineData(KeyDown, 0.5, true)]
    [InlineData(LeftMouseDown, 5.0, false)]
    [InlineData(KeyDown, -1.0, false)]
    [InlineData(MouseMoved, 0.1, false)]
    [InlineData(AppKitDefined, 0.1, false)]
    public void IsRecentUserInput_RequiresFreshMouseOrKeyEvent(ulong eventType, double ageSeconds, bool expected)
    {
        const double now = 1000.0;
        Assert.Equal(expected, MacAppActivationGate.IsRecentUserInput(eventType, now - ageSeconds, now));
    }

    /// <summary>The gate applies to bundled launches, where LaunchServices decides whether GenHub comes forward.</summary>
    /// <param name="processPath">The executable path.</param>
    /// <param name="expected">Whether the path is inside an app bundle.</param>
    [Theory]
    [InlineData("/Applications/GenHub.app/Contents/MacOS/GenHub", true)]
    [InlineData("/Users/dev/GenHub/bin/Debug/net8.0/GenHub.MacOS", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsRunningFromAppBundle_DetectsBundledExecutable(string? processPath, bool expected)
    {
        Assert.Equal(expected, MacAppActivationGate.IsRunningFromAppBundle(processPath));
    }

    /// <summary>Unbundled development runs keep Avalonia's activation because nothing else brings them forward.</summary>
    [Fact]
    public void Install_UnbundledProcess_LeavesActivationUntouched()
    {
        Assert.False(MacAppActivationGate.Install("/Users/dev/GenHub/bin/Debug/net8.0/GenHub.MacOS", null));
    }
}
