using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Utilities;
using GenHub.Features.GameProfiles.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Closes the loop between the manifest model and a real launch.
/// <para>
/// The variant and entry-point model is otherwise only exercised against hand-built
/// manifests. This builds a manifest from an actual native install, resolves the entry
/// point the way the workspace does, and launches whatever comes out — so a resolver
/// that picks the wrong file fails here rather than in production.
/// </para>
/// <para>
/// Skipped unless a native install is present. Set <c>GENHUB_NATIVE_CLIENT_DIR</c> to
/// override the default location.
/// </para>
/// </summary>
[Collection(NativeClientLaunchCollection.Name)]
public class NativeClientManifestLaunchTests
{
    private readonly GameProcessManager _processManager = new(NullLogger<GameProcessManager>.Instance);

    /// <summary>
    /// Builds a manifest describing a real install and launches the entry point the
    /// resolver selects.
    /// <para>
    /// The manifest deliberately includes the engine's dynamic libraries. That is the
    /// shape that used to break resolution: several files qualify as executable, so
    /// picking the first one was picking by enumeration order.
    /// </para>
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ManifestResolvedEntryPoint_LaunchesTheRealEngine()
    {
        var installDirectory = NativeClientFixture.Directory;
        if (installDirectory is null)
        {
            return;
        }

        var manifest = BuildManifestFromInstall(installDirectory);

        // Sanity-check the fixture actually reproduces the ambiguous case; otherwise this
        // test would pass for the wrong reason.
        Assert.True(
            manifest.Variants.Single().Files.Count(f => f.IsExecutable) >= 1,
            "Expected the install to contain at least one file requiring execute permission.");

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);

        Assert.True(resolution.Success, resolution.ToString());
        Assert.Equal(NativeClientFixture.BinaryName, resolution.RelativePath);

        var configuration = new GameLaunchConfiguration
        {
            ExecutablePath = Path.Combine(installDirectory, resolution.RelativePath!),
            WorkingDirectory = installDirectory,
            Arguments = new() { ["-win"] = string.Empty },
        };

        var result = await _processManager.StartProcessAsync(configuration);
        Assert.True(result.Success, $"Launch failed: {string.Join(" ", result.Errors)}");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12));
            var info = await _processManager.GetProcessInfoAsync(result.Data!.ProcessId);
            Assert.True(info.Success, "The engine exited during initialisation.");
        }
        finally
        {
            await _processManager.TerminateProcessAsync(result.Data!.ProcessId);
        }
    }

    /// <summary>
    /// A manifest whose variant does not target this runtime must resolve to nothing,
    /// so a Windows-only client is never offered on macOS.
    /// </summary>
    [Fact]
    public void ManifestForAnotherRuntime_IsNotOfferedHere()
    {
        var installDirectory = NativeClientFixture.Directory;
        if (installDirectory is null)
        {
            return;
        }

        var manifest = BuildManifestFromInstall(installDirectory);
        manifest.Variants.Single().RuntimeIdentifiers = ["win-x64"];

        Assert.False(ManifestVariantResolver.SupportsRuntime(manifest));
        Assert.Empty(ManifestVariantResolver.ResolveFiles(manifest));
    }

    /// <summary>
    /// Builds a single-variant manifest describing the engine binary and its libraries,
    /// classified by the shared rules rather than by hand.
    /// </summary>
    /// <param name="installDirectory">The native install directory.</param>
    /// <returns>A manifest targeting the current runtime.</returns>
    private static ContentManifest BuildManifestFromInstall(string installDirectory)
    {
        var engineFiles = new List<ManifestFile>();

        foreach (var path in Directory.EnumerateFiles(installDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);

            // The engine and its bundled libraries; retail archives are the user's own
            // content and belong to the installation, not the client manifest.
            if (name != NativeClientFixture.BinaryName && !name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            engineFiles.Add(new ManifestFile
            {
                RelativePath = name,
                IsExecutable = ExecutableFileClassifier.RequiresExecutePermission(name),
            });
        }

        return new ContentManifest
        {
            Name = "Native Zero Hour (BGFX)",
            Variants =
            [
                new ArtifactVariant
                {
                    RuntimeIdentifiers = [ManifestVariantResolver.CurrentRuntimeIdentifier],
                    EntryPoint = NativeClientFixture.BinaryName,
                    Files = engineFiles,
                },
            ],
        };
    }
}
