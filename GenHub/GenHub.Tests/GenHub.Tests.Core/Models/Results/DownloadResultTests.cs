using GenHub.Core.Models.Results;

namespace GenHub.Tests.Core.Models.Results;

/// <summary>
/// Unit tests for <see cref="DownloadResult"/>.
/// </summary>
/// <summary>
/// Contains unit tests for the <see cref="DownloadResult"/> class.
/// </summary>
public class DownloadResultTests
{
    /// <summary>
    /// Verifies that CreateSuccess creates a successful result with valid parameters.
    /// </summary>
    [Fact]
    public void CreateSuccess_WithValidParameters_CreatesSuccessfulResult()
    {
        // Arrange
        var filePath = "/path/to/file.txt";
        var bytesDownloaded = 1024L;
        var elapsed = TimeSpan.FromSeconds(2);
        var hashVerified = true;

        // Act
        var result = DownloadResult.CreateSuccess(filePath, bytesDownloaded, elapsed, hashVerified);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Failed);
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(bytesDownloaded, result.BytesDownloaded);
        Assert.Equal(elapsed, result.Elapsed);
        Assert.True(result.HashVerified);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// Verifies that CreateSuccess creates a successful result without hash verification.
    /// </summary>
    [Fact]
    public void CreateSuccess_WithoutHashVerification_CreatesSuccessfulResult()
    {
        // Arrange
        var filePath = "/path/to/file.txt";
        var bytesDownloaded = 2048L;
        var elapsed = TimeSpan.FromSeconds(1);

        // Act
        var result = DownloadResult.CreateSuccess(filePath, bytesDownloaded, elapsed);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(bytesDownloaded, result.BytesDownloaded);
        Assert.Equal(elapsed, result.Elapsed);
        Assert.False(result.HashVerified);
    }

    /// <summary>
    /// Verifies that CreateFailed creates a failed result with an error message.
    /// </summary>
    [Fact]
    public void CreateFailed_WithErrorMessage_CreatesFailedResult()
    {
        // Arrange
        var error = "Download failed";
        var bytesDownloaded = 512L;
        var elapsed = TimeSpan.FromSeconds(0.5);

        // Act
        var result = DownloadResult.CreateFailed(error, bytesDownloaded, elapsed);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Failed);
        Assert.Null(result.FilePath);
        Assert.Equal(bytesDownloaded, result.BytesDownloaded);
        Assert.Equal(elapsed, result.Elapsed);
        Assert.False(result.HashVerified);
        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal(error, result.FirstError);
    }

    /// <summary>
    /// Verifies that CreateFailed creates a failed result with minimal parameters.
    /// </summary>
    [Fact]
    public void CreateFailed_WithMinimalParameters_CreatesFailedResult()
    {
        // Arrange
        var error = "Network error";

        // Act
        var result = DownloadResult.CreateFailed(error);

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.FilePath);
        Assert.Equal(0L, result.BytesDownloaded);
        Assert.Equal(TimeSpan.Zero, result.Elapsed);
        Assert.False(result.HashVerified);
        Assert.Equal(error, result.FirstError);
    }

    /// <summary>
    /// Verifies that AverageSpeedBytesPerSecond calculates the correct speed.
    /// </summary>
    /// <param name="bytesDownloaded">The number of bytes downloaded.</param>
    /// <param name="seconds">The elapsed time in seconds.</param>
    /// <param name="expectedSpeed">The expected average speed in bytes per second.</param>
    [Theory]
    [InlineData(1024, 1, 1024)]
    [InlineData(2048, 2, 1024)]
    [InlineData(0, 1, 0)]
    [InlineData(1024, 0, 0)]
    public void AverageSpeedBytesPerSecond_CalculatesCorrectly(long bytesDownloaded, double seconds, long expectedSpeed)
    {
        // Arrange
        var elapsed = TimeSpan.FromSeconds(seconds);
        var result = DownloadResult.CreateSuccess("/path/to/file.txt", bytesDownloaded, elapsed);

        // Act
        var actualSpeed = result.AverageSpeedBytesPerSecond;

        // Assert
        Assert.Equal(expectedSpeed, actualSpeed);
    }

    /// <summary>
    /// Verifies that FormattedBytesDownloaded returns the correct formatted string.
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <param name="expected">The expected formatted string.</param>
    [Theory]
    [InlineData(0, "0.0 B")]
    [InlineData(1023, "1023.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    public void FormattedBytesDownloaded_FormatsCorrectly(long bytes, string expected)
    {
        // Arrange
        var result = DownloadResult.CreateSuccess("/path/to/file.txt", bytes, TimeSpan.FromSeconds(1));

        // Act
        var formatted = result.FormattedBytesDownloaded;

        // Assert
        Assert.Equal(expected, formatted);
    }

    /// <summary>
    /// Verifies that FormattedSpeed returns the correct formatted speed string.
    /// </summary>
    /// <param name="bytesDownloaded">The number of bytes downloaded.</param>
    /// <param name="seconds">The elapsed time in seconds.</param>
    /// <param name="expected">The expected formatted speed string.</param>
    [Theory]
    [InlineData(1024, 1, "1.0 KB/s")]
    [InlineData(1048576, 2, "512.0 KB/s")]
    [InlineData(0, 1, "0.0 B/s")]
    public void FormattedSpeed_FormatsCorrectly(long bytesDownloaded, double seconds, string expected)
    {
        // Arrange
        var elapsed = TimeSpan.FromSeconds(seconds);
        var result = DownloadResult.CreateSuccess("/path/to/file.txt", bytesDownloaded, elapsed);

        // Act
        var formatted = result.FormattedSpeed;

        // Assert
        Assert.Equal(expected, formatted);
    }
}