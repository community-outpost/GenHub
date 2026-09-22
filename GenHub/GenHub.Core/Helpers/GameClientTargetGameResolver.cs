using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Utilities;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Helpers;

/// <summary>
/// Resolves a game client's target game from its baked launch binary, shared by every
/// manifest factory so discovery inference, local detection, and factory output agree.
/// <para>
/// Binary sniffing only ever proves a concrete game (Zero Hour title plus menu token,
/// or the Generals-exclusive engine token), so this returns a value solely when the
/// entry binary carries exclusive evidence. Anything else — missing files, launchers,
/// conflicting markers, ambiguous bytes — yields <c>null</c> and the manifest keeps
/// its type.
/// </para>
/// </summary>
public static class GameClientTargetGameResolver
{
    /// <summary>
    /// Sniffs the manifest's baked entry binary for a confident game-type verdict.
    /// </summary>
    /// <param name="manifest">The manifest whose entry binary is inspected.</param>
    /// <param name="extractedDirectory">The extracted payload root the entry is relative to.</param>
    /// <param name="cancellationToken">Cancels the binary scan.</param>
    /// <returns>The proven game type, otherwise <c>null</c> (keep the manifest type).</returns>
    public static async Task<GameType?> ResolveFromEntryBinaryAsync(
        ContentManifest manifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.ContentType != ContentType.GameClient
            || string.IsNullOrWhiteSpace(manifest.EntryPoint)
            || string.IsNullOrWhiteSpace(extractedDirectory))
        {
            return null;
        }

        var resolved = ContentPathPolicy.ResolveContainedFile(extractedDirectory, manifest.EntryPoint);
        if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.Data))
        {
            return null;
        }

        var absolutePath = resolved.Data;
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var inspection = await GameBinaryInspector.InspectAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        if (!inspection.Success || inspection.Data is null)
        {
            return null;
        }

        return inspection.Data.GameType switch
        {
            GameType.ZeroHour => GameType.ZeroHour,
            GameType.Generals => GameType.Generals,
            _ => null,
        };
    }
}
