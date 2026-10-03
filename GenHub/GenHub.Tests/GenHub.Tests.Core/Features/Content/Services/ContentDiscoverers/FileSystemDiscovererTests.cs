using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Features.Content.Services.ContentDiscoverers;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// Tests for <see cref="FileSystemDiscoverer"/>.
/// </summary>
public sealed class FileSystemDiscovererTests : IDisposable
{
    private readonly string _contentDirectory = Directory.CreateTempSubdirectory("GenHub.FileSystemDiscovererTests.").FullName;

    /// <summary>
    /// A scan failure the discovery service does not skip is returned as a failure result
    /// instead of escaping from discovery.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task DiscoverAsync_WhenScanThrowsUnskippableException_ReturnsFailureAsync()
    {
        var configuration = new Mock<IConfigurationProviderService>();
        configuration.Setup(c => c.GetContentDirectories()).Returns(new List<string> { _contentDirectory });
        configuration.Setup(c => c.GetApplicationDataPath()).Returns(_contentDirectory);
        var discoveryService = new ManifestDiscoveryService(
            NullLogger<ManifestDiscoveryService>.Instance,
            new Mock<IManifestCache>().Object,
            configuration.Object,
            (_, _) => throw new SecurityException("scan denied"),
            _ => []);
        var discoverer = new FileSystemDiscoverer(
            NullLogger<FileSystemDiscoverer>.Instance,
            discoveryService,
            configuration.Object);

        var result = await discoverer.DiscoverAsync(new ContentSearchQuery());

        Assert.False(result.Success);
        Assert.Contains("scan denied", result.FirstError);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_contentDirectory, true);
        }
        catch (IOException)
        {
            // Ignore cleanup errors
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore cleanup errors
        }
    }
}
