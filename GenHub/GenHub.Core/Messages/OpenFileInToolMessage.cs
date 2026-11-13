namespace GenHub.Core.Messages;

/// <summary>
/// Message requesting that a tool opens a file.
/// </summary>
public sealed class OpenFileInToolMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenFileInToolMessage"/> class.
    /// </summary>
    /// <param name="toolId">The ID of the target tool plugin.</param>
    /// <param name="filePath">The full path of the file to open.</param>
    public OpenFileInToolMessage(string toolId, string filePath)
    {
        ToolId = toolId;
        FilePath = filePath;
    }

    /// <summary>
    /// Gets the ID of the target tool plugin.
    /// </summary>
    public string ToolId { get; }

    /// <summary>
    /// Gets the full path of the file to open.
    /// </summary>
    public string FilePath { get; }
}
