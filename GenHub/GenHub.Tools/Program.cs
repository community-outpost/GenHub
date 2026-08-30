using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;

namespace GenHub.Tools;

/// <summary>
/// CSV Generation Utility for creating authoritative CSV files from game installations.
/// </summary>
public static class Program
{
    /// <summary>
    /// Parses command-line arguments into a structured <see cref="CsvGeneratorOptions"/> object.
    /// </summary>
    /// <param name="args">The raw command-line arguments.</param>
    /// <returns>A validated <see cref="CsvGeneratorOptions"/> instance.</returns>
    /// <exception cref="ArgumentException">Thrown when a required argument is missing or invalid.</exception>
    public static CsvGeneratorOptions ParseCommandLineArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var updateIndex = false;

        var knownValueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "installDir", "gameType", "version", "output", "language", "index", "downloadUrl",
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--updateIndex", StringComparison.OrdinalIgnoreCase))
            {
                updateIndex = true;
                continue;
            }

            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var key = arg[2..];
                if (!knownValueOptions.Contains(key))
                {
                    throw new ArgumentException($"Unrecognized command-line argument: {arg}");
                }

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    dict[key] = args[i + 1];
                    i++;
                }
                else
                {
                    throw new ArgumentException($"Option {arg} requires a value.");
                }
            }
            else
            {
                throw new ArgumentException($"Unrecognized command-line argument: {arg}");
            }
        }

        var required = new[] { "installDir", "gameType", "version", "output" };
        foreach (var req in required)
        {
            if (!dict.TryGetValue(req, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Missing required command-line argument: --{req}");
            }
        }

        var language = dict.TryGetValue("language", out var lang) ? lang : CsvConstants.LanguageEn;
        var index = dict.TryGetValue("index", out var idx) ? idx : null;
        var downloadUrl = dict.TryGetValue("downloadUrl", out var dl) ? dl : null;

        return new CsvGeneratorOptions(
            InstallDir: dict["installDir"],
            OutputPath: dict["output"],
            GameType: dict["gameType"],
            Version: dict["version"],
            Language: language,
            IndexFilePath: index,
            DownloadUrl: downloadUrl,
            UpdateIndex: updateIndex || dict.ContainsKey("updateIndex"));
    }

    /// <summary>
    /// Main entry point for the CSV Generation Utility.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>A task representing the asynchronous operation with exit code.</returns>
    private static async Task<int> Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("CsvGenerator");

        try
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
            {
                PrintUsage(logger);
                return 0;
            }

            var options = ParseCommandLineArguments(args);

            logger.LogInformation("Starting CSV Generation Utility");
            logger.LogInformation(
                "Configuration: InstallDir={InstallDir}, GameType={GameType}, Version={Version}, Language={Language}, Output={Output}, UpdateIndex={UpdateIndex}",
                options.InstallDir,
                options.GameType,
                options.Version,
                options.Language,
                options.OutputPath,
                options.UpdateIndex);

            var generator = new CsvGenerator(logger);
            var result = await generator.GenerateCsvFileAsync(options);

            if (!result.Success)
            {
                foreach (var error in result.Errors ?? [])
                {
                    logger.LogError("Error: {Message}", error);
                }

                return 1;
            }

            logger.LogInformation(
                "CSV generation completed successfully in {Elapsed:F2}s. Total entries: {Count}, MD5: {Md5}, SHA256: {Sha256}",
                result.Elapsed.TotalSeconds,
                result.Data.TotalEntriesWritten,
                result.Data.CsvMd5,
                result.Data.CsvSha256);

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CSV Generation Utility failed unexpectedly: {Error}", ex.Message);
            return 1;
        }
    }

    private static void PrintUsage(ILogger logger)
    {
        logger.LogInformation("GenHub CSV Generation Utility");
        logger.LogInformation("================================");
        logger.LogInformation("Usage: GenHub.Tools --installDir <path> --gameType <Generals|ZeroHour> --version <v> --output <file> [options]");
        logger.LogInformation(string.Empty);
        logger.LogInformation("Required arguments:");
        logger.LogInformation("  --installDir <path>      Path to the game installation root directory.");
        logger.LogInformation("  --gameType <type>        Target game type: 'Generals' or 'ZeroHour'.");
        logger.LogInformation("  --version <version>      Game release version (e.g., '1.08', '1.04').");
        logger.LogInformation("  --output <csv-path>      Path to save the generated CSV catalog file.");
        logger.LogInformation(string.Empty);
        logger.LogInformation("Optional options:");
        logger.LogInformation("  --language <code>        Canonical language code for localized files (default: 'EN').");
        logger.LogInformation("                           Supported: EN, DE, FR, ES, IT, KO, PL, PT-BR, ZH-CN, ZH-TW, All.");
        logger.LogInformation("  --updateIndex            Automatically update or create index.json with new checksums.");
        logger.LogInformation("  --index <json-path>      Custom path to index.json (used with --updateIndex).");
        logger.LogInformation("  --downloadUrl <url>      Custom download URL prefix or template.");
        logger.LogInformation("  --help, -h               Show this help information.");
        logger.LogInformation(string.Empty);
        logger.LogInformation("Examples:");
        logger.LogInformation("  GenHub.Tools --installDir \"C:\\Games\\Generals\" --gameType Generals --version 1.08 --output \"docs/GameInstallationFilesRegistry/Generals-1.08.csv\" --language EN --updateIndex");
        logger.LogInformation("  GenHub.Tools --installDir \"C:\\Games\\ZeroHour\" --gameType ZeroHour --version 1.04 --output \"docs/GameInstallationFilesRegistry/ZeroHour-1.04.csv\" --language DE --updateIndex");
    }
}
