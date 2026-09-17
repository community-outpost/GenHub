using GenHub.Core.Constants;
using GenHub.Features.Content.Services.GenLauncher;
using System;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.GenLauncher;

/// <summary>
/// Unit tests for <see cref="GenLauncherS3Signer"/>.
/// </summary>
public sealed class GenLauncherS3SignerTests
{
    /// <summary>
    /// Tests that ResolveCredentials returns the provided keys when explicitly passed.
    /// </summary>
    [Fact]
    public void ResolveCredentials_WithExplicitKeys_ReturnsExplicitKeys()
    {
        var (shouldSign, pub, sec) = GenLauncherS3Signer.ResolveCredentials(
            "custom.s3.endpoint.com", "MYPUBKEY", "MYSECKEY");

        Assert.True(shouldSign);
        Assert.Equal("MYPUBKEY", pub);
        Assert.Equal("MYSECKEY", sec);
    }

    /// <summary>
    /// Tests that ResolveCredentials defaults to InSave credentials for InSave host endpoints.
    /// </summary>
    [Fact]
    public void ResolveCredentials_WithInSaveHost_ReturnsDefaultKeys()
    {
        var (shouldSign, pub, sec) = GenLauncherS3Signer.ResolveCredentials(
            "gen.insave.ovh:9000", null, null);

        Assert.True(shouldSign);
        Assert.Equal(GenLauncherConstants.DefaultGenInsavePublicKey, pub);
        Assert.Equal(GenLauncherConstants.DefaultGenInsaveSecretKey, sec);
    }

    /// <summary>
    /// Tests that ResolveCredentials returns false when no keys are provided for non-InSave hosts.
    /// </summary>
    [Fact]
    public void ResolveCredentials_WithNonInSaveHostAndNoKeys_ReturnsFalse()
    {
        var (shouldSign, pub, sec) = GenLauncherS3Signer.ResolveCredentials(
            "s3.wasabisys.com", null, null);

        Assert.False(shouldSign);
        Assert.Empty(pub);
        Assert.Empty(sec);
    }

    /// <summary>
    /// Tests that GeneratePresignedGetUrl generates valid AWS4 presigned parameters for InSave bucket root queries.
    /// </summary>
    [Fact]
    public void GeneratePresignedGetUrl_ForInSaveBucket_GeneratesValidAws4Signature()
    {
        var queryParams = new Dictionary<string, string>
        {
            ["prefix"] = "Improved AI 1.2/",
        };

        var url = GenLauncherS3Signer.GeneratePresignedGetUrl(
            "gen.insave.ovh:9000",
            "improved-ai",
            objectKey: null,
            extraQueryParams: queryParams);

        Assert.StartsWith("http://gen.insave.ovh:9000/improved-ai?X-Amz-Algorithm=AWS4-HMAC-SHA256", url);
        Assert.Contains($"X-Amz-Credential={GenLauncherConstants.DefaultGenInsavePublicKey}%2F", url);
        Assert.Contains("X-Amz-Date=", url);
        Assert.Contains("X-Amz-Expires=86400", url);
        Assert.Contains("X-Amz-SignedHeaders=host", url);
        Assert.Contains("prefix=Improved%20AI%201.2%2F", url);
        Assert.Contains("&X-Amz-Signature=", url);
    }

    /// <summary>
    /// Tests that GeneratePresignedGetUrl correctly URL-escapes special characters and generates valid signature for objects.
    /// </summary>
    [Fact]
    public void GeneratePresignedGetUrl_ForInSaveObject_EscapesKeyAndGeneratesValidSignature()
    {
        var url = GenLauncherS3Signer.GeneratePresignedGetUrl(
            "gen.insave.ovh:9000",
            "tpotw",
            "TPOTWFile/!0_TPOTW_V2A.big");

        Assert.StartsWith("http://gen.insave.ovh:9000/tpotw/TPOTWFile/%210_TPOTW_V2A.big?", url);
        Assert.Contains("X-Amz-Algorithm=AWS4-HMAC-SHA256", url);
        Assert.Contains($"X-Amz-Credential={GenLauncherConstants.DefaultGenInsavePublicKey}%2F", url);
        Assert.Contains("&X-Amz-Signature=", url);
    }

    /// <summary>
    /// Tests that GeneratePresignedGetUrl returns plain unsigned URL for hosts without credentials.
    /// </summary>
    [Fact]
    public void GeneratePresignedGetUrl_ForUnsignedHost_ReturnsPlainUrl()
    {
        var url = GenLauncherS3Signer.GeneratePresignedGetUrl(
            "s3.wasabisys.com",
            "public-bucket",
            "folder/file.big");

        Assert.Equal("https://s3.wasabisys.com/public-bucket/folder/file.big", url);
    }
}
