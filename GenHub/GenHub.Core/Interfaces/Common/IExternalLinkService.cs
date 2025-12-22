namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Interface for opening external links and URLs.
/// </summary>
public interface IExternalLinkService
{
    /// <summary>
    /// Opens the specified URL in the default system browser.
    /// </summary>
    /// <param name="url">The URL to open.</param>
    /// <returns>True if the URL was opened successfully, otherwise false.</returns>
    bool OpenUrl(string url);
}