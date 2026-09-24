using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Core.Services.Tools.WndEditor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WndEditor.Services;

/// <summary>
/// Resolves challenge general medallions from ChallengeMode.ini personas and
/// PlayerTemplate.ini medallion fields, mirroring ChallengeMenu.cpp behavior.
/// </summary>
public sealed class ChallengeMedalService(ILogger<ChallengeMedalService> logger) : IChallengeMedalService
{
    private readonly ConcurrentDictionary<string, ChallengeMedals> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<OperationResult<ChallengeMedals>> GetMedalsAsync(
        string baseRoot,
        string? overrideRoot,
        string? projectDirectory,
        IReadOnlyCollection<string>? additionalBigFiles = null,
        bool isZeroHour = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseRoot);

        var stopwatch = Stopwatch.StartNew();
        if (!isZeroHour)
        {
            return OperationResult<ChallengeMedals>.CreateSuccess(ChallengeMedals.Empty, stopwatch.Elapsed);
        }

        var key = WndGameFileSystem.BuildAssetCacheKey(baseRoot, overrideRoot, projectDirectory, additionalBigFiles, isZeroHour);
        if (_cache.TryGetValue(key, out var cached))
        {
            return OperationResult<ChallengeMedals>.CreateSuccess(cached, stopwatch.Elapsed);
        }

