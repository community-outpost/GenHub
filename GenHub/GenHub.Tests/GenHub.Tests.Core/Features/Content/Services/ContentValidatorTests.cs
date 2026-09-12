using System;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Regression tests for validation during acquisition, before staging files reach CAS.
/// </summary>
public sealed class ContentValidatorTests : IDisposable
{
    private readonly string _stagingDirectory = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies a staging file is valid before its later CAS storage operation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task ValidateContentIntegrityAsync_StagedContentAddressableFile_ValidatesBeforeCasStorageAsync()
    {
        // Arrange
        Directory.CreateDirectory(_stagingDirectory);
        await File.WriteAllTextAsync(Path.Combine(_stagingDirectory, "payload.map"), "map payload");
        var casService = new Mock<ICasService>();
        var validator = new ContentValidator(
            new Mock<IFileOperationsService>().Object,
            casService.Object,
            new Mock<ILogger<ContentValidator>>().Object);
        var manifest = new ContentManifest
        {
            Id = "1.0.test.map.staged",
            Name = "Staged content",
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "payload.map",
                    Hash = "staged-hash",
                    SourceType = ContentSourceType.ContentAddressable,
                },
            ],
        };

        // Act
        var result = await validator.ValidateContentIntegrityAsync(_stagingDirectory, manifest);

        // Assert
        Assert.True(result.IsValid);
        casService.Verify(service => service.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Deletes the test staging directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_stagingDirectory))
        {
            Directory.Delete(_stagingDirectory, recursive: true);
        }
    }
}
