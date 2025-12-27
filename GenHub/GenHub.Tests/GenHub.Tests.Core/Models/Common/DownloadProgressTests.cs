using GenHub.Core.Helpers;
using GenHub.Core.Models.Common;

namespace GenHub.Tests.Core.Models.Common;

/// <summary>
/// Unit tests for <see cref="DownloadProgress"/>.
/// </summary>
public class DownloadProgressTests
{
    /// <summary>
    /// Verifies that the constructor sets all properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var bytesReceived = 1024L;
        var totalBytes = 2048L;
        var fileName = "test.zip";
        var urlString = "https://example.com/test.zip";
        var url = new Uri(urlString);
        var bytesPerSecond = 512L;
        var elapsedTime = TimeSpan.FromSeconds(2);

        // Act
        var progress = new DownloadProgress(bytesReceived, totalBytes, fileName, url, bytesPerSecond, elapsedTime);

        // Assert
        Assert.Equal(bytesReceived, progress.BytesReceived);
        Assert.Equal(totalBytes, progress.TotalBytes);
        Assert.Equal(fileName, progress.FileName);
        Assert.Equal(url, progress.Url);
        Assert.Equal(bytesPerSecond, progress.BytesPerSecond);
        Assert.Equal(elapsedTime, progress.ElapsedTime);
    }

    /// <summary>
    /// Verifies that the Percentage property calculates the correct value.
    /// </summary>
    /// <param name="bytesReceived">The number of bytes received.</param>
    /// <param name="totalBytes">The total number of bytes.</param>
    /// <param name="expectedPercentage">The expected percentage value.</param>
    [Theory]
    [InlineData(0, 1024, 0.0)]
    [InlineData(512, 1024, 50.0)]
    [InlineData(1024, 1024, 100.0)]
    [InlineData(1024, 0, 0.0)]
    public void Percentage_CalculatesCorrectly(long bytesReceived, long totalBytes, double expectedPercentage)
    {
        // Arrange
        var progress = new DownloadProgress(bytesReceived, totalBytes, "test.zip", new Uri("https://example.com/test.zip"), 0, TimeSpan.Zero);

        // Act
        var percentage = progress.Percentage;

        // Assert
        Assert.Equal(expectedPercentage, percentage);
    }

    /// <summary>
    /// Verifies that the EstimatedTimeRemaining property calculates the correct value.
    /// </summary>
    /// <param name="bytesReceived">The number of bytes received.</param>
    /// <param name="totalBytes">The total number of bytes.</param>
    /// <param name="bytesPerSecond">The download speed in bytes per second.</param>
    /// <param name="expectedSeconds">The expected time remaining in seconds, or null if not applicable.</param>
    [Theory]
    [InlineData(1024, 2048, 512, 2.0)] // 1024 bytes remaining at 512 bytes/sec = 2 seconds
    [InlineData(512, 1024, 256, 2.0)] // 512 bytes remaining at 256 bytes/sec = 2 seconds
    [InlineData(1024, 1024, 512, null)] // Complete, no time remaining
    [InlineData(512, 1024, 0, null)] // No speed data, can't calculate
    public void EstimatedTimeRemaining_CalculatesCorrectly(long bytesReceived, long totalBytes, long bytesPerSecond, double? expectedSeconds)
    {
        // Arrange
        var progress = new DownloadProgress(bytesReceived, totalBytes, "test.zip", new Uri("https://example.com/test.zip"), bytesPerSecond, TimeSpan.Zero);

        // Act
        var estimatedTime = progress.EstimatedTimeRemaining;

        // Assert
        if (expectedSeconds.HasValue)
        {
            Assert.NotNull(estimatedTime);
            Assert.Equal(expectedSeconds.Value, estimatedTime.Value.TotalSeconds, 1);
        }
        else
        {
            Assert.Null(estimatedTime);
        }
    }

    /// <summary>
    /// Verifies that the FormattedSpeed property returns the correct formatted string.
    /// </summary>
    /// <param name="bytesPerSecond">The download speed in bytes per second.</param>
    /// <param name="expected">The expected formatted string.</param>
    [Theory]
    [InlineData(0, "0.0 B/s")]
    [InlineData(1023, "1023.0 B/s")]
    [InlineData(1024, "1.0 KB/s")]
    [InlineData(1536, "1.5 KB/s")]
    [InlineData(1048576, "1.0 MB/s")]
    public void FormattedSpeed_FormatsCorrectly(long bytesPerSecond, string expected)
    {
        // Arrange
        var progress = new DownloadProgress(0, 0, "test.zip", new Uri("https://example.com/test.zip"), bytesPerSecond, TimeSpan.Zero);

        // Act
        var formatted = progress.FormattedSpeed;

        // Assert
        Assert.Equal(expected, formatted);
    }

    /// <summary>
    /// Verifies that the FormattedProgress property returns the correct formatted string.
    /// </summary>
    /// <param name="bytesReceived">The number of bytes received.</param>
    /// <param name="totalBytes">The total number of bytes.</param>
    /// <param name="expected">The expected formatted string.</param>
    [Theory]
    [InlineData(512, 1024, "512.0 B / 1.0 KB")]
    [InlineData(1024, 2048, "1.0 KB / 2.0 KB")]
    [InlineData(0, 1048576, "0.0 B / 1.0 MB")]
    public void FormattedProgress_FormatsCorrectly(long bytesReceived, long totalBytes, string expected)
    {
        // Arrange
        var progress = new DownloadProgress(bytesReceived, totalBytes, "test.zip", new Uri("https://example.com/test.zip"), 0, TimeSpan.Zero);

        // Act
        var formatted = progress.FormattedProgress;

        // Assert
        Assert.Equal(expected, formatted);
    }

    /// <summary>
    /// Verifies that the FileSizeFormatter.Format method returns the correct formatted string.
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <param name="expected">The expected formatted string.</param>
    [Theory]
    [InlineData(1023, "1023.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1099511627776, "1.0 TB")]
    public void FormatBytes_FormatsCorrectly(long bytes, string expected)
    {
        // Act
        var formatted = FileSizeFormatter.Format(bytes);

        // Assert
        Assert.Equal(expected, formatted);
    }
}
