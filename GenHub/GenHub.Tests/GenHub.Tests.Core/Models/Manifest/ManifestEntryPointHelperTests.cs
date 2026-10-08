using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System;
using System.IO;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Unit tests for <see cref="ManifestEntryPointHelper"/>.
/// </summary>
public sealed class ManifestEntryPointHelperTests : IDisposable
{
    private readonly string _payload = Path.Combine(Path.GetTempPath(), "GenHub_BakeEntry_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestEntryPointHelperTests"/> class.
    /// </summary>
    public ManifestEntryPointHelperTests()
    {
        Directory.CreateDirectory(_payload);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_payload))
        {
            Directory.Delete(_payload, recursive: true);
        }
    }

    /// <summary>
    /// An explicitly declared entry that exists always wins and is never overwritten.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_DeclaredEntry_ReturnsDeclaredUnchanged()
    {
        Directory.CreateDirectory(Path.Combine(_payload, "custom"));
        File.WriteAllText(Path.Combine(_payload, "custom", "launcher.sh"), "#!/bin/sh\n");
        var manifest = new ContentManifest
        {
            Id = "1.0.test.gameclient.declared",
            ContentType = ContentType.GameClient,
            EntryPoint = "custom/launcher.sh",
        };

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.True(result.Success);
        Assert.Equal("custom/launcher.sh", result.Data);
        Assert.Equal("custom/launcher.sh", manifest.EntryPoint);
    }

    /// <summary>
    /// A declared game client entry naming no payload file fails instead of shipping
    /// an unlaunchable manifest.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_DeclaredEntryMissing_Fails()
    {
        var manifest = new ContentManifest
        {
            Id = "1.0.test.gameclient.declaredmissing",
            ContentType = ContentType.GameClient,
            EntryPoint = "custom/launcher.sh",
        };

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.False(result.Success);
    }

    /// <summary>
    /// The host variant's declared entry is validated against the payload and returned
    /// without sniffing. A foreign variant's entry is not in the host payload and is kept.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_HostVariantDeclared_ReturnsHostEntryAndKeepsForeignEntry()
    {
        File.WriteAllBytes(Path.Combine(_payload, "macclient"), [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00]);
        File.WriteAllBytes(Path.Combine(_payload, "helper"), [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]);
        var manifest = VariantManifestFixture.Create([], []);
        manifest.Variants[0].EntryPoint = "win.exe";
        manifest.Variants[1].EntryPoint = "macclient";

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.True(result.Success);
        Assert.Equal("macclient", result.Data);
        Assert.Equal("win.exe", manifest.Variants[0].EntryPoint);
        Assert.Equal("macclient", manifest.Variants[1].EntryPoint);
        Assert.Null(manifest.EntryPoint);
    }

    /// <summary>
    /// Variants missing entries are filled from a single detection pass.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_VariantMissingEntry_FillsFromDetection()
    {
        File.WriteAllBytes(Path.Combine(_payload, "win.exe"), [(byte)'M', (byte)'Z', 0x00, 0x00]);
        var manifest = new ContentManifest
        {
            Id = "1.0.test.gameclient.variantfill",
            ContentType = ContentType.GameClient,
            Variants =
            [
                new ArtifactVariant(),
            ],
        };

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.True(result.Success);
        Assert.Equal("win.exe", result.Data);
        Assert.Equal("win.exe", manifest.Variants[0].EntryPoint);
        Assert.Null(manifest.EntryPoint);
    }

    /// <summary>
    /// Only the host variant is detected from the payload. A foreign variant without an
    /// entry no longer fails the bake and keeps no entry.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_ForeignVariantMissingEntry_BakesOnlyHostVariant()
    {
        File.WriteAllBytes(Path.Combine(_payload, "GeneralsOnlineZH"), [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00]);
        var manifest = VariantManifestFixture.Create([], []);

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.True(result.Success);
        Assert.Equal("GeneralsOnlineZH", result.Data);
        Assert.Null(manifest.Variants[0].EntryPoint);
        Assert.Equal("GeneralsOnlineZH", manifest.Variants[1].EntryPoint);
        Assert.Null(manifest.EntryPoint);
    }

    /// <summary>
    /// A host variant entry that names no payload file fails instead of shipping an
    /// unlaunchable manifest.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_HostVariantDeclaredEntryMissing_Fails()
    {
        File.WriteAllBytes(Path.Combine(_payload, "GeneralsOnlineZH"), [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00]);
        var manifest = VariantManifestFixture.Create([], []);
        manifest.Variants[1].EntryPoint = "missing/generalszh";

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.False(result.Success);
        Assert.Contains("missing/generalszh", result.FirstError, StringComparison.Ordinal);
        Assert.Equal("missing/generalszh", manifest.Variants[1].EntryPoint);
    }

    /// <summary>
    /// A variant manifest with no host variant has nothing to bake and succeeds untouched.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_NoHostVariant_SucceedsWithoutBaking()
    {
        File.WriteAllBytes(Path.Combine(_payload, "GeneralsOnlineZH"), [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00]);
        var manifest = VariantManifestFixture.Create([], []);
        manifest.Variants.RemoveAt(1);

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.True(result.Success);
        Assert.Null(result.Data);
        Assert.Null(manifest.Variants[0].EntryPoint);
    }

    /// <summary>
    /// A game client payload gets its detected entry baked as the declaration.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_GameClientWithSingleNative_BakesDetectedEntry()
    {
        File.WriteAllBytes(Path.Combine(_payload, "GeneralsOnlineZH"), [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00]);
        var manifest = new ContentManifest
        {
            Id = "1.0.test.gameclient.native",
            ContentType = ContentType.GameClient,
        };

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.True(result.Success);
        Assert.Equal("GeneralsOnlineZH", result.Data);
        Assert.Equal("GeneralsOnlineZH", manifest.EntryPoint);
    }

    /// <summary>
    /// An ambiguous game client payload fails so it never ships a manifest that cannot launch.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_AmbiguousGameClient_Fails()
    {
        File.WriteAllBytes(Path.Combine(_payload, "alpha"), [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00]);
        File.WriteAllBytes(Path.Combine(_payload, "beta"), [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]);
        var manifest = new ContentManifest
        {
            Id = "1.0.test.gameclient.ambiguous",
            ContentType = ContentType.GameClient,
        };

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.False(result.Success);
        Assert.Null(manifest.EntryPoint);
    }

    /// <summary>
    /// Non-client content keeps the legacy manifest-file inference.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_ModWithSingleLegacyCandidate_BakesLegacyEntry()
    {
        var manifest = new ContentManifest
        {
            Id = "1.0.test.mod.legacy",
            ContentType = ContentType.Mod,
            Files =
            [
                new ManifestFile { RelativePath = "setup.exe", IsExecutable = true },
                new ManifestFile { RelativePath = "readme.txt", IsExecutable = false },
            ],
        };

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.True(result.Success);
        Assert.Equal("setup.exe", manifest.EntryPoint);
    }

    /// <summary>
    /// Ambiguous non-client content stays unbaked but still succeeds.
    /// </summary>
    [Fact]
    public void BakeEntryPoint_AmbiguousMod_SucceedsWithoutEntry()
    {
        var manifest = new ContentManifest
        {
            Id = "1.0.test.mod.ambiguous",
            ContentType = ContentType.Mod,
            Files =
            [
                new ManifestFile { RelativePath = "readme.txt", IsExecutable = false },
            ],
        };

        var result = ManifestEntryPointHelper.BakeEntryPoint(manifest, _payload);

        Assert.True(result.Success);
        Assert.Null(result.Data);
        Assert.Null(manifest.EntryPoint);
    }
}
