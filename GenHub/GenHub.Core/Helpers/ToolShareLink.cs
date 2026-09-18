using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace GenHub.Core.Helpers;

/// <summary>
/// Builds and parses <c>genhub://</c> share URIs for the map and replay managers.
/// </summary>
public static class ToolShareLink
{
    /// <summary>
    /// Builds a share URI that opens GenHub and imports the download at <paramref name="innerUrl"/>
    /// into the tool identified by <paramref name="toolCommand"/>.
    /// </summary>
    /// <param name="toolCommand">The tool path segment (<c>map</c> or <c>replay</c>).</param>
    /// <param name="innerUrl">The absolute HTTP or HTTPS download URL to import.</param>
    /// <param name="game">The optional target game recorded in the share URI.</param>
    /// <returns>The share URI (for example <c>genhub://map/import?url=...</c>).</returns>
    /// <exception cref="ArgumentException">Thrown when the tool command or download URL is invalid.</exception>
    public static string BuildShareUri(string toolCommand, string innerUrl, GameType? game = null)
    {
        var prefix = GetUriPrefix(toolCommand);

        if (string.IsNullOrWhiteSpace(innerUrl) || !IsAllowedDownloadUrl(innerUrl))
        {
            throw new ArgumentException("Download URL must be an absolute HTTP or HTTPS URL.", nameof(innerUrl));
        }

        var builder = new StringBuilder(prefix);
        builder.Append('?');
        builder.Append(CommandLineConstants.UrlQueryParam);
        builder.Append(Uri.EscapeDataString(innerUrl.Trim()));

        if (game.HasValue)
        {
            builder.Append('&');
            builder.Append(CommandLineConstants.GameQueryParam);
            builder.Append(ToGameValue(game.Value));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses a <c>genhub://map/import</c> or <c>genhub://replay/import</c> share URI.
    /// </summary>
    /// <param name="input">The raw input, possibly quoted or padded with whitespace.</param>
    /// <param name="target">The parsed share target when parsing succeeds; otherwise, <c>null</c>.</param>
    /// <returns><see langword="true"/> when <paramref name="input"/> is a well-formed tool share URI; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseShareUri(string? input, out ToolShareTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var cleaned = CommandLineParser.SanitizePayload(input.Trim()).Trim('"', '\'', ' ', '\t');
        var toolCommand = ExtractToolCommand(cleaned);
        if (toolCommand == null)
        {
            return false;
        }

        var query = ExtractQuery(cleaned);
        if (query == null)
        {
            return false;
        }

        var parameters = ParseQueryParameters(query);
        if (!parameters.TryGetValue(CommandLineConstants.UrlQueryKey, out var encodedUrl) ||
            string.IsNullOrWhiteSpace(encodedUrl))
        {
            return false;
        }

        var innerUrl = CommandLineParser.SanitizePayload(Uri.UnescapeDataString(encodedUrl).Trim());
        if (!IsAllowedDownloadUrl(innerUrl))
        {
            return false;
        }

        GameType? game = null;
        if (parameters.TryGetValue(CommandLineConstants.GameQueryKey, out var gameValue) &&
            !string.IsNullOrWhiteSpace(gameValue))
        {
            game = ParseGameValue(gameValue);
            if (game == null)
            {
                return false;
            }
        }

        target = new ToolShareTarget(toolCommand, innerUrl, game);
        return true;
    }

    /// <summary>
    /// Normalizes an import text box value to a plain download URL.
    /// </summary>
    /// <remarks>
    /// Tool share URIs for <paramref name="toolCommand"/> unwrap to their inner download URL.
    /// Every other input (plain URLs, match IDs) passes through unchanged.
    /// </remarks>
    /// <param name="input">The raw pasted input.</param>
    /// <param name="toolCommand">The tool path segment (<c>map</c> or <c>replay</c>).</param>
    /// <returns>The inner download URL for matching share URIs; otherwise, <paramref name="input"/>.</returns>
    public static string NormalizeImportUrl(string input, string toolCommand)
    {
        if (TryParseShareUri(input, out var target) &&
            target != null &&
            target.ToolCommand.Equals(toolCommand, StringComparison.OrdinalIgnoreCase))
        {
            return target.Url;
        }

        return input;
    }

    /// <summary>
    /// Builds the single-instance IPC command forwarding a tool share URI to the primary instance.
    /// </summary>
    /// <param name="toolShareUri">The sanitized share URI, or <c>null</c> when no share URI was provided.</param>
    /// <returns>The IPC command (for example <c>import-map:genhub://...</c>); otherwise, <c>null</c>.</returns>
    public static string? BuildIpcCommand(string? toolShareUri)
    {
        if (string.IsNullOrEmpty(toolShareUri) ||
            !TryParseShareUri(toolShareUri, out var target) ||
            target == null)
        {
            return null;
        }

        var prefix = target.ToolCommand.Equals(CommandLineConstants.MapCommand, StringComparison.OrdinalIgnoreCase)
            ? IpcCommands.ImportMapPrefix
            : IpcCommands.ImportReplayPrefix;
        return $"{prefix}{toolShareUri}";
    }

    /// <summary>
    /// Determines whether the input is a share URI for a different tool than expected.
    /// </summary>
    /// <param name="input">The raw pasted input.</param>
    /// <param name="toolCommand">The tool path segment (<c>map</c> or <c>replay</c>).</param>
    /// <returns><see langword="true"/> when the input targets the other tool; otherwise, <see langword="false"/>.</returns>
    public static bool IsOtherToolShareUri(string? input, string toolCommand)
    {
        return TryParseShareUri(input, out var target) &&
            target != null &&
            !target.ToolCommand.Equals(toolCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUriPrefix(string toolCommand)
    {
        if (toolCommand.Equals(CommandLineConstants.MapCommand, StringComparison.OrdinalIgnoreCase))
        {
            return CommandLineConstants.MapImportUriPrefix;
        }

        if (toolCommand.Equals(CommandLineConstants.ReplayCommand, StringComparison.OrdinalIgnoreCase))
        {
            return CommandLineConstants.ReplayImportUriPrefix;
        }

        throw new ArgumentException($"Unknown tool command: {toolCommand}.", nameof(toolCommand));
    }

    private static string? ExtractToolCommand(string cleaned)
    {
        if (cleaned.StartsWith(CommandLineConstants.MapImportUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return CommandLineConstants.MapCommand;
        }

        if (cleaned.StartsWith(CommandLineConstants.ReplayImportUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return CommandLineConstants.ReplayCommand;
        }

        return null;
    }

    private static string? ExtractQuery(string cleaned)
    {
        int queryIndex = cleaned.IndexOf('?');
        if (queryIndex == -1 || queryIndex == cleaned.Length - 1)
        {
            return null;
        }

        var path = cleaned[..queryIndex];
        if (!IsToolImportPath(path))
        {
            return null;
        }

        return cleaned[(queryIndex + 1)..];
    }

    private static bool IsToolImportPath(string path)
    {
        return path.Equals(CommandLineConstants.MapImportUriPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.Equals(CommandLineConstants.MapImportUriPrefix + "/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals(CommandLineConstants.ReplayImportUriPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.Equals(CommandLineConstants.ReplayImportUriPrefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseQueryParameters(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            int separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = pair[..separator].Trim();
            if (key.Length == 0 || parameters.ContainsKey(key))
            {
                continue;
            }

            parameters[key] = pair[(separator + 1)..].Trim();
        }

        return parameters;
    }

    private static GameType? ParseGameValue(string value)
    {
        var normalized = value.Trim();
        if (normalized.Equals(CommandLineConstants.GameGeneralsValue, StringComparison.OrdinalIgnoreCase))
        {
            return GameType.Generals;
        }

        if (normalized.Equals(CommandLineConstants.GameZeroHourValue, StringComparison.OrdinalIgnoreCase))
        {
            return GameType.ZeroHour;
        }

        return null;
    }

    private static string ToGameValue(GameType game)
    {
        return game == GameType.Generals
            ? CommandLineConstants.GameGeneralsValue
            : CommandLineConstants.GameZeroHourValue;
    }

    private static bool IsAllowedDownloadUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
