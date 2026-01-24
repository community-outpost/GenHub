using Microsoft.Extensions.Logging;

namespace GenHub.Tools;

/// <summary>
/// CSV Generation Utility for creating authoritative CSV files from game installations.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Main entry point for the CSV Generation Utility.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("CsvGenerator");

        try
        {
            if (args.Length < 2 || args.Contains("--help"))
            {
                logger.LogInformation("Usage: GenHub.Tools --installDir <path> --gameType <Generals|ZeroHour> --version <v> --output <file> [--language <code>]");
                logger.LogInformation("  Default for --language: EN");
                logger.LogInformation("Example: GenHub.Tools --installDir C:\\Games\\Generals --gameType Generals --version 1.08 --output docs/GameInstallationFilesRegistry/Generals-1.08.csv --language EN");
                return;
            }

            var arguments = ParseArguments(args);
            ValidateArguments(arguments);

            logger.LogInformation("Starting CSV Generation Utility");
            logger.LogInformation(
                "Configuration: InstallDir={InstallDir}, GameType={GameType}, Version={Version}, Language={Language}",
                arguments["installDir"],
                arguments["gameType"],
                arguments["version"],
                arguments["language"]);

            var generator = new CsvGenerator(arguments, logger);
            await generator.GenerateCsvFileAsync();

            logger.LogInformation("CSV Generation Utility completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CSV Generation Utility failed");
            Environment.Exit(1);
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 < args.Length && args[i].StartsWith("--"))
            {
                arguments[args[i].TrimStart('-')] = args[i + 1];
            }
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--"))
                {
                    throw new ArgumentException($"Argument '{args[i]}' is missing a value.");
                }

                arguments[args[i].TrimStart('-')] = args[i + 1];
                i++;
            }
        }

        if (!arguments.ContainsKey("language"))
        {
            arguments["language"] = "EN";
        }

        // make sure language code is uppercase
        else
        {
            arguments["language"] = arguments["language"].ToUpperInvariant();
        }

        return arguments;
    }

    private static void ValidateArguments(Dictionary<string, string> args)
    {
        var required = new[] { "installDir", "gameType", "version", "output" };
        foreach (var req in required)
        {
            if (!args.ContainsKey(req) || string.IsNullOrWhiteSpace(args[req]))
            {
                throw new ArgumentException($"Missing required argument: --{req}");
            }
        }
    }
}