        try
        {
            var medals = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileSystem = WndGameFileSystem.Open(
                        baseRoot,
                        overrideRoot,
                        projectDirectory,
                        logger,
                        additionalBigFiles,
                        isZeroHour,
                        cancellationToken);
                    var challengeMode = ReadFirst(
                        fileSystem,
                        WndConstants.Challenge.ChallengeModeIniPath,
                        WndConstants.Challenge.ChallengeModeEnglishIniPath);
                    var templates = ReadFirst(
                        fileSystem,
                        WndConstants.Challenge.PlayerTemplateIniPath,
                        WndConstants.Challenge.PlayerTemplateEnglishIniPath);
                    var resolved = ResolveMedals(challengeMode, templates);
                    logger.LogInformation(
                        "Challenge medals: ChallengeMode.ini {ChallengeMode}, PlayerTemplate.ini {Templates}, {Medals} medallions, {Hidden} hidden",
                        challengeMode == null ? "missing" : $"found ({challengeMode.Length} chars)",
                        templates == null ? "missing" : $"found ({templates.Length} chars)",
                        resolved.MedalsByPosition.Count,
                        resolved.HiddenPositions.Count);
                    return resolved;
                },
                cancellationToken).ConfigureAwait(false);

            _cache[key] = medals;
            return OperationResult<ChallengeMedals>.CreateSuccess(medals, stopwatch.Elapsed);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to load challenge medals from {Root}", baseRoot);
            return OperationResult<ChallengeMedals>.CreateFailure(
                $"Failed to load challenge medals: {ex.Message}",
                stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied loading challenge medals from {Root}", baseRoot);
            return OperationResult<ChallengeMedals>.CreateFailure(
                $"Access denied loading challenge medals: {ex.Message}",
                stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public void InvalidateCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Joins personas with medallions into positions, hidden states, and images.
    /// </summary>
    /// <param name="challengeModeContent">The ChallengeMode.ini content, if any.</param>
    /// <param name="playerTemplateContent">The PlayerTemplate.ini content, if any.</param>
    /// <returns>The resolved challenge medallions.</returns>
    internal static ChallengeMedals ResolveMedals(string? challengeModeContent, string? playerTemplateContent)
    {
        var personas = ParsePersonas(challengeModeContent);
        if (personas.Count == 0)
        {
            return ChallengeMedals.Empty;
        }

        var medallions = ParseMedallions(playerTemplateContent);

        var medals = new Dictionary<int, string>();
        var hidden = new HashSet<int>();
        foreach (var (index, persona) in personas)
        {
            if (!persona.StartsEnabled)
            {
                hidden.Add(index);
                continue;
            }

            // For personas without a resolved medallion, GenHub leaves the .wnd static
            // draw data in place without generating a runtime medallion override.
            if (persona.PlayerTemplate != null && medallions.TryGetValue(persona.PlayerTemplate, out var medal))
            {
                medals[index] = medal;
            }
        }

        return new ChallengeMedals(medals, hidden);
    }

    /// <summary>
    /// Parses GeneralPersona blocks keyed by persona index.
    /// </summary>
    /// <param name="content">The ChallengeMode.ini content, if any.</param>
    /// <returns>The parsed persona entries.</returns>
    internal static IReadOnlyDictionary<int, PersonaEntry> ParsePersonas(string? content)
    {
        var result = new Dictionary<int, PersonaEntry>();
        if (string.IsNullOrEmpty(content))
        {
            return result;
        }

        int? index = null;
        string? template = null;
        var startsEnabled = false;
        var inPersona = false;
        foreach (var rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!inPersona && TryParsePersonaStart(line, out var parsed))
            {
                inPersona = true;
                index = parsed;
                template = null;
                startsEnabled = false;
                continue;
            }

            if (!inPersona)
            {
                continue;
            }

            if (IsEnd(line))
            {
                if (index.HasValue)
                {
                    result[index.Value] = new PersonaEntry(template, startsEnabled);
                }

                inPersona = false;
                index = null;
                continue;
            }

            var separator = line.IndexOf(WndConstants.MappedImages.KeySeparator);
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals(WndConstants.Challenge.PlayerTemplateField, StringComparison.OrdinalIgnoreCase))
            {
                template = value;
            }
            else if (key.Equals(WndConstants.Challenge.StartsEnabledField, StringComparison.OrdinalIgnoreCase))
            {
                startsEnabled = IsAffirmative(value);
            }
        }

        return result;
    }

    /// <summary>
    /// Parses resting medallions keyed by player template name.
    /// </summary>
    /// <param name="content">The PlayerTemplate.ini content, if any.</param>
    /// <returns>The parsed medallion names.</returns>
    internal static IReadOnlyDictionary<string, string> ParseMedallions(string? content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(content))
        {
            return result;
        }

        string? template = null;
        var inTemplate = false;
        foreach (var rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!inTemplate && TryParseTemplateStart(line, out var parsed))
            {
                inTemplate = true;
                template = parsed;
                continue;
            }

            if (!inTemplate)
            {
                continue;
            }

            if (IsEnd(line))
            {
                inTemplate = false;
                template = null;
                continue;
            }

            var separator = line.IndexOf(WndConstants.MappedImages.KeySeparator);
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (template != null
                && key.Equals(WndConstants.Challenge.MedallionRegularField, StringComparison.OrdinalIgnoreCase)
                && value.Length > 0)
            {
                result[template] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// A parsed challenge persona entry.
    /// </summary>
    /// <param name="PlayerTemplate">The player template name, if any.</param>
    /// <param name="StartsEnabled">Whether the persona starts enabled.</param>
    internal sealed record PersonaEntry(string? PlayerTemplate, bool StartsEnabled);

    private static string? ReadFirst(SageVirtualFileSystem fileSystem, string first, string second)
    {
        var bytes = fileSystem.Read(first) ?? fileSystem.Read(second);
        return bytes == null ? null : Encoding.UTF8.GetString(bytes);
    }

    private static bool TryParsePersonaStart(string line, out int index)
    {
        index = -1;
        var prefix = WndConstants.Challenge.PersonaBlockPrefix;
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || line.Length <= prefix.Length)
        {
            return false;
        }

        return int.TryParse(line[prefix.Length..].Trim(), out index);
    }

    private static bool TryParseTemplateStart(string line, out string name)
    {
        name = string.Empty;
        var tag = WndConstants.Challenge.PlayerTemplateBlockTag;
        if (!line.StartsWith(tag, StringComparison.OrdinalIgnoreCase) || line.Length <= tag.Length)
        {
            return false;
        }

        if (!char.IsWhiteSpace(line[tag.Length]))
        {
            return false;
        }

        name = line[tag.Length..].Trim();
        return name.Length > 0;
    }

    private static bool IsEnd(string line)
    {
        return string.Equals(line, WndConstants.MappedImages.EndTag, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAffirmative(string value)
    {
        return value.Equals("YES", StringComparison.OrdinalIgnoreCase)
            || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || value.Equals("1", StringComparison.Ordinal)
            || value.Equals("ON", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripComment(string line)
    {
        var comment = line.IndexOf(WndConstants.MappedImages.CommentPrefix);
        return comment < 0 ? line : line[..comment];
    }
}
