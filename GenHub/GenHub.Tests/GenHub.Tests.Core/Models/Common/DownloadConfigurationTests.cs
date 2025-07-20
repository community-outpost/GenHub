using System;
using System.Collections.Generic;
using GenHub.Core.Models.Common;
using Xunit;

namespace GenHub.Tests.Core.Models.Common;

/// <summary>
/// Unit tests for <see cref="DownloadConfiguration"/>.
/// </summary>
public class DownloadConfigurationTests
{
    /// <summary>
    /// Verifies that the constructor sets default values correctly.
    /// </summary>
    [Fact]
    public void Constructor_SetsDefaultValues()
    {
        // Act
        var config = new DownloadConfiguration();

        // Assert
        Assert.Equal(string.Empty, config.Url);
        Assert.Equal(string.Empty, config.DestinationPath);
        Assert.Null(config.ExpectedHash);
        Assert.True(config.OverwriteExisting);
        Assert.Equal(TimeSpan.FromMinutes(10), config.Timeout);
        Assert.Equal(81920, config.BufferSize);
        Assert.Equal(TimeSpan.FromMilliseconds(100), config.ProgressReportingInterval);
        Assert.NotNull(config.Headers);
        Assert.Empty(config.Headers);
        Assert.Equal("GenHub/1.0", config.UserAgent);
        Assert.True(config.VerifySslCertificate);
        Assert.Equal(3, config.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), config.RetryDelay);
    }

    /// <summary>
    /// Verifies that all properties can be set and retrieved correctly.
    /// </summary>
    [Fact]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var config = new DownloadConfiguration();
        var url = "https://example.com/file.zip";
        var destinationPath = "/path/to/destination/file.zip";
        var expectedHash = "abcdef123456";
        var timeout = TimeSpan.FromMinutes(60);
        var bufferSize = 65536;
        var progressInterval = TimeSpan.FromMilliseconds(500);
        var headers = new Dictionary<string, string> { { "Authorization", "Bearer token" } };
        var userAgent = "CustomAgent/2.0";
        var maxRetryAttempts = 5;
        var retryDelay = TimeSpan.FromSeconds(2);

        // Act
        config.Url = url;
        config.DestinationPath = destinationPath;
        config.ExpectedHash = expectedHash;
        config.OverwriteExisting = false;
        config.Timeout = timeout;
        config.BufferSize = bufferSize;
        config.ProgressReportingInterval = progressInterval;
        config.Headers = headers;
        config.UserAgent = userAgent;
        config.VerifySslCertificate = false;
        config.MaxRetryAttempts = maxRetryAttempts;
        config.RetryDelay = retryDelay;

        // Assert
        Assert.Equal(url, config.Url);
        Assert.Equal(destinationPath, config.DestinationPath);
        Assert.Equal(expectedHash, config.ExpectedHash);
        Assert.False(config.OverwriteExisting);
        Assert.Equal(timeout, config.Timeout);
        Assert.Equal(bufferSize, config.BufferSize);
        Assert.Equal(progressInterval, config.ProgressReportingInterval);
        Assert.Equal(headers, config.Headers);
        Assert.Equal(userAgent, config.UserAgent);
        Assert.False(config.VerifySslCertificate);
        Assert.Equal(maxRetryAttempts, config.MaxRetryAttempts);
        Assert.Equal(retryDelay, config.RetryDelay);
    }

    /// <summary>
    /// Verifies that BufferSize accepts valid values.
    /// </summary>
    /// <param name="bufferSize">The buffer size to test.</param>
    [Theory]
    [InlineData(1024)]
    [InlineData(4096)]
    [InlineData(65536)]
    [InlineData(1048576)]
    public void BufferSize_AcceptsValidValues(int bufferSize)
    {
        // Arrange
        var config = new DownloadConfiguration();

        // Act
        config.BufferSize = bufferSize;

        // Assert
        Assert.Equal(bufferSize, config.BufferSize);
    }

    /// <summary>
    /// Verifies that MaxRetryAttempts accepts valid values.
    /// </summary>
    /// <param name="maxRetryAttempts">The max retry attempts to test.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void MaxRetryAttempts_AcceptsValidValues(int maxRetryAttempts)
    {
        // Arrange
        var config = new DownloadConfiguration();

        // Act
        config.MaxRetryAttempts = maxRetryAttempts;

        // Assert
        Assert.Equal(maxRetryAttempts, config.MaxRetryAttempts);
    }
}
