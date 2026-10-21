// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.WorldBuilder.Common;

/// <summary>
/// Fuzzy name similarity for the replace-missing dialogs. Verbatim port of the
/// qt/WBQtNameMatch.h metric: when a map references a unit or terrain texture the
/// current data set no longer has, the dialogs pre-select the closest existing
/// name instead of making the user hunt through the tree.
/// </summary>
public static class NameMatch
{
    /// <summary>
    /// Default good-enough-to-auto-select bar for the replace-missing suggestion.
    /// Admission is on the boosted score, not the raw similarity.
    /// </summary>
    public const float SuggestThreshold = 0.70f;

    /// <summary>
    /// Case-insensitive normalized similarity in [0,1] (1 is equal ignoring case,
    /// 0 is nothing in common). Two empty strings are identical.
    /// </summary>
    /// <param name="a">The first name.</param>
    /// <param name="b">The second name.</param>
    /// <returns>The normalized similarity.</returns>
    public static float Similarity(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var lowerA = a.ToLowerInvariant();
        var lowerB = b.ToLowerInvariant();
        if (lowerA.Length == 0 && lowerB.Length == 0)
        {
            return 1.0f;
        }

        if (lowerA.Length == 0 || lowerB.Length == 0)
        {
            return 0.0f;
        }

        var distance = EditDistance(lowerA, lowerB);
        var maxLength = Math.Max(lowerA.Length, lowerB.Length);
        return 1.0f - (distance / (float)maxLength);
    }

    /// <summary>
    /// Ranking score from an already-computed similarity: a candidate that contains
    /// the whole target intact beats one of the same edit distance that splices text
    /// into the middle of it. The boost closes half the remaining gap to 1.0, so it
    /// can never outrank a true equal or reorder two candidates that both contain
    /// the name; among those, edit distance still decides.
    /// </summary>
    /// <param name="target">The missing name.</param>
    /// <param name="candidate">The existing name.</param>
    /// <param name="baseScore">The already-computed similarity.</param>
    /// <returns>The boosted ranking score.</returns>
    public static float MatchScoreFromBase(string target, string candidate, float baseScore)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(candidate);
        var lowerTarget = target.ToLowerInvariant();
        var lowerCandidate = candidate.ToLowerInvariant();
        if (lowerTarget.Length == 0 || lowerCandidate == lowerTarget || !lowerCandidate.Contains(lowerTarget, StringComparison.Ordinal))
        {
            return baseScore;
        }

