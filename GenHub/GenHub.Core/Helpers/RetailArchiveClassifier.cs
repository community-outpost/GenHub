using System;
using System.IO;
using GenHub.Core.Constants;
using GenHub.Core.Models.GameInstallations;

namespace GenHub.Core.Helpers;

/// <summary>
/// Classifies a directory by the retail game archives it holds.
/// </summary>
/// <remarks>
/// This is the detection-side predicate on top of the <see cref="RetailArchiveConstants"/>
/// vocabulary: it decides <em>which</em> games' retail data a directory carries. It is
/// deliberately separate from the launch-side any-archive check
/// (<c>GameLauncher.ValidateRetailArchiveRoots</c>), which validates a root that has
/// already been chosen and stays game-agnostic so it cannot reject a valid root over a
/// localisation difference. Both build on <see cref="RetailArchiveConstants.ArchiveSearch"/>
/// so every call site matches archive files identically, case-insensitivity included.
/// </remarks>
public static class RetailArchiveClassifier
{
    /// <summary>
    /// Determines which games' retail archives are present in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The directory to classify.</param>
    /// <returns>
    /// The classification; an absent or null directory classifies as holding neither game.
    /// A directory holding only unrecognised archives (mods, hotkey packs, control bars)
    /// also classifies as neither — an arbitrary <c>.big</c> proves nothing about retail
    /// data, which is exactly why the executable-name proxy this replaces was retired.
    /// </returns>
    /// <remarks>
    /// Only the directory root is examined, never subdirectories: <c>Data/INI/INIZH.big</c>
    /// is a duplicate shipped in the English, Chinese and Korean SKUs and must not be
    /// counted twice. Filesystem errors (an unreadable directory, an I/O failure) propagate
    /// to the caller rather than reading as "no archives" — converting a permission problem
    /// into missing content is the failure mode
    /// <see cref="RetailArchiveConstants.ArchiveSearch"/> exists to prevent.
    /// </remarks>
    public static RetailArchiveClassification ClassifyArchives(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return default;
        }

        var hasGenerals = false;
        var hasZeroHour = false;

        foreach (var archivePath in Directory.EnumerateFiles(
            directory,
            RetailArchiveConstants.ArchiveSearchPattern,
            RetailArchiveConstants.ArchiveSearch))
        {
            var archiveName = Path.GetFileName(archivePath);

            if (!hasZeroHour &&
                archiveName.EndsWith(RetailArchiveConstants.ZeroHourArchiveSuffix, StringComparison.OrdinalIgnoreCase))
            {
                hasZeroHour = true;
            }

            if (!hasGenerals && RetailArchiveConstants.GeneralsArchiveNames.Contains(archiveName))
            {
                hasGenerals = true;
            }

            if (hasGenerals && hasZeroHour)
            {
                break;
            }
        }

        return new RetailArchiveClassification(hasGenerals, hasZeroHour);
    }
}
