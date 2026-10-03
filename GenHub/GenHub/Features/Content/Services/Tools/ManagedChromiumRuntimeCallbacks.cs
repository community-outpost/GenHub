using GenHub.Core.Helpers;
using System;

namespace GenHub.Features.Content.Services.Tools;

/// <summary>
/// Optional lifecycle callbacks and scope factory for <see cref="ManagedChromiumRuntime"/>.
/// </summary>
internal sealed record ManagedChromiumRuntimeCallbacks
{
    /// <summary>
    /// Gets an optional callback invoked when Chromium runtime installation starts.
    /// </summary>
    public Action? OnInstallStarting { get; init; }

    /// <summary>
    /// Gets an optional callback invoked when Chromium runtime installation completes.
    /// </summary>
    public Action<bool>? OnInstallCompleted { get; init; }

    /// <summary>
    /// Gets an optional callback invoked when Chromium runtime installation is canceled.
    /// </summary>
    public Action? OnInstallCanceled { get; init; }

    /// <summary>
    /// Gets an optional factory creating a <see cref="DownloadNotificationScope"/> instance.
    /// </summary>
    public Func<DownloadNotificationScope?>? ScopeFactory { get; init; }
}
