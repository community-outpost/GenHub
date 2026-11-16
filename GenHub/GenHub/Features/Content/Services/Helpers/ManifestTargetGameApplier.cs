using GenHub.Core.Helpers;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Helpers;

/// <summary>
/// Applies a binary-sniffed target game to a freshly baked game client manifest, shared
/// by every manifest factory so discovery inference and factory output agree.
/// </summary>
public static class ManifestTargetGameApplier
{
    /// <summary>
    /// Sniffs the manifest's baked entry binary and updates the target game when the
    /// binary proves a different type. Never clears or guesses: without confident
    /// evidence the manifest keeps its type untouched.
    /// </summary>
    /// <param name="logger">The calling factory's logger.</param>
    /// <param name="manifest">The manifest being built.</param>
    /// <param name="extractedDirectory">The extracted payload root the entry is relative to.</param>
    /// <param name="cancellationToken">Cancels the binary scan.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public static async Task ApplyBinaryTargetGameAsync(
        ILogger logger,
        ContentManifest manifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        var binaryGameType = await GameClientTargetGameResolver.ResolveFromEntryBinaryAsync(manifest, extractedDirectory, cancellationToken);
        if (binaryGameType.HasValue && binaryGameType.Value != manifest.TargetGame)
        {
            logger.LogInformation(
                "Entry binary identifies {GameType}; updating manifest {ManifestId} target game (was {OldGameType})",
                binaryGameType.Value,
                manifest.Id,
                manifest.TargetGame);
            manifest.TargetGame = binaryGameType.Value;
        }
    }
}
