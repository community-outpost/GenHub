using System.Threading.Tasks;
using GenHub.Core;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// High-level service for generating different types of content manifests.
/// </summary>
public interface IManifestGenerationService
{
    /// <summary>
    /// Creates a manifest builder for a base game installation.
    /// </summary>
    /// <param name="gameInstallationPath">Path to the game installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="installationType">The installation type (Steam, EaApp).</param>
    /// <param name="version">The game version (e.g., "1.04", "1.08").</param>
    /// <returns>A task that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateBaseGameManifestAsync(string gameInstallationPath, GameType gameType, GameInstallationType installationType, string version);

    /// <summary>
    /// Creates a manifest builder for any content type (mod, patch, addon, etc).
    /// </summary>
    /// <param name="contentDirectory">Path to the content directory.</param>
    /// <param name="contentId">Unique content identifier.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="contentVersion">Content version.</param>
    /// <param name="contentType">Type of content (Mod, Patch, Addon, etc).</param>
    /// <param name="targetGame">Target game type.</param>
    /// <param name="dependencies">Dependencies for this content.</param>
    /// <returns>A task that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateContentManifestAsync(
        string contentDirectory,
        string contentId,
        string contentName,
        string contentVersion,
        ContentType contentType,
        GameType targetGame,
        params ContentDependency[] dependencies);

    /// <summary>
    /// Creates a manifest builder for a standalone game version.
    /// </summary>
    /// <param name="gameDirectory">Path to the standalone game directory.</param>
    /// <param name="gameId">Unique game version identifier.</param>
    /// <param name="gameName">Game version display name.</param>
    /// <param name="gameVersion">Game version.</param>
    /// <param name="executablePath">Path to the main executable.</param>
    /// <returns>A task that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateStandaloneGameManifestAsync(string gameDirectory, string gameId, string gameName, string gameVersion, string executablePath);

    /// <summary>
    /// Saves a manifest to a file.
    /// </summary>
    /// <param name="manifest">The manifest to save.</param>
    /// <param name="outputPath">The output file path.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveManifestAsync(GameManifest manifest, string outputPath);

    /// <summary>
    /// Creates a content bundle from multiple content items.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <param name="bundleName">The bundle name.</param>
    /// <param name="bundleVersion">The bundle version.</param>
    /// <param name="publisher">The publisher information.</param>
    /// <param name="items">The bundle items.</param>
    /// <returns>A task that returns the created content bundle.</returns>
    Task<ContentBundle> CreateContentBundleAsync(
        string bundleId,
        string bundleName,
        string bundleVersion,
        PublisherInfo publisher,
        params BundleItem[] items);
}
