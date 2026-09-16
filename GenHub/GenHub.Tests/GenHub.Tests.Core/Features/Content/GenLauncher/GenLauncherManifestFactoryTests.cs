using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.GenLauncher;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.GenLauncher;

/// <summary>
/// Unit tests for <see cref="GenLauncherManifestFactory"/>.
/// </summary>
public sealed class GenLauncherManifestFactoryTests
{
    /// <summary>
    /// Tests that CreateManifestsFromExtractedContentAsync hashes files and validates engine checksums.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_HashesFilesAndValidatesEngineChecksums()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "genlauncher_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var bigFilePath = Path.Combine(tempDir, "mod.big");
            var bigContent = "Dummy Big File Content";
            var fileBytes = Encoding.UTF8.GetBytes(bigContent);
            await File.WriteAllBytesAsync(bigFilePath, fileBytes);

            var md5Bytes = MD5.HashData(fileBytes);
            var md5Hex = Convert.ToHexString(md5Bytes).ToLowerInvariant();

            var shaBytes = SHA256.HashData(fileBytes);
            var shaHex = Convert.ToHexString(shaBytes).ToLowerInvariant();

            var originalManifest = new ContentManifest
            {
                Id = ManifestId.Create("1.1.genlauncher.mod.test"),
                Name = "Test Mod",
                Version = "1.0",
                ContentType = ContentType.Mod,
                TargetGame = GameType.ZeroHour,
                Publisher = new PublisherInfo
                {
                    PublisherType = PublisherTypeConstants.GenLauncher,
                },
            };
            originalManifest.Files.Add(new ManifestFile
            {
                RelativePath = "mod.big",
                Hash = md5Hex,
            });

            var archiveProcessorMock = new Mock<IArchivePayloadProcessor>();
            var loggerMock = new Mock<ILogger<GenLauncherManifestFactory>>();

            var factory = new GenLauncherManifestFactory(archiveProcessorMock.Object, loggerMock.Object);

            var result = await factory.CreateManifestsFromExtractedContentAsync(
                originalManifest,
                tempDir,
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);

            var createdManifest = Assert.Single(result.Data);
            Assert.Single(createdManifest.Files);
            Assert.Equal("mod.big", createdManifest.Files[0].RelativePath);
            Assert.Equal(shaHex, createdManifest.Files[0].Hash); // Should have updated to SHA256 CAS hash
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
