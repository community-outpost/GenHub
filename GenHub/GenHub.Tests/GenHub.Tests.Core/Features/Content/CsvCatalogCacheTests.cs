using System;
using System.IO;
using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="CsvCatalogCache"/>.
/// </summary>
public class CsvCatalogCacheTests
{
    /// <summary>
    /// Verifies that cache construction removes expired entries while retaining recent entries.
    /// </summary>
    [Fact]
    public void Constructor_PrunesExpiredEntries()
    {
        var applicationDataDirectory = Directory.CreateTempSubdirectory();
        try
        {
            var cacheDirectory = Path.Combine(
                applicationDataDirectory.FullName,
                DirectoryNames.Cache,
                CsvConstants.CacheDirectoryName);
            Directory.CreateDirectory(cacheDirectory);
            var expiredPath = Path.Combine(cacheDirectory, $"expired{CsvConstants.CacheFileExtension}");
            var recentPath = Path.Combine(cacheDirectory, $"recent{CsvConstants.CacheFileExtension}");
            File.WriteAllText(expiredPath, "expired");
            File.WriteAllText(recentPath, "recent");
            File.SetLastWriteTimeUtc(expiredPath, DateTime.UtcNow.AddDays(-(CsvConstants.CacheRetentionDays + 1)));

            var configurationProvider = new Mock<IConfigurationProviderService>();
            configurationProvider
                .Setup(provider => provider.GetApplicationDataPath())
                .Returns(applicationDataDirectory.FullName);

            _ = new CsvCatalogCache(configurationProvider.Object, Mock.Of<ILogger<CsvCatalogCache>>());

            File.Exists(expiredPath).Should().BeFalse();
            File.Exists(recentPath).Should().BeTrue();
        }
        finally
        {
            applicationDataDirectory.Delete(true);
        }
    }
}
