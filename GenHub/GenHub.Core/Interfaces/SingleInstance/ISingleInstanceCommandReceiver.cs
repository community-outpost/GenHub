using System;

namespace GenHub.Core.Interfaces.SingleInstance;

/// <summary>
/// Defines the contract for receiving single-instance commands.
/// </summary>
public interface ISingleInstanceCommandReceiver
{
    /// <summary>
    /// Raised when a command is received from another instance.
    /// </summary>
    event EventHandler<string>? CommandReceived;
}
