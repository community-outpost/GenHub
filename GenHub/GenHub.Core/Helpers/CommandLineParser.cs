using GenHub.Core.Constants;
using System;
using System.Text;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides helper methods for parsing command line arguments.
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// Extracts a profile identifier from command line arguments.
    /// Supports both spaced and inline formats: <c>--launch-profile &lt;id&gt;</c> and <c>--launch-profile=&lt;id&gt;</c>.<br/>
    /// Strips balanced quotes around the identifier if present.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The extracted profile identifier if present; otherwise, <c>null</c>.</returns>
    public static string? ExtractProfileId(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg.Equals(CommandLineConstants.LaunchProfileArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var nextArg = args[i + 1];
                if (!nextArg.StartsWith('-'))
                {
                    var id = SanitizePayload(Unquote(nextArg));
                    return string.IsNullOrWhiteSpace(id) ? null : id;
                }
            }

            if (arg.StartsWith(CommandLineConstants.LaunchProfileInlinePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var id = SanitizePayload(Unquote(arg[CommandLineConstants.LaunchProfileInlinePrefix.Length..]));
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the absolute URL from a single <c>genhub://subscribe?url=...</c> argument or URL string.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="ExtractSubscriptionUrl(string[])"/> to parse the single argument.
    /// </remarks>
    /// <param name="arg">The single command line argument or URL string.</param>
    /// <returns>The decoded absolute URL if present; otherwise, <c>null</c>.</returns>
    public static string? ExtractSubscriptionUrl(string? arg)
    {
        return string.IsNullOrWhiteSpace(arg) ? null : ExtractSubscriptionUrl([arg]);
    }

    /// <summary>
    /// Extracts the absolute URL from a <c>genhub://subscribe?url=...</c> startup argument.
    /// </summary>
    /// <remarks>
    /// The returned value is the <c>url</c> query value only (not the <c>genhub://</c> wrapper).<br/>
    /// Callers treat it as a GenHub catalog JSON URL today; later it may also be a Provider
    /// Definition URL without changing this parser.
    /// </remarks>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The decoded absolute URL if present; otherwise, <c>null</c>.</returns>
    public static string? ExtractSubscriptionUrl(string[] args)
    {
        foreach (string arg in args)
        {
            if (arg.StartsWith(CommandLineConstants.SubscribeUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string remainder = arg[CommandLineConstants.SubscribeUriPrefix.Length..];
                if (!remainder.StartsWith('?') && !remainder.StartsWith("/?", StringComparison.Ordinal))
                {
                    continue;
                }

                int queryStart = arg.IndexOf(CommandLineConstants.SubscribeUrlParam, StringComparison.OrdinalIgnoreCase);
                if (queryStart != -1)
                {
                    string url = arg[(queryStart + CommandLineConstants.SubscribeUrlParam.Length)..];
                    string unescaped = SanitizePayload(Uri.UnescapeDataString(url).Trim(' ', '\t'));
                    unescaped = Unquote(unescaped);

                    if (string.IsNullOrWhiteSpace(unescaped))
                    {
                        return null;
                    }

                    if (Uri.TryCreate(unescaped, UriKind.Absolute, out var uri))
                    {
                        if (uri.Scheme == Uri.UriSchemeHttps)
                        {
                            return unescaped;
                        }

                        // Allow local non-UNC file:// URIs matching CatalogDocumentReader rules
                        if (uri.IsFile && !uri.IsUnc && string.IsNullOrEmpty(uri.Host))
                        {
                            return unescaped;
                        }
                    }

                    return null;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts a profile share URI, catalog view URI, or .ghprofile file path from command line arguments.
    /// Supports direct <c>genhub://profile/...</c> URIs, <c>--import-profile &lt;target&gt;</c>, <c>--import-profile=&lt;target&gt;</c>, and <c>.ghprofile</c> file paths.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The extracted share URI or file path if present; otherwise, <c>null</c>.</returns>
    public static string? ExtractProfileShareUri(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string rawArg = args[i].Trim();
            string sanitizedArg = SanitizePayload(rawArg);
            string arg = Unquote(sanitizedArg.Trim());

            if (arg.Equals(CommandLineConstants.ImportProfileArg, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var nextArg = args[i + 1];
                if (!nextArg.StartsWith('-'))
                {
                    var result = SanitizePayload(Unquote(nextArg.Trim()));
                    return string.IsNullOrWhiteSpace(result) ? null : result;
                }
            }

            if (arg.StartsWith(CommandLineConstants.ImportProfileInlinePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var result = SanitizePayload(Unquote(arg[CommandLineConstants.ImportProfileInlinePrefix.Length..].Trim()));
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }

            if (arg.StartsWith(CommandLineConstants.ProfileImportUriPrefix, StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith(CommandLineConstants.ProfileViewUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg;
            }

            if (arg.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                return arg;
            }
        }

        return null;
    }

    /// <summary>
    /// Strips C0 control characters (including CRLF and nulls) and the DEL character from command line and IPC payloads.
    /// </summary>
    /// <param name="input">The input string to sanitize.</param>
    /// <returns>The sanitized string.</returns>
    public static string SanitizePayload(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c < 0x20 || c == 0x7F)
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string Unquote(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        s = s.Trim();
        if (s.Length >= 2 &&
            ((s[0] == '"' && s[^1] == '"') ||
             (s[0] == '\'' && s[^1] == '\'')))
        {
            return s[1..^1].Trim();
        }

        return s;
    }
}
