using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System.Collections.Generic;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Builds manifests that declare one variant for the host and one for another platform,
/// with an empty root <see cref="ContentManifest.Files"/> list.
/// <para>
/// The ingestion gate keeps such manifests out of production today, so consumer tests
/// construct them directly. The host variant is keyed to the runtime running the tests,
/// which keeps the tests meaningful on every CI platform.
/// </para>
/// </summary>
internal static class VariantManifestFixture
{
    /// <summary>
    /// Gets the runtime identifier the host variant targets.
    /// </summary>
    public static string HostRuntimeIdentifier => ManifestVariantResolver.CurrentRuntimeIdentifier;

    /// <summary>
    /// Gets a runtime identifier that never matches the host.
    /// </summary>
    public static string ForeignRuntimeIdentifier =>
        HostRuntimeIdentifier == "win-x64" ? "osx-arm64" : "win-x64";

    /// <summary>
    /// Creates a manifest whose only files live in a host variant and a foreign variant.
    /// The foreign variant is declared first so that selection cannot pass by order.
    /// </summary>
    /// <param name="hostFiles">Files of the variant that matches the host.</param>
    /// <param name="foreignFiles">Files of the variant for another platform.</param>
    /// <returns>The manifest.</returns>
    public static ContentManifest Create(List<ManifestFile> hostFiles, List<ManifestFile> foreignFiles) => new()
    {
        Id = ManifestId.Create("1.0.test.gameclient.variants"),
        Name = "Variant Test Client",
        Version = "1.0",
        ContentType = ContentType.GameClient,
        TargetGame = GameType.ZeroHour,
        Files = [],
        Variants =
        [
            new ArtifactVariant { RuntimeIdentifiers = [ForeignRuntimeIdentifier], Files = foreignFiles },
            new ArtifactVariant { RuntimeIdentifiers = [HostRuntimeIdentifier], Files = hostFiles },
        ],
    };
}
