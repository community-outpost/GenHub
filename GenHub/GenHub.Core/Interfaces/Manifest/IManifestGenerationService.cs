using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Manifest;

/// <summary>
/// High-level service for generating different types of content manifests.
/// </summary>
public interface IManifestGenerationService
{
    /// <summary>
    /// Creates a manifest builder for a game installation with string version normalization.
    /// </summary>
    /// <param name="gameInstallationPath">Path to the game installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="installationType">The installation type (Steam, EaApp).</param>
    /// <param name="manifestVersion">The manifest version (e.g., "1.08", "1.04", or integer like 0, 1, 2). If null, defaults to 0.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateGameInstallationManifestAsync(string gameInstallationPath, GameType gameType, GameInstallationType installationType, string? manifestVersion = null);

    /// <summary>
    /// Creates a manifest builder for a game installation with integer version.
    /// </summary>
    /// <param name="gameInstallationPath">Path to the game installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="installationType">The installation type (Steam, EaApp).</param>
    /// <param name="manifestVersion">The manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateGameInstallationManifestAsync(string gameInstallationPath, GameType gameType, GameInstallationType installationType, int manifestVersion = 0);

    /// <summary>
    /// Creates a manifest builder for any content type (mod, patch, addon, etc).
    /// </summary>
    /// <param name="contentDirectory">Path to the content directory.</param>
    /// <param name="publisherId">The publisher identifier used to deterministically generate the manifest id.</param>
    /// <param name="contentName">Content display name.</param>
    /// <param name="manifestVersion">Manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="contentType">Type of content (Mod, Patch, Addon, etc).</param>
    /// <param name="targetGame">Target game type.</param>
    /// <param name="dependencies">Dependencies for this content.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateContentManifestAsync(
        string contentDirectory,
        string publisherId,
        string contentName,
        int manifestVersion = 0,
        ContentType contentType = ContentType.Mod,
        GameType targetGame = GameType.Generals,
        params ContentDependency[] dependencies);

    /// <summary>
    /// Creates a manifest builder for a game client.
    /// </summary>
    /// <param name="installationPath">Path to the game client installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="clientName">The name of the game client.</param>
    /// <param name="clientVersion">The version of the game client.</param>
    /// <param name="executablePath">The full path to the game executable.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateGameClientManifestAsync(
        string installationPath,
        GameType gameType,
        string clientName,
        string clientVersion,
        string executablePath);

    /// <summary>
    /// Creates a manifest builder for a GeneralsOnline game client with special handling.
    /// GeneralsOnline clients are auto-updated, so hash validation is bypassed until a dedicated
    /// publisher system is implemented for downloading and updating via content manifest endpoints.
    /// </summary>
    /// <param name="installationPath">Path to the game client installation.</param>
    /// <param name="gameType">The game type (Generals, ZeroHour).</param>
    /// <param name="clientName">The name of the GeneralsOnline client.</param>
    /// <param name="clientVersion">The version of the client (typically "Auto-Updated").</param>
    /// <param name="executablePath">The full path to the GeneralsOnline executable.</param>
    /// <returns>A <see cref="Task"/> that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateGeneralsOnlineClientManifestAsync(
        string installationPath,
        GameType gameType,
        string clientName,
        string clientVersion,
        string executablePath);

    /// <summary>
    /// Saves a manifest to a file.
    /// </summary>
    /// <param name="manifest">The manifest to save.</param>
    /// <param name="outputPath">The output file path.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task SaveManifestAsync(ContentManifest manifest, string outputPath);

    /// <summary>
    /// Creates a content bundle from multiple content items.
    /// </summary>
    /// <param name="publisherId">The publisher identifier used to generate the bundle id.</param>
    /// <param name="bundleName">The bundle name.</param>
    /// <param name="manifestVersion">The manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="publisher">The publisher information.</param>
    /// <param name="items">The bundle items.</param>
    /// <returns>A <see cref="Task"/> that returns the created <see cref="ContentBundle"/>.</returns>
    Task<ContentBundle> CreateContentBundleAsync(
        string publisherId,
        string bundleName,
        int manifestVersion = 0,
        PublisherInfo? publisher = null,
        params BundleItem[] items);

    /// <summary>
    /// Creates a publisher referral manifest with a deterministic id.
    /// </summary>
    /// <param name="publisherId">The publisher identifier used to generate the referral id.</param>
    /// <param name="referralName">Display name for the referral.</param>
    /// <param name="manifestVersion">Manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="targetPublisherId">The target publisher id being referred to.</param>
    /// <param name="referralUrl">The URL for the referral.</param>
    /// <param name="description">Optional description for the referral.</param>
    /// <returns>A <see cref="Task"/> that returns the created <see cref="ContentManifest"/>.</returns>
    Task<ContentManifest> CreatePublisherReferralAsync(
        string publisherId,
        string referralName,
        int manifestVersion = 0,
        string targetPublisherId = "",
        string referralUrl = "",
        string description = "");

    /// <summary>
    /// Creates a content referral manifest that references another content id.
    /// </summary>
    /// <param name="publisherId">The publisher identifier used to generate the referral id.</param>
    /// <param name="referralName">Display name for the referral.</param>
    /// <param name="manifestVersion">Manifest version (e.g., 1, 2, 20). Defaults to 0 for first version.</param>
    /// <param name="targetContentId">The id of the content being referred to.</param>
    /// <param name="targetPublisherId">The publisher id of the target content.</param>
    /// <param name="referralUrl">The URL for the referral.</param>
    /// <param name="description">Optional description for the referral.</param>
    /// <returns>A <see cref="Task"/> that returns the created <see cref="ContentManifest"/>.</returns>
    Task<ContentManifest> CreateContentReferralAsync(
        string publisherId,
        string referralName,
        int manifestVersion = 0,
        string targetContentId = "",
        string targetPublisherId = "",
        string referralUrl = "",
        string description = "");
}