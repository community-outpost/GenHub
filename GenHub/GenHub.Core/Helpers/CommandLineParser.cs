using GenHub.Core.Constants;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides helper methods for parsing command line arguments.
/// </summary>
public static class CommandLineParser
{
    /// <summary>
    /// Extracts a profile identifier from command line arguments.
    /// Supports both spaced and inline formats: <c>--launch-profile &lt;id&gt;</c> and <c>--launch-profile=&lt;id&gt;</c>.
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
                return args[i + 1].Trim('"');
            }

            if (arg.StartsWith(CommandLineConstants.LaunchProfileInlinePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[CommandLineConstants.LaunchProfileInlinePrefix.Length..].Trim('"');
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the absolute URL from a <c>genhub://subscribe?url=...</c> startup argument.
    /// </summary>
    /// <remarks>
    /// The returned value is the <c>url</c> query value only (not the <c>genhub://</c> wrapper).
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
                    string unescaped = Uri.UnescapeDataString(url)
                        .Replace("\r", string.Empty)
                        .Replace("\n", string.Empty)
                        .Trim('"', '\'', ' ', '\t');

                    if (string.IsNullOrWhiteSpace(unescaped))
                    {
                        return null;
                    }

                    if (Uri.TryCreate(unescaped, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        return unescaped;
                    }

                    return null;
                }
            }
        }

        return null;
    }
}