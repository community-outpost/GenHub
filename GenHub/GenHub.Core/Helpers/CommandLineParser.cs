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
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

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
    /// Extracts a subscription URL from command line arguments.
    /// Supports the URI scheme format: <c>genhub://subscribe?url=&lt;url&gt;</c>.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>The extracted catalog URL if present; otherwise, <c>null</c>.</returns>
    public static string? ExtractSubscriptionUrl(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(CommandLineConstants.SubscribeUriPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // Simple parsing for ?url=...
                var queryStart = arg.IndexOf(CommandLineConstants.SubscribeUrlParam, StringComparison.OrdinalIgnoreCase);
                if (queryStart != -1)
                {
                    var url = arg[(queryStart + CommandLineConstants.SubscribeUrlParam.Length)..];
                    return Uri.UnescapeDataString(url).Trim('"');
                }
            }
        }

        return null;
    }
}