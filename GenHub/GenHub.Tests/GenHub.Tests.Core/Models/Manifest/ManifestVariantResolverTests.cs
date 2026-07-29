using System.Collections.Generic;
using GenHub.Core.Models.Manifest;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Tests for <see cref="ManifestVariantResolver"/>, which replaced an order-dependent
/// <c>FirstOrDefault(f =&gt; f.IsExecutable)</c> with an explicit resolution chain.
/// </summary>
public class ManifestVariantResolverTests
{
    private static ManifestFile File(string path, bool isExecutable = false) =>
        new() { RelativePath = path, IsExecutable = isExecutable };

    /// <summary>
    /// A manifest with no variants keeps behaving exactly as before. Every manifest
    /// written before variants existed is this shape, including everything already
    /// sitting in a user's content store.
    /// </summary>
    [Fact]
    public void NoVariants_UsesFlatFileList()
    {
        var manifest = new ContentManifest { Files = [File("generals.exe", true), File("data.big")] };

        Assert.Equal(2, ManifestVariantResolver.ResolveFiles(manifest).Count);
        Assert.True(ManifestVariantResolver.SupportsRuntime(manifest, "osx-arm64"));
        Assert.Null(ManifestVariantResolver.ResolveVariant(manifest));
    }

    /// <summary>
    /// With variants declared, the host's runtime identifier selects one.
    /// </summary>
    [Fact]
    public void Variants_SelectByRuntimeIdentifier()
    {
        var manifest = new ContentManifest
        {
            Variants =
            [
                new() { RuntimeIdentifiers = ["win-x64"], EntryPoint = "generalszh.exe", Files = [File("generalszh.exe", true)] },
                new() { RuntimeIdentifiers = ["osx-arm64"], EntryPoint = "generalszh", Files = [File("generalszh", true), File("libSDL3.dylib")] },
            ],
        };

        Assert.Equal("generalszh", ManifestVariantResolver.ResolveEntryPoint(manifest, "osx-arm64").RelativePath);
        Assert.Equal("generalszh.exe", ManifestVariantResolver.ResolveEntryPoint(manifest, "win-x64").RelativePath);
        Assert.Equal(2, ManifestVariantResolver.ResolveFiles(manifest, "osx-arm64").Count);
    }

    /// <summary>
    /// Content that declares variants but matches none must report that it cannot run
    /// here, so the catalogue can hide it rather than let a user install something inert.
    /// </summary>
    [Fact]
    public void Variants_UnmatchedRuntime_IsNotSupported()
    {
        var manifest = new ContentManifest
        {
            Variants = [new() { RuntimeIdentifiers = ["win-x64"], Files = [File("generals.exe", true)] }],
        };

        Assert.False(ManifestVariantResolver.SupportsRuntime(manifest, "osx-arm64"));
        Assert.Empty(ManifestVariantResolver.ResolveFiles(manifest, "osx-arm64"));
    }

    /// <summary>
    /// A platform-neutral variant is a valid fallback, but an explicit match wins. A
    /// release carrying both a native build and a neutral asset bundle must resolve to
    /// the native build on a platform it supports.
    /// </summary>
    [Fact]
    public void ExplicitRuntimeMatch_BeatsNeutralVariant()
    {
        var manifest = new ContentManifest
        {
            Variants =
            [
                new() { RuntimeIdentifiers = [], EntryPoint = "shared", Files = [File("shared", true)] },
                new() { RuntimeIdentifiers = ["osx-arm64"], EntryPoint = "native", Files = [File("native", true)] },
            ],
        };

        Assert.Equal("native", ManifestVariantResolver.ResolveEntryPoint(manifest, "osx-arm64").RelativePath);
        Assert.Equal("shared", ManifestVariantResolver.ResolveEntryPoint(manifest, "linux-x64").RelativePath);
    }

