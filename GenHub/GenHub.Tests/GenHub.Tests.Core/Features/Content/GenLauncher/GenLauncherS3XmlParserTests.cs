using GenHub.Features.Content.Services.GenLauncher;
using System;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.GenLauncher;

/// <summary>
/// Unit tests for <see cref="GenLauncherS3XmlParser"/>.
/// </summary>
public sealed class GenLauncherS3XmlParserTests
{
    private const string SampleS3XmlWithNamespace = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<ListBucketResult xmlns=""http://s3.amazonaws.com/doc/2006-03-01/"">
    <Name>generals-mods</Name>
    <Prefix>Shockwave_1.2/</Prefix>
    <Contents>
        <Key>Shockwave_1.2/</Key>
        <Size>0</Size>
        <ETag>&quot;d41d8cd98f00b204e9800998ecf8427e&quot;</ETag>
    </Contents>
    <Contents>
        <Key>Shockwave_1.2/Shockwave.big</Key>
        <Size>104857600</Size>
        <ETag>&quot;0123456789abcdef0123456789abcdef&quot;</ETag>
    </Contents>
    <Contents>
        <Key>Shockwave_1.2/Data/INI/GameData.ini</Key>
        <Size>4096</Size>
        <ETag>fedcba9876543210fedcba9876543210</ETag>
    </Contents>
</ListBucketResult>";

    private const string SampleS3XmlWithoutNamespace = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<ListBucketResult>
    <Name>zh-mods</Name>
    <Prefix>Contra009/</Prefix>
    <Contents>
        <Key>Contra009/Contra.big</Key>
        <Size>52428800</Size>
        <ETag>&quot;aabbccddeeff00112233445566778899&quot;</ETag>
    </Contents>
</ListBucketResult>";

    /// <summary>
    /// Tests that ParseListBucketResult correctly extracts files and presigns URLs for InSave host.
    /// </summary>
    [Fact]
    public void ParseListBucketResult_WithNamespace_ExtractsValidFiles()
    {
        var result = GenLauncherS3XmlParser.ParseListBucketResult(
            SampleS3XmlWithNamespace,
            "Shockwave_1.2/",
            "gen.insave.ovh:9000",
            "generals-mods");

        Assert.True(result.Success);
        var entries = result.Data;
        Assert.Equal(2, entries.Count);

        var first = entries[0];
        Assert.Equal("Shockwave_1.2/Shockwave.big", first.Key);
        Assert.Equal("Shockwave.big", first.RelativePath);
        Assert.Equal(104857600, first.Size);
        Assert.Equal("0123456789abcdef0123456789abcdef", first.ETag);
        Assert.StartsWith("http://gen.insave.ovh:9000/generals-mods/Shockwave_1.2/Shockwave.big?", first.DownloadUrl);
        Assert.Contains("X-Amz-Signature=", first.DownloadUrl);
        Assert.Contains("X-Amz-Algorithm=AWS4-HMAC-SHA256", first.DownloadUrl);

        var second = entries[1];
        Assert.Equal("Data/INI/GameData.ini", second.RelativePath);
        Assert.Equal("fedcba9876543210fedcba9876543210", second.ETag);
    }

    /// <summary>
    /// Tests that ParseListBucketResult correctly extracts files when XML has no namespace and host is unsigned.
    /// </summary>
    [Fact]
    public void ParseListBucketResult_WithoutNamespace_ExtractsValidFiles()
    {
        var result = GenLauncherS3XmlParser.ParseListBucketResult(
            SampleS3XmlWithoutNamespace,
            "Contra009",
            "s3.wasabisys.com",
            "zh-mods");

        Assert.True(result.Success);
        var entries = result.Data;
        Assert.Single(entries);
        Assert.Equal("Contra.big", entries[0].RelativePath);
        Assert.Equal(52428800, entries[0].Size);
        Assert.Equal("aabbccddeeff00112233445566778899", entries[0].ETag);
        Assert.Equal("https://s3.wasabisys.com/zh-mods/Contra009/Contra.big", entries[0].DownloadUrl);
    }

    /// <summary>
    /// Tests that ParseListBucketResult returns an empty list for empty or invalid XML.
    /// </summary>
    [Fact]
    public void ParseListBucketResult_EmptyXml_ReturnsEmptyList()
    {
        var result = GenLauncherS3XmlParser.ParseListBucketResult(string.Empty, "folder", "host", "bucket");
        Assert.True(result.Success);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// Tests that ParseListBucketResult returns a failure result when S3 returns an Error XML document.
    /// </summary>
    [Fact]
    public void ParseListBucketResult_ErrorXml_ReturnsFailureResult()
    {
        const string errorXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Error>
    <Code>NoSuchBucket</Code>
    <Message>The specified bucket does not exist</Message>
    <BucketName>invalid-bucket</BucketName>
</Error>";

        var result = GenLauncherS3XmlParser.ParseListBucketResult(errorXml, "folder", "host", "bucket");

        Assert.False(result.Success);
        Assert.Contains("NoSuchBucket", result.FirstError);
        Assert.Contains("The specified bucket does not exist", result.FirstError);
    }

    /// <summary>
    /// Tests that ParseListBucketResult generates direct unsigned URLs when useAuth is false.
    /// </summary>
    [Fact]
    public void ParseListBucketResult_Anonymous_ExtractsUnsignedDownloadUrl()
    {
        var result = GenLauncherS3XmlParser.ParseListBucketResult(
            SampleS3XmlWithNamespace,
            "Shockwave_1.2/",
            "gen.insave.ovh:9000",
            "generals-mods",
            out var isTruncated,
            out var nextMarker,
            useAuth: false);

        Assert.True(result.Success);
        var entries = result.Data;
        Assert.Equal(2, entries.Count);
        var first = entries[0];
        Assert.Equal("http://gen.insave.ovh:9000/generals-mods/Shockwave_1.2/Shockwave.big", first.DownloadUrl);
        Assert.DoesNotContain("X-Amz-Signature=", first.DownloadUrl);
        Assert.False(isTruncated);
        Assert.Null(nextMarker);
    }

    /// <summary>
    /// Tests that ParseListBucketResult supports NextContinuationToken from S3 v2 listings.
    /// </summary>
    [Fact]
    public void ParseListBucketResult_WithNextContinuationToken_ExtractsContinuationToken()
    {
        const string s3v2Xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<ListBucketResult xmlns=""http://s3.amazonaws.com/doc/2006-03-01/"">
    <Name>zh-mods</Name>
    <IsTruncated>true</IsTruncated>
    <NextContinuationToken>token-12345</NextContinuationToken>
    <Contents>
        <Key>Contra/file.big</Key>
        <Size>100</Size>
        <ETag>&quot;d41d8cd98f00b204e9800998ecf8427e&quot;</ETag>
    </Contents>
</ListBucketResult>";

        var result = GenLauncherS3XmlParser.ParseListBucketResult(
            s3v2Xml,
            "Contra",
            "gen.insave.ovh:9000",
            "zh-mods",
            out var isTruncated,
            out var nextMarker);

        Assert.True(result.Success);
        Assert.True(isTruncated);
        Assert.Equal("token-12345", nextMarker);
        Assert.Single(result.Data);
    }
}