        return baseScore + (0.5f * (1.0f - baseScore));
    }

    /// <summary>
    /// Ranking score for a target/candidate pair.
    /// </summary>
    /// <param name="target">The missing name.</param>
    /// <param name="candidate">The existing name.</param>
    /// <returns>The boosted ranking score.</returns>
    public static float MatchScore(string target, string candidate)
    {
        return MatchScoreFromBase(target, candidate, Similarity(target, candidate));
    }

    /// <summary>
    /// Best candidate for the target, or null when nothing clears the threshold.
    /// Admits on the boosted score: admitting on the raw value would drop prefixed
    /// variants (Aslt_GLABarracks scores 0.6875 raw for GLABarracks) before the
    /// boost could rank them above spliced variants (GLAHoleBarracks, 0.7333).
    /// </summary>
    /// <param name="candidates">The existing names in tree order.</param>
    /// <param name="target">The missing name.</param>
    /// <param name="threshold">The minimum boosted score to admit.</param>
    /// <returns>The best candidate, or null.</returns>
    public static string? BestMatch(IReadOnlyList<string> candidates, string target, float threshold = SuggestThreshold)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(target);
        string? best = null;
        var bestScore = 0.0f;
        foreach (var candidate in candidates)
        {
            if (candidate.Length == 0)
            {
                continue;
            }

            var score = MatchScore(target, candidate);
            if (score < threshold || score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            best = candidate;
        }

        return best;
    }

    /// <summary>
    /// Collects every name at least threshold-similar to the target, sorted
    /// best-first with tree order kept for ties.
    /// </summary>
    /// <param name="names">The existing names in tree order.</param>
    /// <param name="target">The missing name.</param>
    /// <param name="threshold">The minimum boosted score to admit.</param>
    /// <returns>The ranked matches, best first.</returns>
    public static IReadOnlyList<string> RankMatches(IReadOnlyList<string> names, string target, float threshold = SuggestThreshold)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Length == 0)
        {
            return Array.Empty<string>();
        }

        var scored = new List<(string Name, float Score, int Index)>();
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i].Length == 0)
            {
                continue;
            }

            var score = MatchScore(target, names[i]);
            if (score >= threshold)
            {
                scored.Add((names[i], score, i));
            }
        }

        scored.Sort(static (left, right) =>
        {
            var byScore = right.Score.CompareTo(left.Score);
            return byScore != 0 ? byScore : left.Index.CompareTo(right.Index);
        });
        var ranked = new List<string>(scored.Count);
        foreach (var entry in scored)
        {
            ranked.Add(entry.Name);
        }

        return ranked;
    }

    /// <summary>
    /// Finds the index of a name for reselecting after a tree rebuild, so matches
    /// are tracked by name and a rebuild cannot dangle them. Returns -1 when absent.
    /// </summary>
    /// <param name="names">The current names.</param>
    /// <param name="name">The name to find.</param>
    /// <returns>The index, or -1.</returns>
    public static int IndexOfName(IReadOnlyList<string> names, string name)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(name);
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Cursor over ranked match names backing Find Next buttons: holds the
    /// best-first name list plus the current position, stepping with wrap-around.
    /// Kept by name so tree rebuilds are harmless.
    /// </summary>
    public sealed class MatchCursor
    {
        private IReadOnlyList<string> _names = Array.Empty<string>();
        private int _index;

        /// <summary>
        /// Gets a value indicating whether the cursor holds no names.
        /// </summary>
        public bool IsEmpty => _names.Count == 0;

        /// <summary>
        /// Gets the number of ranked names.
        /// </summary>
        public int Size => _names.Count;

        /// <summary>
        /// Loads the ranked names and resets to the best. Callers must check
        /// <see cref="IsEmpty"/> before reading <see cref="Current"/>.
        /// </summary>
        /// <param name="names">The best-first ranked names.</param>
        public void Reset(IReadOnlyList<string> names)
        {
            ArgumentNullException.ThrowIfNull(names);
            _names = names;
            _index = 0;
        }

        /// <summary>
        /// Gets the current name. Callers must check <see cref="IsEmpty"/> first.
        /// </summary>
        public string Current => _names[_index];

        /// <summary>
        /// Steps forward (dir = +1) or back (dir = -1) with wrap-around and
        /// returns the new current name. Callers must check
        /// <see cref="IsEmpty"/> first.
        /// </summary>
        /// <param name="direction">The step direction.</param>
        /// <returns>The new current name.</returns>
        public string Step(int direction)
        {
            var count = _names.Count;
            _index = (_index + direction + count) % count;
            return _names[_index];
        }
    }

    /// <summary>
    /// The pickers shared arming sequence: ranks the close matches, loads their
    /// names into the cursor, pre-selects the best, and reports whether Find Next
    /// navigation has more than one match to cycle. Returns true when at least one
    /// match cleared the bar.
    /// </summary>
    /// <param name="cursor">The cursor to arm.</param>
    /// <param name="names">The existing names in tree order.</param>
    /// <param name="target">The missing name.</param>
    /// <param name="selectBest">Selects a name in the tree, if any match clears the bar.</param>
    /// <param name="threshold">The minimum boosted score to admit.</param>
    /// <returns>True when at least one match cleared the bar.</returns>
    public static bool ArmMatchCursor(
        MatchCursor cursor,
        IReadOnlyList<string> names,
        string target,
        Action<string>? selectBest,
        float threshold = SuggestThreshold)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(target);
        var ranked = RankMatches(names, target, threshold);
        cursor.Reset(ranked);
        if (!cursor.IsEmpty)
        {
            selectBest?.Invoke(cursor.Current);
        }

        return !cursor.IsEmpty;
    }

    /// <summary>
    /// The Find Next slot body: steps the cursor and selects the new current
    /// match. No-op while the cursor is empty.
    /// </summary>
    /// <param name="cursor">The armed cursor.</param>
    /// <param name="direction">The step direction.</param>
    /// <param name="selectName">Selects a name in the tree.</param>
    public static void StepMatchCursor(MatchCursor cursor, int direction, Action<string> selectName)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(selectName);
        if (!cursor.IsEmpty)
        {
            selectName(cursor.Step(direction));
        }
    }

    private static int EditDistance(string lowerA, string lowerB)
    {
        // Levenshtein edit distance, two-row rolling buffer.
        var previous = new int[lowerB.Length + 1];
        var current = new int[lowerB.Length + 1];
        for (var j = 0; j < previous.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= lowerA.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j < current.Length; j++)
            {
                current[j] = EditCost(lowerA[i - 1], lowerB[j - 1], previous[j], current[j - 1], previous[j - 1]);
            }

            (previous, current) = (current, previous);
        }

        return previous[lowerB.Length];
    }

    private static int EditCost(char a, char b, int deletion, int insertion, int substitution)
    {
        var cost = a == b ? 0 : 1;
        return Math.Min(deletion + 1, Math.Min(insertion + 1, substitution + cost));
    }
}
