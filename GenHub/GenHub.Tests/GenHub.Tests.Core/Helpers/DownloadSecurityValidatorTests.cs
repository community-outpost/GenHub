namespace GenHub.Tests.Core.Helpers;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Results;
using Xunit;

/// <summary>
/// Unit tests for <see cref="DownloadSecurityValidator"/>.
/// </summary>
public class DownloadSecurityValidatorTests
{
    /// <summary>
    /// Verifies that ValidateFileAsync succeeds when SHA-256 matches allowed hashes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test.</returns>
    [Fact]
    public async Task ValidateFileAsync_WhenSha256Matches_ReturnsSuccessAsync()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = Encoding.UTF8.GetBytes("Test Content for Sha256");
            await File.WriteAllBytesAsync(tempFile, content);

            using var sha256 = SHA256.Create();
            var expectedHash = Convert.ToHexString(sha256.ComputeHash(content)).ToLowerInvariant();

            var result = await DownloadSecurityValidator.ValidateFileAsync(
                tempFile,
                allowedSha256Hashes: [expectedHash]);

            Assert.True(result.Success);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that ValidateFileAsync fails when SHA-256 does not match allowed hashes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test.</returns>
    [Fact]
    public async Task ValidateFileAsync_WhenSha256Mismatches_ReturnsFailureAsync()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = Encoding.UTF8.GetBytes("Test Content for Sha256 Mismatch");
            await File.WriteAllBytesAsync(tempFile, content);

            var wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";

            var result = await DownloadSecurityValidator.ValidateFileAsync(
                tempFile,
                allowedSha256Hashes: [wrongHash]);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("SHA-256 hash mismatch"));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that a publisher criterion cannot override a pinned SHA-256 mismatch in either validation path.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test.</returns>
    /// <param name="lockFile">Whether to validate through the locked-file path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateCombinedCriteriaAsync_WhenSha256Mismatches_ReturnsHashFailureAsync(bool lockFile)
    {
        var (tempFile, _) = await CreateTempFileWithHashAsync("Hash mismatch must fail before publisher validation.");
        try
        {
            var result = await ValidateWithCombinedCriteriaAsync(
                tempFile,
                "0000000000000000000000000000000000000000000000000000000000000000",
                lockFile);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("SHA-256 hash mismatch"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that a matching SHA-256 hash cannot override a failed publisher check in either validation path.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test.</returns>
    /// <param name="lockFile">Whether to validate through the locked-file path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidateCombinedCriteriaAsync_WhenSha256MatchesAndPublisherFails_ReturnsPublisherFailureAsync(bool lockFile)
    {
        var (tempFile, expectedHash) = await CreateTempFileWithHashAsync("A matching hash must not bypass publisher validation.");
        try
        {
            var result = await ValidateWithCombinedCriteriaAsync(
                tempFile,
                expectedHash,
                lockFile);

            Assert.False(result.Success);
            Assert.DoesNotContain(result.Errors, e => e.Contains("SHA-256 hash mismatch"));
            Assert.Contains(
                result.Errors,
                e => e.Contains("Authenticode", StringComparison.OrdinalIgnoreCase) ||
                     e.Contains("publisher", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that ValidateAndLockFileAsync succeeds, sets read-only, and locks the file when SHA-256 matches.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test.</returns>
    [Fact]
    public async Task ValidateAndLockFileAsync_WhenSha256Matches_ReturnsLockedStreamAsync()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = Encoding.UTF8.GetBytes("Test Content for Lock Validation");
            await File.WriteAllBytesAsync(tempFile, content);

            using var sha256 = SHA256.Create();
            var expectedHash = Convert.ToHexString(sha256.ComputeHash(content)).ToLowerInvariant();

            var result = await DownloadSecurityValidator.ValidateAndLockFileAsync(
                tempFile,
                allowedSha256Hashes: [expectedHash]);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);

            await using var stream = result.Data;
            Assert.True(stream.CanRead);
            Assert.False(stream.CanWrite);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.SetAttributes(tempFile, FileAttributes.Normal);
                    File.Delete(tempFile);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Verifies that ValidateAndLockFileAsync returns failure when hash mismatches.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test.</returns>
    [Fact]
    public async Task ValidateAndLockFileAsync_WhenSha256Mismatches_ReturnsFailureAsync()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var content = Encoding.UTF8.GetBytes("Mismatch Content for Lock Validation");
            await File.WriteAllBytesAsync(tempFile, content);

            var wrongHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

            var result = await DownloadSecurityValidator.ValidateAndLockFileAsync(
                tempFile,
                allowedSha256Hashes: [wrongHash]);

            Assert.False(result.Success);
            Assert.Null(result.Data);
            Assert.NotEmpty(result.Errors);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.SetAttributes(tempFile, FileAttributes.Normal);
                    File.Delete(tempFile);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Verifies that ComputeSha256Async from stream returns the correct hexadecimal hash.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test.</returns>
    [Fact]
    public async Task ComputeSha256Async_FromStream_ReturnsExpectedHashAsync()
    {
        var content = Encoding.UTF8.GetBytes("Stream Hash Content");
        using var ms = new MemoryStream(content);

        using var sha256 = SHA256.Create();
        var expectedHash = Convert.ToHexString(sha256.ComputeHash(content)).ToLowerInvariant();

        var computedHash = await DownloadSecurityValidator.ComputeSha256Async(ms);

        Assert.Equal(expectedHash, computedHash);
    }

    private static async Task<(string Path, string Sha256)> CreateTempFileWithHashAsync(string text)
    {
        var path = Path.GetTempFileName();
        var content = Encoding.UTF8.GetBytes(text);
        await File.WriteAllBytesAsync(path, content);
        return (path, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
    }

    private static async Task<OperationResult<bool>> ValidateWithCombinedCriteriaAsync(
        string filePath,
        string expectedHash,
        bool lockFile)
    {
        if (!lockFile)
        {
            return await DownloadSecurityValidator.ValidateFileAsync(
                filePath,
                allowedSha256Hashes: [expectedHash],
                expectedAuthenticodePublisher: "Publisher That Does Not Match");
        }

        var result = await DownloadSecurityValidator.ValidateAndLockFileAsync(
            filePath,
            allowedSha256Hashes: [expectedHash],
            expectedAuthenticodePublisher: "Publisher That Does Not Match");

        if (result.Data is not null)
        {
            await result.Data.DisposeAsync();
        }

        return result.Success
            ? OperationResult<bool>.CreateSuccess(true)
            : OperationResult<bool>.CreateFailure(result.Errors);
    }
}
