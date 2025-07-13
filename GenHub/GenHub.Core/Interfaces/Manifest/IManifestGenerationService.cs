using GenHub.Core;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using System.Threading.Tasks;

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
    /// Creates a manifest builder for a mod.
    /// </summary>
    /// <param name="modDirectory">Path to the mod directory.</param>
    /// <param name="modId">Unique mod identifier.</param>
    /// <param name="modName">Mod display name.</param>
    /// <param name="modVersion">Mod version.</param>
    /// <param name="targetGame">Target game type.</param>
    /// <param name="baseGameDependencies">Required base game versions.</param>
    /// <returns>A task that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateModManifestAsync(string modDirectory, string modId, string modName, string modVersion, GameType targetGame, params string[] baseGameDependencies);

    /// <summary>
    /// Creates a manifest builder for an addon/utility.
    /// </summary>
    /// <param name="addonDirectory">Path to the addon directory.</param>
    /// <param name="addonId">Unique addon identifier.</param>
    /// <param name="addonName">Addon display name.</param>
    /// <param name="addonVersion">Addon version.</param>
    /// <param name="targetGame">Target game type.</param>
    /// <returns>A task that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreateAddonManifestAsync(string addonDirectory, string addonId, string addonName, string addonVersion, GameType targetGame);

    /// <summary>
    /// Creates a manifest builder for a patch.
    /// </summary>
    /// <param name="patchDirectory">Path to the patch directory.</param>
    /// <param name="patchId">Unique patch identifier.</param>
    /// <param name="patchName">Patch display name.</param>
    /// <param name="patchVersion">Patch version.</param>
    /// <param name="targetContent">What this patch applies to.</param>
    /// <param name="targetVersion">Version of the target content.</param>
    /// <returns>A task that returns a configured manifest builder.</returns>
    Task<IContentManifestBuilder> CreatePatchManifestAsync(string patchDirectory, string patchId, string patchName, string patchVersion, string targetContent, string targetVersion);

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
}
