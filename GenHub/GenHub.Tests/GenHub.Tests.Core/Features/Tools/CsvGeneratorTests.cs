using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.ContentResolvers;
using GenHub.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools;

/// <summary>
/// Unit tests for <see cref="CsvGenerator"/> and CLI tooling.
/// </summary>
public sealed class CsvGeneratorTests : IDisposable
{
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvGeneratorTests"/> class.
    /// </summary>
    public CsvGeneratorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_CsvGeneratorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Cleans up temporary test files and directories.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    /// <summary>
    /// Verifies that scanning a game installation produces a valid CSV catalog matching all requirements.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GenerateCsvFileAsync_WithValidDirectory_GeneratesCsvWithExpectedEntriesAsync()
    {
        var installDir = Path.Combine(_tempDirectory, "install");
        Directory.CreateDirectory(installDir);

        CreateTestFile(installDir, "generals.exe", "dummy executable bytes");
        CreateTestFile(installDir, "game.dat", "dummy dat bytes");
        CreateTestFile(installDir, "Data/INI/GameData.ini", "dummy ini content");
        CreateTestFile(installDir, "Data/INI/English.ini", "dummy language ini");
        CreateTestFile(installDir, "Data/Lang/English/game.str", "dummy string table");
        CreateTestFile(installDir, "AudioEnglish.big", "dummy audio big");
        CreateTestFile(installDir, "Data/Maps/Custom/test.map", "dummy map");
        CreateTestFile(installDir, "Textures.w3d", "dummy graphics");
        CreateTestFile(installDir, "EmptyFile.txt", string.Empty); // Should be skipped

        var outputFile = Path.Combine(_tempDirectory, "output", "Generals-1.08.csv");

        var generator = new CsvGenerator(NullLogger.Instance);
        var options = new CsvGeneratorOptions(
            InstallDir: installDir,
            OutputPath: outputFile,
            GameType: "Generals",
            Version: "1.08",
            Language: "EN");

        var result = await generator.GenerateCsvFileAsync(options);

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.TotalEntriesWritten.Should().Be(8);
        result.Data.TotalFilesScanned.Should().Be(9);
        File.Exists(outputFile).Should().BeTrue();

        using var reader = new StreamReader(outputFile);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true });
        var records = csv.GetRecords<CsvCatalogEntry>().ToList();

        records.Should().HaveCount(8);

        // Core required files
        var exeEntry = records.FirstOrDefault(r => r.RelativePath == "generals.exe");
        exeEntry.Should().NotBeNull();
        exeEntry!.IsRequired.Should().BeTrue();
        exeEntry.Language.Should().Be(CsvConstants.AllLanguagesFilter);
        exeEntry.GameType.Should().Be("Generals");

        var datEntry = records.FirstOrDefault(r => r.RelativePath == "game.dat");
        datEntry.Should().NotBeNull();
        datEntry!.IsRequired.Should().BeTrue();
        datEntry.Language.Should().Be(CsvConstants.AllLanguagesFilter);

        // Config category
        var iniEntry = records.FirstOrDefault(r => r.RelativePath == "Data/INI/GameData.ini");
        iniEntry.Should().NotBeNull();
        iniEntry!.Metadata.Should().Contain("\"category\":\"config\"");
        iniEntry.Language.Should().Be(CsvConstants.AllLanguagesFilter);

        // Language specific
        var langIniEntry = records.FirstOrDefault(r => r.RelativePath == "Data/INI/English.ini");
        langIniEntry.Should().NotBeNull();
        langIniEntry!.IsRequired.Should().BeTrue();
        langIniEntry.Language.Should().Be("EN");

        var strEntry = records.FirstOrDefault(r => r.RelativePath == "Data/Lang/English/game.str");
        strEntry.Should().NotBeNull();
        strEntry!.IsRequired.Should().BeTrue();
        strEntry.Language.Should().Be("EN");
        strEntry.Metadata.Should().Contain("\"category\":\"language\"");

        var audioBigEntry = records.FirstOrDefault(r => r.RelativePath == "AudioEnglish.big");
        audioBigEntry.Should().NotBeNull();
        audioBigEntry!.Language.Should().Be("EN");

        // Maps category
        var mapEntry = records.FirstOrDefault(r => r.RelativePath == "Data/Maps/Custom/test.map");
        mapEntry.Should().NotBeNull();
        mapEntry!.Metadata.Should().Contain("\"category\":\"maps\"");

        // Graphics category
        var gfxEntry = records.FirstOrDefault(r => r.RelativePath == "Textures.w3d");
        gfxEntry.Should().NotBeNull();
        gfxEntry!.Metadata.Should().Contain("\"category\":\"graphics\"");
    }

    /// <summary>
    /// Verifies that running with UpdateIndex updates the index.json manifest with accurate metadata.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GenerateCsvFileAsync_WithUpdateIndex_UpdatesIndexJsonCorrectlyAsync()
    {
        var installDir = Path.Combine(_tempDirectory, "install_zh");
        Directory.CreateDirectory(installDir);

        CreateTestFile(installDir, "ZeroHour.exe", "dummy zh executable");
        CreateTestFile(installDir, "AudioZH.big", "dummy audio zh");

        var outputFile = Path.Combine(_tempDirectory, "registry", "ZeroHour-1.04.csv");
        var indexPath = Path.Combine(_tempDirectory, "registry", "index.json");

        var generator = new CsvGenerator(NullLogger.Instance);
        var options = new CsvGeneratorOptions(
            InstallDir: installDir,
            OutputPath: outputFile,
            GameType: "ZeroHour",
            Version: "1.04",
            Language: "EN",
            IndexFilePath: indexPath,
            UpdateIndex: true);

        var result = await generator.GenerateCsvFileAsync(options);

        result.Success.Should().BeTrue();
        result.Data!.IndexUpdated.Should().BeTrue();
        File.Exists(indexPath).Should().BeTrue();

        var indexJson = await File.ReadAllTextAsync(indexPath);
        var index = JsonSerializer.Deserialize<CsvCatalogRegistryIndex>(indexJson);

        index.Should().NotBeNull();
        index!.Version.Should().Be("1.0.0");
        index.Entries.Should().ContainSingle();

        var entry = index.Entries[0];
        entry.Id.Should().Be("zerohour-1.04");
        entry.GameType.Should().Be("ZeroHour");
        entry.Version.Should().Be("1.04");
        entry.FileCount.Should().Be(2);
        entry.Checksum.Should().NotBeNull();
        entry.Checksum!.Md5.Should().Be(result.Data.CsvMd5);
        entry.Checksum.Sha256.Should().Be(result.Data.CsvSha256);
        entry.TotalSizeBytes.Should().Be(result.Data.TotalSizeBytes);
    }

    /// <summary>
    /// Verifies that when the installation directory does not exist, an error result is returned.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GenerateCsvFileAsync_WhenDirectoryDoesNotExist_ReturnsFailureAsync()
    {
        var generator = new CsvGenerator(NullLogger.Instance);
        var options = new CsvGeneratorOptions(
            InstallDir: Path.Combine(_tempDirectory, "nonexistent"),
            OutputPath: Path.Combine(_tempDirectory, "out.csv"),
            GameType: "Generals",
            Version: "1.08");

        var result = await generator.GenerateCsvFileAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Installation directory not found"));
    }

    /// <summary>
    /// Verifies that invalid game type fails validation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GenerateCsvFileAsync_WhenInvalidGameType_ReturnsFailureAsync()
    {
        var installDir = Path.Combine(_tempDirectory, "install_invalid");
        Directory.CreateDirectory(installDir);

        var generator = new CsvGenerator(NullLogger.Instance);
        var options = new CsvGeneratorOptions(
            InstallDir: installDir,
            OutputPath: Path.Combine(_tempDirectory, "out.csv"),
            GameType: "TiberianSun",
            Version: "1.08");

        var result = await generator.GenerateCsvFileAsync(options);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid game type"));
    }

    /// <summary>
    /// Verifies that the CSV output generated by CsvGenerator is completely resolvable by CsvResolver.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GenerateCsvFileAsync_GeneratedCsv_IsResolvableByCsvResolverAsync()
    {
        var installDir = Path.Combine(_tempDirectory, "install_res");
        Directory.CreateDirectory(installDir);

        CreateTestFile(installDir, "generals.exe", "test content 1");
        CreateTestFile(installDir, "Data/Lang/English/game.str", "test strings");

        var outputFile = Path.Combine(_tempDirectory, "Generals-1.08.csv");

        var generator = new CsvGenerator(NullLogger.Instance);
        var options = new CsvGeneratorOptions(
            InstallDir: installDir,
            OutputPath: outputFile,
            GameType: "Generals",
            Version: "1.08",
            Language: "EN");

        var genResult = await generator.GenerateCsvFileAsync(options);
        genResult.Success.Should().BeTrue();

        var resolver = new CsvResolver(Mock.Of<IHttpClientFactory>(), NullLogger<CsvResolver>.Instance);
        var searchResult = new ContentSearchResult
        {
            Id = "csv-generals-1.08-en",
            Name = "Generals 1.08 EN",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.Generals,
            SourceUrl = outputFile,
            ResolverMetadata =
            {
                [CsvConstants.CsvUrlMetadataKey] = outputFile,
                [CsvConstants.GameTypeMetadataKey] = "Generals",
                [CsvConstants.VersionMetadataKey] = "1.08",
                [CsvConstants.LanguageMetadataKey] = "EN",
            },
        };

        var resolveResult = await resolver.ResolveAsync(searchResult, CancellationToken.None);

        resolveResult.Success.Should().BeTrue();
        resolveResult.Data.Should().NotBeNull();
        resolveResult.Data!.TargetGame.Should().Be(GameType.Generals);
        resolveResult.Data.Files.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies command line argument parsing for all supported flags and parameters.
    /// </summary>
    [Fact]
    public void ParseCommandLineArguments_WithValidArgs_ParsesAllOptions()
    {
        var args = new[]
        {
            "--installDir", @"C:\Games\Generals",
            "--gameType", "Generals",
            "--version", "1.08",
            "--output", @"C:\Registries\Generals-1.08.csv",
            "--language", "de",
            "--updateIndex",
            "--index", @"C:\Registries\index.json",
            "--downloadUrl", "https://example.com/custom.csv",
        };

        var options = Program.ParseCommandLineArguments(args);

        options.InstallDir.Should().Be(@"C:\Games\Generals");
        options.GameType.Should().Be("Generals");
        options.Version.Should().Be("1.08");
        options.OutputPath.Should().Be(@"C:\Registries\Generals-1.08.csv");
        options.Language.Should().Be("de");
        options.UpdateIndex.Should().BeTrue();
        options.IndexFilePath.Should().Be(@"C:\Registries\index.json");
        options.DownloadUrl.Should().Be("https://example.com/custom.csv");
    }

    /// <summary>
    /// Verifies that missing required arguments throws ArgumentException.
    /// </summary>
    [Fact]
    public void ParseCommandLineArguments_WithMissingRequiredArg_ThrowsArgumentException()
    {
        var args = new[]
        {
            "--installDir", @"C:\Games\Generals",
            "--gameType", "Generals",

            // missing version and output
        };

        var act = () => Program.ParseCommandLineArguments(args);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Missing required command-line argument*");
    }

    /// <summary>
    /// Verifies language normalization across all supported locale identifiers.
    /// </summary>
    /// <param name="input">The raw input language string.</param>
    /// <param name="expected">The expected normalized language code.</param>
    [Theory]
    [InlineData("en", "EN")]
    [InlineData("de", "DE")]
    [InlineData("german", "DE")]
    [InlineData("deutsch", "DE")]
    [InlineData("fr", "FR")]
    [InlineData("french", "FR")]
    [InlineData("es", "ES")]
    [InlineData("spanish", "ES")]
    [InlineData("it", "IT")]
    [InlineData("italian", "IT")]
    [InlineData("ko", "KO")]
    [InlineData("korean", "KO")]
    [InlineData("pl", "PL")]
    [InlineData("polish", "PL")]
    [InlineData("pt-br", "PT-BR")]
    [InlineData("pt_br", "PT-BR")]
    [InlineData("zh-cn", "ZH-CN")]
    [InlineData("zh-tw", "ZH-TW")]
    [InlineData("all", "All")]
    [InlineData("", "EN")]
    [InlineData(null, "EN")]
    public void NormalizeLanguage_MapsAllLocalesCorrectly(string? input, string expected)
    {
        var result = CsvGenerator.NormalizeLanguage(input);
        result.Should().Be(expected);
    }

    /// <summary>
    /// Verifies game type normalization across canonical and alternative identifiers.
    /// </summary>
    /// <param name="input">The raw input game type string.</param>
    /// <param name="expected">The expected normalized game type string.</param>
    [Theory]
    [InlineData("Generals", "Generals")]
    [InlineData("generals", "Generals")]
    [InlineData("ZeroHour", "ZeroHour")]
    [InlineData("zerohour", "ZeroHour")]
    [InlineData("ZH", "ZeroHour")]
    [InlineData("Zero Hour", "ZeroHour")]
    [InlineData("Invalid", "")]
    public void NormalizeGameType_MapsValidTypes(string input, string expected)
    {
        var result = CsvGenerator.NormalizeGameType(input);
        result.Should().Be(expected);
    }

    private static void CreateTestFile(string baseDir, string relativePath, string content)
    {
        var fullPath = Path.Combine(baseDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, content);
    }
}
