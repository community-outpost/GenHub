using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.GameInstallations;
using Xunit;

namespace GenHub.Tests.Features.GameInstallations;

/// <summary>
/// Unit tests for LanguageDetector.
/// </summary>
public class LanguageDetectorTests
{
    private readonly LanguageDetector _detector = new();

    /// <summary>
    /// Tests that invalid or non-existent paths return English fallback.
    /// </summary>
    /// <param name="path">The invalid path to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("non_existent_directory_xyz_123")]
    public async Task DetectAsync_WithInvalidPath_ReturnsEnglishFallbackAsync(string? path)
    {
        var result = await _detector.DetectAsync(path!);
        Assert.Equal(CsvConstants.LanguageEn, result);
    }

    /// <summary>
    /// Tests that a cancelled token throws OperationCanceledException.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DetectAsync_WithCancelledToken_ThrowsOperationCanceledExceptionAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _detector.DetectAsync("some_path", cts.Token));
    }

    /// <summary>
    /// Tests that language directory presence detects the corresponding language code.
    /// </summary>
    /// <param name="relativeDir">The relative directory name.</param>
    /// <param name="expectedLanguage">The expected detected language code.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(LanguageDirectoryNames.DataEnglish, CsvConstants.LanguageEn)]
    [InlineData(LanguageDirectoryNames.DataEnglishUppercase, CsvConstants.LanguageEn)]
    [InlineData(LanguageDirectoryNames.DataGerman, CsvConstants.LanguageDe)]
    [InlineData(LanguageDirectoryNames.DataDeutsch, CsvConstants.LanguageDe)]
    [InlineData(LanguageDirectoryNames.DataFrench, CsvConstants.LanguageFr)]
    [InlineData(LanguageDirectoryNames.DataSpanish, CsvConstants.LanguageEs)]
    [InlineData(LanguageDirectoryNames.DataItalian, CsvConstants.LanguageIt)]
    [InlineData(LanguageDirectoryNames.DataKorean, CsvConstants.LanguageKo)]
    [InlineData(LanguageDirectoryNames.DataPolish, CsvConstants.LanguagePl)]
    [InlineData(LanguageDirectoryNames.DataPortuguese, CsvConstants.LanguagePtBr)]
    [InlineData(LanguageDirectoryNames.DataChinese, CsvConstants.LanguageZhCn)]
    [InlineData(LanguageDirectoryNames.DataChineseTraditional, CsvConstants.LanguageZhTw)]
    public async Task DetectAsync_WithLanguageDirectory_DetectsCorrectLanguageAsync(string relativeDir, string expectedLanguage)
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var segments = relativeDir.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            var dirPath = Path.Combine(segments.Prepend(tempDir.FullName).ToArray());
            Directory.CreateDirectory(dirPath);

            var result = await _detector.DetectAsync(tempDir.FullName);
            Assert.Equal(expectedLanguage, result);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that language-specific BIG files detect the corresponding language code.
    /// </summary>
    /// <param name="fileName">The BIG file name.</param>
    /// <param name="expectedLanguage">The expected detected language code.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(LanguageFilePatterns.GermanBig, CsvConstants.LanguageDe)]
    [InlineData(LanguageFilePatterns.AudioGermanBig, CsvConstants.LanguageDe)]
    [InlineData(LanguageFilePatterns.FrenchBig, CsvConstants.LanguageFr)]
    [InlineData(LanguageFilePatterns.AudioFrenchBig, CsvConstants.LanguageFr)]
    [InlineData(LanguageFilePatterns.SpanishBig, CsvConstants.LanguageEs)]
    [InlineData(LanguageFilePatterns.AudioSpanishBig, CsvConstants.LanguageEs)]
    [InlineData(LanguageFilePatterns.ItalianBig, CsvConstants.LanguageIt)]
    [InlineData(LanguageFilePatterns.AudioItalianBig, CsvConstants.LanguageIt)]
    [InlineData(LanguageFilePatterns.KoreanBig, CsvConstants.LanguageKo)]
    [InlineData(LanguageFilePatterns.AudioKoreanBig, CsvConstants.LanguageKo)]
    [InlineData(LanguageFilePatterns.PolishBig, CsvConstants.LanguagePl)]
    [InlineData(LanguageFilePatterns.AudioPolishBig, CsvConstants.LanguagePl)]
    [InlineData(LanguageFilePatterns.PortugueseBrazilBig, CsvConstants.LanguagePtBr)]
    [InlineData(LanguageFilePatterns.AudioPortugueseBrazilBig, CsvConstants.LanguagePtBr)]
    [InlineData(LanguageFilePatterns.ChineseBig, CsvConstants.LanguageZhCn)]
    [InlineData(LanguageFilePatterns.AudioChineseBig, CsvConstants.LanguageZhCn)]
    [InlineData(LanguageFilePatterns.ChineseTraditionalBig, CsvConstants.LanguageZhTw)]
    [InlineData(LanguageFilePatterns.AudioChineseTraditionalBig, CsvConstants.LanguageZhTw)]
    public async Task DetectAsync_WithLanguageBigFile_DetectsCorrectLanguageAsync(string fileName, string expectedLanguage)
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var filePath = Path.Combine(tempDir.FullName, fileName);
            await File.WriteAllTextAsync(filePath, "dummy big content");

            var result = await _detector.DetectAsync(tempDir.FullName);
            Assert.Equal(expectedLanguage, result);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that Zero Hour specific language BIG files detect the corresponding language code.
    /// </summary>
    /// <param name="fileName">The Zero Hour BIG file name.</param>
    /// <param name="expectedLanguage">The expected detected language code.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(LanguageFilePatterns.GermanZHBig, CsvConstants.LanguageDe)]
    [InlineData(LanguageFilePatterns.FrenchZHBig, CsvConstants.LanguageFr)]
    [InlineData(LanguageFilePatterns.SpanishZHBig, CsvConstants.LanguageEs)]
    [InlineData(LanguageFilePatterns.ItalianZHBig, CsvConstants.LanguageIt)]
    [InlineData(LanguageFilePatterns.KoreanZHBig, CsvConstants.LanguageKo)]
    [InlineData(LanguageFilePatterns.PolishZHBig, CsvConstants.LanguagePl)]
    [InlineData(LanguageFilePatterns.PortugueseZHBig, CsvConstants.LanguagePtBr)]
    [InlineData(LanguageFilePatterns.ChineseZHBig, CsvConstants.LanguageZhCn)]
    [InlineData(LanguageFilePatterns.EnglishZHBig, CsvConstants.LanguageEn)]
    public async Task DetectAsync_WithZeroHourPatterns_DetectsCorrectLanguageAsync(string fileName, string expectedLanguage)
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var filePath = Path.Combine(tempDir.FullName, fileName);
            await File.WriteAllTextAsync(filePath, "dummy zh big content");

            var result = await _detector.DetectAsync(tempDir.FullName);
            Assert.Equal(expectedLanguage, result);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    /// <summary>
    /// Tests that directory containing only unknown files falls back to English.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DetectAsync_WithUnknownFiles_FallsBackToEnglishAsync()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir.FullName, "random_mod_file.big"), "data");

            var result = await _detector.DetectAsync(tempDir.FullName);
            Assert.Equal(CsvConstants.LanguageEn, result);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }
}
