using System;

namespace GenHub.Common.ViewModels;

/// <summary>
/// View model contract for requesting its host window to close.
/// <see cref="GenHub.Common.Controls.GenHubWindow"/> subscribes to this automatically.
/// </summary>
public interface IRequestCloseViewModel
{
    /// <summary>
    /// Raised when the host window should close.
    /// </summary>
    event EventHandler? RequestClose;
}
