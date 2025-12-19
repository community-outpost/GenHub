using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Features.GameInstallations;

/// <summary>
/// Interface for detecting the language of a game installation.
/// </summary>
public interface ILanguageDetector
{
    /// <summary>
    /// Detects the language of a game installation at the specified path.
    /// </summary>
    /// <param name="installationPath">The path to the game installation directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The detected language code in uppercase (e.g., "EN", "DE"), or "EN" as fallback.</returns>
    Task<string> DetectAsync(string installationPath, CancellationToken cancellationToken = default);
}
