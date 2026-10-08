namespace GenHub.Core.Messages;

/// <summary>
/// Defines the type of tool status message.
/// </summary>
public enum MessageType
{
    /// <summary>Informational message.</summary>
    Info,

    /// <summary>Success message.</summary>
    Success,

    /// <summary>Error message.</summary>
    Error,

    /// <summary>Warning message.</summary>
    Warning,
}

/// <summary>
/// Message sent when a tool's status changes.
/// </summary>
/// <param name="Message">The status message.</param>
/// <param name="Type">The type of message.</param>
public record ToolStatusMessage(string Message, MessageType Type = MessageType.Info);
