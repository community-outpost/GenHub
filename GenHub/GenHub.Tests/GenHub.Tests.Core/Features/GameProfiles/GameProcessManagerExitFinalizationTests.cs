using GenHub.Core.Models.Events;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>Verifies cleanup using inert Process objects; never starts or signals a process.</summary>
public class GameProcessManagerExitFinalizationTests
{
    private sealed class DisposableProcess : Process
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    /// <summary>An inspection error retains the subscribed process and its stop ownership.</summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task GetActiveProcessesAsync_InspectionFails_RetainsTrackedInstanceAsync()
    {
        using var manager = new GameProcessManager(Mock.Of<ILogger<GameProcessManager>>());
        using var process = new DisposableProcess();
        const int processId = 12345;
        GetState(manager, "_managedProcesses")[processId] = process;
        var result = await manager.GetActiveProcessesAsync();
        Assert.True(result.Success);
        Assert.Same(process, GetState(manager, "_managedProcesses")[processId]);
    }

    /// <summary>Natural exits release their process after all subscribers have returned.</summary>
    /// <param name="requested">Whether the termination caller owns disposal.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnProcessExited_DisposesNaturalExitAfterNotification(bool requested)
    {
        using var manager = new GameProcessManager(Mock.Of<ILogger<GameProcessManager>>());
        using var process = new DisposableProcess();
        const int processId = 12345;
        GetState(manager, "_managedProcesses")[processId] = process;
        if (requested)
        {
            GetState(manager, "_requestedTerminations")[process] = (byte)1;
        }

        GameProcessExitedEventArgs? notification = null;
        var disposedDuringNotification = true;
        manager.ProcessExited += (_, _) => throw new InvalidOperationException("Broken subscriber");
        manager.ProcessExited += (_, args) =>
        {
            notification = args;
            disposedDuringNotification = process.WasDisposed;
        };

        // The inert process has no OS handle. The callback finds its synthetic ID in tracking.
        manager.OnProcessExited(process, EventArgs.Empty);

        Assert.NotNull(notification);
        Assert.Equal(processId, notification.ProcessId);
        Assert.Equal(requested, notification.TerminationRequested);
        Assert.False(disposedDuringNotification);
        Assert.Equal(!requested, process.WasDisposed);
        Assert.Empty(GetState(manager, "_managedProcesses"));
        manager.OnProcessExited(process, EventArgs.Empty);
    }

    /// <summary>A missing positive PID is a successful stop, using a lookup that cannot reach the OS.</summary>
    /// <returns>The asynchronous operation.</returns>
    [Fact]
    public async Task TerminateProcessAsync_MissingPositivePid_SucceedsWithoutProcessAccessAsync()
    {
        using var manager = new GameProcessManager(Mock.Of<ILogger<GameProcessManager>>());
        var lookedUp = false;
        manager.TerminationProcessLookup = pid =>
        {
            Assert.Equal(12345, pid);
            lookedUp = true;
            throw new ArgumentException("Mock missing process");
        };
        var result = await manager.TerminateProcessAsync(12345);
        Assert.True(lookedUp);
        Assert.True(result.Success);
    }

    /// <summary>A timeout fallback clears state and a delayed duplicate cannot affect a reused PID.</summary>
    [Fact]
    public void FinalizeProcessExit_DuplicateAfterPidReuse_PreservesNewProcess()
    {
        using var manager = new GameProcessManager(Mock.Of<ILogger<GameProcessManager>>());
        using var oldProcess = new Process();
        using var newProcess = new Process();
        var managed = GetState(manager, "_managedProcesses");
        var requested = GetState(manager, "_requestedTerminations");
        var buffers = GetState(manager, "_stderrBuffers");
        var bufferType = typeof(GameProcessManager).GetNestedType("BoundedErrorBuffer", BindingFlags.NonPublic)!;
        const int reusedPid = 12345;
        managed[reusedPid] = oldProcess;
        requested[oldProcess] = (byte)1;
        buffers[oldProcess] = Activator.CreateInstance(bufferType, nonPublic: true)!;
        var notifications = new List<GameProcessExitedEventArgs>();
        manager.ProcessExited += (_, e) => notifications.Add(e);

        // This finalizes state only. Neither Process has an associated OS process.
        manager.FinalizeProcessExit(oldProcess, reusedPid);
        Assert.Empty(managed);
        Assert.Empty(requested);
        Assert.Empty(buffers);
        Assert.True(Assert.Single(notifications).TerminationRequested);

        managed[reusedPid] = newProcess;
        requested[newProcess] = (byte)1;
        buffers[newProcess] = Activator.CreateInstance(bufferType, nonPublic: true)!;
        oldProcess.Dispose();
        manager.OnProcessExited(oldProcess, EventArgs.Empty);
        manager.FinalizeProcessExit(oldProcess, reusedPid);
        Assert.Same(newProcess, managed[reusedPid]);
        Assert.True(requested.Contains(newProcess));
        Assert.True(buffers.Contains(newProcess));
        Assert.Single(notifications);

        manager.FinalizeProcessExit(newProcess, reusedPid);
        Assert.Equal(2, notifications.Count);
        Assert.NotEqual(Guid.Empty, notifications[0].ProcessInstanceId);
        Assert.NotEqual(notifications[0].ProcessInstanceId, notifications[1].ProcessInstanceId);
        Assert.True(notifications[1].TerminationRequested);
        Assert.Empty(managed);
        Assert.Empty(requested);
        Assert.Empty(buffers);
    }

    private static IDictionary GetState(GameProcessManager manager, string name) =>
        (IDictionary)typeof(GameProcessManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
}
