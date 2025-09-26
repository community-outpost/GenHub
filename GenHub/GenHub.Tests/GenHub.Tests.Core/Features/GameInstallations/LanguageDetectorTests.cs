using GenHub.Core.Features.GameInstallations;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.GameInstallations;

/// <summary>
/// Unit tests for <see cref="LanguageDetector"/>.
/// </summary>
public class LanguageDetectorTests
{
    /// <summary>
    /// Verifies that DetectAsync returns "EN" for non-existent directory.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAsync_NonExistentDirectory_ReturnsEnglish()
    {
        // Arrange
        var detector = new LanguageDetector();

        // Act
        var result = await detector.DetectAsync("NonExistentPath");

        // Assert
        Assert.Equal("EN", result);
    }

    /// <summary>
    /// Verifies that DetectAsync detects English from directory pattern.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAsync_EnglishDirectory_ReturnsEN()
    {
        // Arrange
        var detector = new LanguageDetector();
        var tempDir = Path.Combine(Path.GetTempPath(), "TestEnglish");
        Directory.CreateDirectory(Path.Combine(tempDir, "Data", "english"));

        try
        {
            // Act
            var result = await detector.DetectAsync(tempDir);

            // Assert
            Assert.Equal("EN", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that DetectAsync detects German from directory pattern.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAsync_GermanDirectory_ReturnsDE()
    {
        // Arrange
        var detector = new LanguageDetector();
        var tempDir = Path.Combine(Path.GetTempPath(), "TestGerman");
        Directory.CreateDirectory(Path.Combine(tempDir, "Data", "german"));

        try
        {
            // Act
            var result = await detector.DetectAsync(tempDir);

            // Assert
            Assert.Equal("DE", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that DetectAsync detects English from file pattern.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAsync_EnglishFile_ReturnsEN()
    {
        // Arrange
        var detector = new LanguageDetector();
        var tempDir = Path.Combine(Path.GetTempPath(), "TestEnglishFile");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "English.big"), "test");

        try
        {
            // Act
            var result = await detector.DetectAsync(tempDir);

            // Assert
            Assert.Equal("EN", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that DetectAsync detects Zero Hour English from file pattern.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAsync_ZeroHourEnglishFile_ReturnsEN()
    {
        // Arrange
        var detector = new LanguageDetector();
        var tempDir = Path.Combine(Path.GetTempPath(), "TestZHEnglish");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "EnglishZH.big"), "test");

        try
        {
            // Act
            var result = await detector.DetectAsync(tempDir);

            // Assert
            Assert.Equal("EN", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Verifies that DetectAsync returns "EN" as fallback when no patterns match.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DetectAsync_NoPatternsMatch_ReturnsEnglishFallback()
    {
        // Arrange
        var detector = new LanguageDetector();
        var tempDir = Path.Combine(Path.GetTempPath(), "TestEmpty");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Act
            var result = await detector.DetectAsync(tempDir);

            // Assert
            Assert.Equal("EN", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
