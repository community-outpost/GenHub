namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Interface for files that can be exported or uploaded.
/// </summary>
public interface IExportableFile
{
    /// <summary>
    /// Gets the file name.
    /// </summary>
    string FileName { get; }

    /// <summary>
    /// Gets the full path to the file.
    /// </summary>
    string FullPath { get; }
}