    /// <summary>
    /// This is the case the old code got silently wrong. Several files can legitimately
    /// need the execute bit, and picking the first one is picking by enumeration order.
    /// </summary>
    [Fact]
    public void MultipleExecutables_WithoutEntryPoint_FailsAndListsCandidates()
    {
        var manifest = new ContentManifest
        {
            Files = [File("generalszh", true), File("crashhandler", true), File("updater", true)],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.False(resolution.Success);
        Assert.Contains("ambiguous", resolution.Reason);
        Assert.Equal(3, resolution.Candidates.Count);
        Assert.Contains("generalszh", resolution.ToString());
    }

    /// <summary>
    /// A declared entry point removes the ambiguity above.
    /// </summary>
    [Fact]
    public void DeclaredEntryPoint_ResolvesAmbiguity()
    {
        var manifest = new ContentManifest
        {
            EntryPoint = "generalszh",
            Files = [File("crashhandler", true), File("generalszh", true), File("updater", true)],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success);
        Assert.Equal("generalszh", resolution.RelativePath);
    }

    /// <summary>
    /// An entry point naming a file the manifest does not contain is a manifest defect.
    /// Catching it here is far more diagnosable than a missing-file error at launch.
    /// </summary>
    [Fact]
    public void DeclaredEntryPoint_NotInFileList_Fails()
    {
        var manifest = new ContentManifest
        {
            EntryPoint = "generalszh",
            Files = [File("generals.exe", true)],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.False(resolution.Success);
        Assert.Contains("not among its", resolution.Reason);
    }

    /// <summary>
    /// Path separators and case must not defeat the entry-point match: manifests are
    /// authored on Windows and consumed on Unix.
    /// </summary>
    /// <param name="entryPoint">The declared entry point.</param>
    /// <param name="filePath">The path as stored in the file list.</param>
    [Theory]
    [InlineData("Release/generalszh", "Release/generalszh")]
    [InlineData("Release\\generalszh", "Release/generalszh")]
    [InlineData("release/GENERALSZH", "Release/generalszh")]
    public void EntryPointMatching_IgnoresSeparatorAndCase(string entryPoint, string filePath)
    {
        var manifest = new ContentManifest { EntryPoint = entryPoint, Files = [File(filePath, true)] };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success);
        Assert.Equal(filePath, resolution.RelativePath);
    }

    /// <summary>
    /// A variant must not inherit the flat manifest entry point. The flat file list and
    /// its entry point are ignored whenever variants are present.
    /// </summary>
    [Fact]
    public void VariantWithoutEntryPoint_DoesNotUseFlatManifestEntryPoint()
    {
        var manifest = new ContentManifest
        {
            EntryPoint = "flat.exe",
            Files = [File("flat.exe", true)],
            Variants =
            [
                new()
                {
                    RuntimeIdentifiers = ["linux-x64"],
                    Files = [File("run.sh", true), File("generalszh", true)],
                },
            ],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest, "linux-x64");

        Assert.True(resolution.Success);
        Assert.Equal("generalszh", resolution.RelativePath);
    }

    /// <summary>
    /// Helper scripts need execute permission but are not inferred launch targets.
    /// </summary>
    [Fact]
    public void ExecutePermissionHelper_DoesNotMakeNativeClientAmbiguous()
    {
        var manifest = new ContentManifest
        {
            Files = [File("run.sh", true), File("generalszh", true)],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success);
        Assert.Equal("generalszh", resolution.RelativePath);
    }

    /// <summary>
    /// Legacy manifests that set no execute flags still resolve when exactly one file
    /// looks like a launch target by extension.
    /// </summary>
    [Fact]
    public void LegacyManifest_WithSingleExe_StillResolves()
    {
        var manifest = new ContentManifest { Files = [File("generals.exe"), File("data.big"), File("d3d8.dll")] };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success);
        Assert.Equal("generals.exe", resolution.RelativePath);
    }

    /// <summary>
    /// A manifest with nothing runnable reports that plainly rather than resolving to
    /// some arbitrary data file.
    /// </summary>
    [Fact]
    public void ManifestWithNoLaunchableFile_Fails()
    {
        var manifest = new ContentManifest { Files = [File("maps.big"), File("Options.ini")] };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.False(resolution.Success);
        Assert.Contains("no launchable file", resolution.Reason);
    }
}
