namespace GenHub.Tests.Core.Helpers;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using GenHub.Core.Helpers;
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
}
