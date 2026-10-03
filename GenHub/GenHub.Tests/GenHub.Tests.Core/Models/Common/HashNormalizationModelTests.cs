using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using System;

namespace GenHub.Tests.Core.Models.Common;

/// <summary>
/// Unit tests verifying consistent hash trimming across domain models.
/// </summary>
public class HashNormalizationModelTests
{
    private const string RawHash = "  085726A5DB6C885EB17F5A38F1B4B3D1899FCB57A03A5B4BE83C0214AFF13FEE \t ";
    private const string ExpectedHash = "085726A5DB6C885EB17F5A38F1B4B3D1899FCB57A03A5B4BE83C0214AFF13FEE";

    /// <summary>
    /// Verifies that ManifestFile.Hash trims leading and trailing whitespace.
    /// </summary>
    [Fact]
    public void ManifestFile_Hash_TrimsWhitespace()
    {
        var file = new ManifestFile { Hash = RawHash };
        Assert.Equal(ExpectedHash, file.Hash);
    }

    /// <summary>
    /// Verifies that ManifestFile.Hash handles null by setting string.Empty.
    /// </summary>
    [Fact]
    public void ManifestFile_Hash_HandlesNull()
    {
        var file = new ManifestFile { Hash = null! };
        Assert.Equal(string.Empty, file.Hash);
    }

    /// <summary>
    /// Verifies that InstallationInstructions.DownloadHash trims leading and trailing whitespace.
    /// </summary>
    [Fact]
    public void InstallationInstructions_DownloadHash_TrimsWhitespace()
    {
        var instructions = new InstallationInstructions { DownloadHash = RawHash };
        Assert.Equal(ExpectedHash, instructions.DownloadHash);
    }

    /// <summary>
    /// Verifies that InstallationInstructions.DownloadHash handles null.
    /// </summary>
    [Fact]
    public void InstallationInstructions_DownloadHash_HandlesNull()
    {
        var instructions = new InstallationInstructions { DownloadHash = null };
        Assert.Null(instructions.DownloadHash);
    }

    /// <summary>
    /// Verifies that ReleaseArtifact.Sha256 trims leading and trailing whitespace.
    /// </summary>
    [Fact]
    public void ReleaseArtifact_Sha256_TrimsWhitespace()
    {
        var artifact = new ReleaseArtifact { Sha256 = RawHash };
        Assert.Equal(ExpectedHash, artifact.Sha256);
    }

    /// <summary>
    /// Verifies that Checksum.Sha256 and Checksum.Md5 trim leading and trailing whitespace.
    /// </summary>
    [Fact]
    public void Checksum_Sha256AndMd5_TrimWhitespace()
    {
        var checksum = new Checksum
        {
            Sha256 = RawHash,
            Md5 = "  d41d8cd98f00b204e9800998ecf8427e \n ",
        };

        Assert.Equal(ExpectedHash, checksum.Sha256);
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", checksum.Md5);
    }

    /// <summary>
    /// Verifies that GeneralsOnlineApiResponse.Sha256 trims leading and trailing whitespace.
    /// </summary>
    [Fact]
    public void GeneralsOnlineApiResponse_Sha256_TrimsWhitespace()
    {
        var response = new GeneralsOnlineApiResponse { Sha256 = RawHash };
        Assert.Equal(ExpectedHash, response.Sha256);
    }

    /// <summary>
    /// Verifies that GeneralsOnlineRelease.Sha256 trims leading and trailing whitespace.
    /// </summary>
    [Fact]
    public void GeneralsOnlineRelease_Sha256_TrimsWhitespace()
    {
        var release = new GeneralsOnlineRelease
        {
            Version = "100126_QFE3",
            PortableUrl = "https://example.com/test.zip",
            Sha256 = RawHash,
        };

        Assert.Equal(ExpectedHash, release.Sha256);
    }
}
