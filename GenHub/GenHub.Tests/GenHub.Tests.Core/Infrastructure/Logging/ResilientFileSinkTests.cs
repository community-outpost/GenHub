using System;
using System.IO;
using GenHub.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Logging;

/// <summary>
/// Unit tests for <see cref="ResilientFileSink"/>.
/// </summary>
public sealed class ResilientFileSinkTests : IDisposable
{
    private readonly string _testDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResilientFileSinkTests"/> class.
    /// </summary>
    public ResilientFileSinkTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "GenHub_ResilientFileSinkTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup in tests
            }
        }
    }

    /// <summary>
    /// Verifies that Emit successfully writes log messages to disk.
    /// </summary>
    [Fact]
    public void Emit_WritesLogEventToFile()
    {
        // Arrange
        var logFile = Path.Combine(_testDirectory, "test.log");
        using var sink = new ResilientFileSink(logFile);
        var logEvent = CreateLogEvent("Test message 1");

        // Act
        sink.Emit(logEvent);

        // Assert
        Assert.True(File.Exists(logFile));
        var content = File.ReadAllText(logFile);
        Assert.Contains("Test message 1", content);
    }

    /// <summary>
    /// Verifies that after external file truncation (like clearing logs in GenHub),
    /// the sink seamlessly continues appending new log events.
    /// </summary>
    [Fact]
    public void Emit_WhenFileTruncatedExternally_ContinuesWritingSuccessfully()
    {
        // Arrange
        var logFile = Path.Combine(_testDirectory, "truncate-test.log");
        using var sink = new ResilientFileSink(logFile);
        sink.Emit(CreateLogEvent("Initial log line"));

        Assert.True(File.Exists(logFile));
        var initialContent = File.ReadAllText(logFile);
        Assert.Contains("Initial log line", initialContent);

        // Act: Truncate file in place as SettingsViewModel does
        using (var stream = new FileStream(logFile, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
        {
            stream.SetLength(0);
            stream.Flush();
        }

        Assert.Equal(0, new FileInfo(logFile).Length);

        // Act: Emit another log event
        sink.Emit(CreateLogEvent("Subsequent log line after truncation"));

        // Assert: Subsequent write succeeded and content contains new line without old content
        var updatedContent = File.ReadAllText(logFile);
        Assert.DoesNotContain("Initial log line", updatedContent);
        Assert.Contains("Subsequent log line after truncation", updatedContent);
    }

    /// <summary>
    /// Verifies that if the log file is deleted externally,
    /// the sink recreates the file on the next write without throwing.
    /// </summary>
    [Fact]
    public void Emit_WhenFileDeletedExternally_RecreatesFileAndWrites()
    {
        // Arrange
        var logFile = Path.Combine(_testDirectory, "delete-test.log");
        using var sink = new ResilientFileSink(logFile);
        sink.Emit(CreateLogEvent("First line"));

        Assert.True(File.Exists(logFile));
        File.Delete(logFile);
        Assert.False(File.Exists(logFile));

        // Act
        sink.Emit(CreateLogEvent("Line after deletion"));

        // Assert
        Assert.True(File.Exists(logFile));
        var content = File.ReadAllText(logFile);
        Assert.Contains("Line after deletion", content);
    }

    /// <summary>
    /// Verifies that if the directory does not exist, the sink creates it automatically.
    /// </summary>
    [Fact]
    public void Emit_WhenDirectoryDoesNotExist_CreatesDirectoryAndWrites()
    {
        // Arrange
        var nestedDir = Path.Combine(_testDirectory, "sub", "logs");
        var logFile = Path.Combine(nestedDir, "nested.log");
        using var sink = new ResilientFileSink(logFile);

        // Act
        sink.Emit(CreateLogEvent("Nested log message"));

        // Assert
        Assert.True(Directory.Exists(nestedDir));
        Assert.True(File.Exists(logFile));
        Assert.Contains("Nested log message", File.ReadAllText(logFile));
    }

    /// <summary>
    /// Verifies that Emit does nothing when the sink is disposed.
    /// </summary>
    [Fact]
    public void Emit_WhenDisposed_DoesNotWrite()
    {
        // Arrange
        var logFile = Path.Combine(_testDirectory, "disposed.log");
        var sink = new ResilientFileSink(logFile);
        sink.Dispose();

        // Act
        sink.Emit(CreateLogEvent("Should not be written"));

        // Assert
        Assert.False(File.Exists(logFile));
    }

    /// <summary>
    /// Verifies that null LogEvent does not throw.
    /// </summary>
    [Fact]
    public void Emit_WhenLogEventIsNull_DoesNotThrow()
    {
        // Arrange
        var logFile = Path.Combine(_testDirectory, "null.log");
        using var sink = new ResilientFileSink(logFile);

        // Act & Assert (should not throw)
        sink.Emit(null!);
        Assert.False(File.Exists(logFile));
    }

    private static LogEvent CreateLogEvent(string message)
    {
        var parser = new MessageTemplateParser();
        var template = parser.Parse(message);
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            Array.Empty<LogEventProperty>());
    }
}
