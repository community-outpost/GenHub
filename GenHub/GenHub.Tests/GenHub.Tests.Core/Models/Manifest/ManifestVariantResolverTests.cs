using GenHub.Core.Models.Manifest;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Tests for <see cref="ManifestVariantResolver"/>, which replaced an order-dependent
/// <c>FirstOrDefault(f =&gt; f.IsExecutable)</c> with an explicit resolution chain.
/// </summary>
public class ManifestVariantResolverTests
{
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
    /// When multiple executables exist (e.g. game client + development tools),
    /// a single matching primary game executable resolves unambiguously.
    /// </summary>
    [Fact]
    public void MultipleExecutables_WithSinglePrimaryGameExecutable_ResolvesPrimaryExecutable()
    {
        var manifest = new ContentManifest
        {
            Files =
            [
                File(@"tools\WorldBuilderZH.exe", true),
                File(@"tools\guiedit.exe", true),
                File(@"generalszh-weekly-2026-07-31\generalszh.exe", true),
            ],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success);
        Assert.Equal(@"generalszh-weekly-2026-07-31\generalszh.exe", resolution.RelativePath);
        Assert.Contains("primary game executable candidate", resolution.Reason);
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

    /// <summary>
    /// Verifies that game.dat is resolved when marked as executable even without a declared entry point.
    /// </summary>
    [Fact]
    public void ExecutableDatFile_ResolvesAsLaunchCandidate()
    {
        var manifest = new ContentManifest
        {
            Files = [File("game.dat", true)],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success);
        Assert.Equal("game.dat", resolution.RelativePath);
    }

    /// <summary>
    /// A manifest containing game.dat (such as Zero Hour installations) resolves game.dat
    /// as the entry point even when no explicit entry point is declared.
    /// </summary>
    [Fact]
    public void Manifest_WithGameDat_ResolvesGameDat()
    {
        var manifest = new ContentManifest
        {
            Files =
            [
                File("game.dat", true),
                File("Generals.dat", false),
                File("binkw32.dll", false),
                File("mss32.dll", false),
            ],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success);
        Assert.Equal("game.dat", resolution.RelativePath);
    }

    /// <summary>
    /// Legacy manifests containing game.dat without execute flags still resolve game.dat.
    /// </summary>
    [Fact]
    public void LegacyManifest_WithGameDat_ResolvesGameDat()
    {
        var manifest = new ContentManifest
        {
            Files =
            [
                File("game.dat", false),
                File("Generals.dat", false),
                File("binkw32.dll", false),
                File("mss32.dll", false),
            ],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success);
        Assert.Equal("game.dat", resolution.RelativePath);
    }

    /// <summary>
    /// Manifests containing generals.ctr resolve generals.ctr as the primary executable.
    /// </summary>
    [Fact]
    public void Manifest_WithGeneralsCtr_ResolvesGeneralsCtr()
    {
        var manifest = new ContentManifest
        {
            Files =
            [
                File("generals.ctr", true),
                File("d3d8.enb", false),
                File("generals.lcf", false),
                File("generals.dat", false),
            ],
        };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success);
        Assert.Equal("generals.ctr", resolution.RelativePath);
    }

    /// <summary>
    /// A single-asset download manifest carrying only a Flatpak bundle resolves the
    /// bundle as its entry point, so the launch pipeline provisions it instead of
    /// reporting "no launchable file" and falling back to an unrelated executable.
    /// </summary>
    [Fact]
    public void SingleFlatpakFile_ResolvesAsEntryPoint()
    {
        var manifest = new ContentManifest { Files = [File("Linux-GeneralsXZH.flatpak")] };

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success, resolution.ToString());
        Assert.Equal("Linux-GeneralsXZH.flatpak", resolution.RelativePath);
    }

    /// <summary>
    /// A manifest-level relationship resolves on single-build manifests. Absence is
    /// normal and means the entry is the game itself.
    /// </summary>
    [Fact]
    public void LaunchRelationship_ManifestLevel_Resolves()
    {
        var manifest = new ContentManifest
        {
            LaunchRelationship = new LaunchRelationship { ProcessName = "game", DiscoveryTimeoutMs = 5000 },
        };

        var resolved = ManifestVariantResolver.ResolveLaunchRelationship(manifest);

        Assert.NotNull(resolved);
        Assert.Equal("game", resolved.ProcessName);
        Assert.Equal(5000, resolved.DiscoveryTimeoutMs);
        Assert.Null(ManifestVariantResolver.ResolveLaunchRelationship(new ContentManifest()));
    }

    /// <summary>
    /// Variant relationships mirror entry-point placement: the matching variant wins,
    /// and the manifest-level value is ignored once variants exist.
    /// </summary>
    [Fact]
    public void LaunchRelationship_VariantLevel_BeatsManifestLevel()
    {
        var manifest = new ContentManifest
        {
            LaunchRelationship = new LaunchRelationship { ProcessName = "ignored" },
            Variants =
            [
                new() { RuntimeIdentifiers = ["win-x64"], LaunchRelationship = new LaunchRelationship { ProcessName = "game" } },
                new() { RuntimeIdentifiers = ["osx-arm64"] },
            ],
        };

        Assert.Equal("game", ManifestVariantResolver.ResolveLaunchRelationship(manifest, "win-x64")?.ProcessName);
        Assert.Null(ManifestVariantResolver.ResolveLaunchRelationship(manifest, "osx-arm64"));
    }

    /// <summary>
    /// An invalid declaration resolves to nothing so the launch degrades to legacy
    /// guessing instead of adopting a nonsense name.
    /// </summary>
    /// <param name="processName">The declared name.</param>
    [Theory]
    [InlineData("subdir/game")]
    [InlineData("game.exe")]
    [InlineData("")]
    public void LaunchRelationship_InvalidDeclaration_ResolvesToNull(string processName)
    {
        var manifest = new ContentManifest
        {
            LaunchRelationship = new LaunchRelationship { ProcessName = processName },
        };

        Assert.Null(ManifestVariantResolver.ResolveLaunchRelationship(manifest));
    }

    /// <summary>
    /// Enumerating all files covers the flat list and every variant, whichever runtime
    /// each variant targets.
    /// </summary>
    [Fact]
    public void EnumerateAllFiles_IncludesFlatListAndEveryVariant()
    {
        var manifest = new ContentManifest
        {
            Files = [File("readme.txt")],
            Variants =
            [
                new() { RuntimeIdentifiers = ["win-x64"], Files = [File("generalszh.exe", true)] },
                new() { RuntimeIdentifiers = ["osx-arm64"], Files = [File("generalszh", true)] },
            ],
        };

        var paths = ManifestVariantResolver.EnumerateAllFiles(manifest).Select(f => f.RelativePath);

        Assert.Equal(["readme.txt", "generalszh.exe", "generalszh"], paths);
    }

    /// <summary>
    /// Replacing the resolved files updates only the variant that matches the runtime.
    /// </summary>
    [Fact]
    public void ReplaceResolvedFiles_UpdatesOnlyMatchingVariant()
    {
        var windows = new ArtifactVariant { RuntimeIdentifiers = ["win-x64"], Files = [File("generalszh.exe", true)] };
        var mac = new ArtifactVariant { RuntimeIdentifiers = ["osx-arm64"], Files = [File("generalszh", true)] };
        var manifest = new ContentManifest { Variants = [windows, mac] };

        ManifestVariantResolver.ReplaceResolvedFiles(manifest, [File("stored", true)], "osx-arm64");

        Assert.Equal("stored", Assert.Single(mac.Files).RelativePath);
        Assert.Equal("generalszh.exe", Assert.Single(windows.Files).RelativePath);
        Assert.Empty(manifest.Files);
    }

    /// <summary>
    /// A manifest without variants has its flat list replaced, and a manifest whose
    /// variants match no runtime is left unchanged.
    /// </summary>
    [Fact]
    public void ReplaceResolvedFiles_UsesFlatListOrLeavesUnmatchedManifestUnchanged()
    {
        var flat = new ContentManifest { Files = [File("old")] };
        ManifestVariantResolver.ReplaceResolvedFiles(flat, [File("new")], "osx-arm64");
        Assert.Equal("new", Assert.Single(flat.Files).RelativePath);

        var windows = new ArtifactVariant { RuntimeIdentifiers = ["win-x64"], Files = [File("generalszh.exe", true)] };
        var unmatched = new ContentManifest { Variants = [windows] };
        ManifestVariantResolver.ReplaceResolvedFiles(unmatched, [File("new")], "osx-arm64");
        Assert.Empty(unmatched.Files);
        Assert.Equal("generalszh.exe", Assert.Single(windows.Files).RelativePath);
    }

    /// <summary>
    /// Copying with resolved files puts the delivered files on the matching variant of
    /// the copy, keeps the other variants as declared, and leaves the original untouched.
    /// </summary>
    [Fact]
    public void CopyWithResolvedFiles_ReplacesMatchingVariantFilesOnCopyOnly()
    {
        var original = new ContentManifest
        {
            Files = [],
            Variants =
            [
                new() { RuntimeIdentifiers = ["win-x64"], EntryPoint = "generalszh.exe", Files = [File("generalszh.exe", true)] },
                new() { RuntimeIdentifiers = ["osx-arm64"], EntryPoint = "generalszh", Files = [File("generalszh.tar.gz")] },
            ],
        };

        var copy = ManifestVariantResolver.CopyWithResolvedFiles(original, [File("generalszh", true), File("libSDL3.dylib")], "osx-arm64");

        Assert.NotSame(original, copy);
        Assert.Empty(copy.Files);
        Assert.Equal(2, copy.Variants.Count);
        Assert.Equal(["generalszh", "libSDL3.dylib"], ManifestVariantResolver.ResolveFiles(copy, "osx-arm64").Select(f => f.RelativePath));
        Assert.Equal("generalszh.exe", Assert.Single(copy.Variants[0].Files).RelativePath);
        Assert.Equal("generalszh", ManifestVariantResolver.ResolveEntryPoint(copy, "osx-arm64").RelativePath);
        Assert.Equal("generalszh.tar.gz", Assert.Single(original.Variants[1].Files).RelativePath);
        Assert.Empty(original.Files);
    }

    /// <summary>
    /// Copying a manifest without variants replaces the copy's flat list only.
    /// </summary>
    [Fact]
    public void CopyWithResolvedFiles_FlatManifest_ReplacesFlatListOnCopyOnly()
    {
        var original = new ContentManifest { Files = [File("archive.zip")] };

        var copy = ManifestVariantResolver.CopyWithResolvedFiles(original, [File("generals.exe", true)], "osx-arm64");

        Assert.Equal("generals.exe", Assert.Single(copy.Files).RelativePath);
        Assert.Empty(copy.Variants);
        Assert.Equal("archive.zip", Assert.Single(original.Files).RelativePath);
    }

    /// <summary>
    /// With variants declared, the matching variant's entry point is the declared one and
    /// the root entry point is ignored.
    /// </summary>
    [Fact]
    public void GetDeclaredEntryPoint_Variants_UsesMatchingVariantAndIgnoresRoot()
    {
        var manifest = new ContentManifest
        {
            EntryPoint = "root.exe",
            Variants =
            [
                new() { RuntimeIdentifiers = ["win-x64"], EntryPoint = "generalszh.exe" },
                new() { RuntimeIdentifiers = ["osx-arm64"], EntryPoint = "generalszh" },
                new() { RuntimeIdentifiers = ["linux-x64"] },
            ],
        };

        Assert.Equal("generalszh", ManifestVariantResolver.GetDeclaredEntryPoint(manifest, "osx-arm64"));
        Assert.Equal("generalszh.exe", ManifestVariantResolver.GetDeclaredEntryPoint(manifest, "win-x64"));
        Assert.Null(ManifestVariantResolver.GetDeclaredEntryPoint(manifest, "linux-x64"));
    }

    /// <summary>
    /// Without variants the root entry point is the declared one; with variants and no
    /// matching variant there is none.
    /// </summary>
    [Fact]
    public void GetDeclaredEntryPoint_UsesRootWithoutVariantsAndNullWithoutMatch()
    {
        var flat = new ContentManifest { EntryPoint = "generals.exe" };
        var unmatched = new ContentManifest
        {
            EntryPoint = "root.exe",
            Variants = [new() { RuntimeIdentifiers = ["win-x64"], EntryPoint = "generalszh.exe" }],
        };

        Assert.Equal("generals.exe", ManifestVariantResolver.GetDeclaredEntryPoint(flat, "osx-arm64"));
        Assert.Null(ManifestVariantResolver.GetDeclaredEntryPoint(unmatched, "osx-arm64"));
    }

    /// <summary>
    /// Rewriting all files applies to the flat list and to every variant.
    /// </summary>
    [Fact]
    public void RewriteAllFiles_RewritesFlatListAndEveryVariant()
    {
        var manifest = new ContentManifest
        {
            Files = [File("readme.txt")],
            Variants =
            [
                new() { RuntimeIdentifiers = ["win-x64"], Files = [File("generalszh.exe", true)] },
                new() { RuntimeIdentifiers = ["osx-arm64"], Files = [File("generalszh", true)] },
            ],
        };

        ManifestVariantResolver.RewriteAllFiles(manifest, f => File(f.RelativePath + ".x"));

        Assert.Equal(
            ["readme.txt.x", "generalszh.exe.x", "generalszh.x"],
            ManifestVariantResolver.EnumerateAllFiles(manifest).Select(f => f.RelativePath));
    }

    /// <summary>Malformed entries do not prevent valid variants from being resolved.</summary>
    [Fact]
    public void ResolveVariant_SkipsNullEntries()
    {
        var variant = new ArtifactVariant { RuntimeIdentifiers = ["osx-arm64"], Files = [File("generalszh")] };
        var manifest = new ContentManifest { Variants = [null!, variant], Files = [null!] };

        Assert.Same(variant, ManifestVariantResolver.ResolveVariant(manifest, "osx-arm64"));
        Assert.Equal("generalszh", Assert.Single(ManifestVariantResolver.EnumerateAllFiles(manifest)).RelativePath);
        Assert.Null(ManifestVariantResolver.ResolveVariant(manifest, "linux-x64"));
    }

    /// <summary>A variant whose runtime list is explicitly null in JSON is platform-neutral and never null.</summary>
    [Fact]
    public void ResolveVariant_NullRuntimeIdentifiersInJson_FallsBackAsNeutral()
    {
        const string json = """
            {
              "variants": [
                { "runtimeIdentifiers": ["win-x64"], "files": [{ "relativePath": "generals.exe" }] },
                { "runtimeIdentifiers": null, "files": [{ "relativePath": "assets.big" }] }
              ]
            }
            """;
        var manifest = JsonSerializer.Deserialize<ContentManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var neutral = ManifestVariantResolver.ResolveVariant(manifest, "osx-arm64");

        Assert.NotNull(neutral);
        Assert.Empty(neutral.RuntimeIdentifiers);
        Assert.True(neutral.SupportsRuntime("osx-arm64"));
        Assert.Equal("assets.big", Assert.Single(ManifestVariantResolver.ResolveFiles(manifest, "osx-arm64")).RelativePath);
    }

    /// <summary>Resolved files skip null entries in a variant and in the flat list.</summary>
    [Fact]
    public void ResolveFiles_SkipsNullEntries()
    {
        var variantManifest = new ContentManifest
        {
            Variants = [new ArtifactVariant { RuntimeIdentifiers = ["osx-arm64"], Files = [null!, File("generalszh")] }],
        };
        var flatManifest = new ContentManifest { Files = [File("generals.exe"), null!] };

        Assert.Equal("generalszh", Assert.Single(ManifestVariantResolver.ResolveFiles(variantManifest, "osx-arm64")).RelativePath);
        Assert.Equal("generals.exe", Assert.Single(ManifestVariantResolver.ResolveFiles(flatManifest)).RelativePath);
    }

    /// <summary>Declared lists keep the flat list first and preserve null lists and null variants.</summary>
    [Fact]
    public void GetDeclaredFileLists_PreservesOrderAndNullEntries()
    {
        var flat = new List<ManifestFile> { File("readme.txt") };
        var first = new ArtifactVariant { RuntimeIdentifiers = ["win-x64"], Files = [File("generals.exe")] };
        var nullFiles = new ArtifactVariant { RuntimeIdentifiers = ["osx-arm64"], Files = null! };
        var manifest = new ContentManifest { Files = flat, Variants = [first, null!, nullFiles] };

        var lists = ManifestVariantResolver.GetDeclaredFileLists(manifest);

        Assert.Equal(4, lists.Count);
        Assert.Same(flat, lists[0]);
        Assert.Same(first.Files, lists[1]);
        Assert.Null(lists[2]);
        Assert.Null(lists[3]);
    }

    private static ManifestFile File(string path, bool isExecutable = false) =>
        new() { RelativePath = path, IsExecutable = isExecutable };
}
