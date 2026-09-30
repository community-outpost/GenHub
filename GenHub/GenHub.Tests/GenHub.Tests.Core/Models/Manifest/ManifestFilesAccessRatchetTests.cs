using GenHub.Core.Models.Manifest;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Shrink-only ratchet over production code that reads <see cref="ContentManifest.Files"/>
/// directly instead of resolving the host variant through
/// <see cref="ManifestVariantResolver.ResolveFiles"/>. See community-outpost/GenHub#321.
/// <para>
/// The scan works on compiled IL rather than source text. Every production assembly in
/// the test output is loaded, and each method body is walked for a call, delegate load or
/// token load whose operand resolves to the <c>ContentManifest.Files</c> getter. That
/// matches the property by identity, so it ignores the many unrelated <c>.Files</c>
/// members (drag-and-drop data, project snapshots, <see cref="ArtifactVariant.Files"/>),
/// and it still sees reads hidden in lambdas, async state machines, null-conditional
/// access and pattern matching. Compiler-generated types are attributed to the
/// outermost type that declares them, which is the type named in the allowlist.
/// </para>
/// <para>
/// Only assemblies this test project references are scanned. The platform hosts
/// (<c>GenHub.Windows</c>, <c>GenHub.Linux</c>, <c>GenHub.MacOS</c>) are not loaded
/// here, and hold no direct reads today.
/// </para>
/// </summary>
public class ManifestFilesAccessRatchetTests
{
    /// <summary>
    /// Types that read the flat file list legitimately: the resolver itself, the manifest
    /// model, code that compares the declared shape with the resolved one, and code that
    /// authors manifests. These stay allowed once the migration is complete.
    /// </summary>
    private static readonly string[] ManifestOwners =
    [
        "GenHub.Core.Models.Manifest.ContentManifest",
        "GenHub.Core.Models.Manifest.ManifestVariantResolver",
        "GenHub.Core.Models.Workspace.ContentHotswapClassification",
        "GenHub.Features.Content.Services.CommunityOutpost.CommunityOutpostManifestFactory",
        "GenHub.Features.Content.Services.GenLauncher.GenLauncherManifestFactory",
        "GenHub.Features.Content.Services.Publishers.ModDBManifestFactory",
        "GenHub.Features.Content.Services.Publishers.SuperHackersManifestFactory",
        "GenHub.Features.Manifest.ContentManifestBuilder",
        "GenHub.Features.Manifest.ManifestGenerationService",
    ];

    /// <summary>
    /// Types still pending migration to <see cref="ManifestVariantResolver.ResolveFiles"/>.
    /// <para>
    /// This list is SHRINK-ONLY. Never add an entry: resolve the host variant instead.
    /// When a type stops reading <c>Files</c> directly the ratchet fails with a "remove
    /// me" message until its entry is deleted, so the list can only get shorter.
    /// </para>
    /// </summary>
    private static readonly string[] PendingMigration =
    [
        "GenHub.Core.Extensions.Storage.CasServiceExtensions",
        "GenHub.Core.Extensions.WorkspaceConfigurationExtensions",
        "GenHub.Core.Helpers.ManifestHelper",
        "GenHub.Core.Services.Content.LocalContentService",
        "GenHub.Features.Content.Services.Catalog.GenericCatalogResolver",
        "GenHub.Features.Content.Services.CommunityOutpost.CommunityOutpostDeliverer",
        "GenHub.Features.Content.Services.CommunityOutpost.CommunityOutpostResolver",
        "GenHub.Features.Content.Services.ContentDiscoverers.DownloadedContentDiscoverer",
        "GenHub.Features.Content.Services.ContentDiscoverers.FileSystemDiscoverer",
        "GenHub.Features.Content.Services.ContentProviders.BaseContentProvider",
        "GenHub.Features.Content.Services.ContentResolvers.CsvResolver",
        "GenHub.Features.Content.Services.ContentResolvers.LocalManifestResolver",
        "GenHub.Features.Content.Services.ContentStorageService",
        "GenHub.Features.Content.Services.ContentValidator",
        "GenHub.Features.Content.Services.GeneralsOnline.GeneralsOnlineDeliverer",
        "GenHub.Features.Content.Services.GeneralsOnline.GeneralsOnlineProfileReconciler",
        "GenHub.Features.Content.Services.GenLauncher.GenLauncherDeliverer",
        "GenHub.Features.Content.Services.GenLauncher.GenLauncherResolver",
        "GenHub.Features.Content.Services.GitHub.GitHubContentDeliverer",
        "GenHub.Features.Content.Services.InstallationInstructionsService",
        "GenHub.Features.Downloads.Services.ContentStateService",
        "GenHub.Features.GameClients.GameClientDetector",
        "GenHub.Features.GameProfiles.Services.ProfileContentService",
        "GenHub.Features.GameProfiles.Services.ProfileSharingService",
        "GenHub.Features.Launching.GameLauncher",
        "GenHub.Features.Manifest.ContentManifestPool",
        "GenHub.Features.Manifest.ManifestProvider",
        "GenHub.Features.Manifest.SteamManifestPatcher",
        "GenHub.Features.Tools.GenHotkeys.ViewModels.GenHotkeysViewModel",
        "GenHub.Features.Tools.MapManager.Services.MapPackService",
        "GenHub.Features.Tools.MapManager.ViewModels.MapManagerViewModel",
        "GenHub.Features.Workspace.Strategies.FullCopyStrategy",
        "GenHub.Features.Workspace.Strategies.HardLinkStrategy",
        "GenHub.Features.Workspace.Strategies.HybridCopySymlinkStrategy",
        "GenHub.Features.Workspace.Strategies.SymlinkOnlyStrategy",
        "GenHub.Features.Workspace.Strategies.WorkspaceCompatibilityHelper",
        "GenHub.Features.Workspace.Strategies.WorkspaceStrategyBase`1",
        "GenHub.Features.Workspace.WorkspaceReconciler",
    ];

    private static readonly MethodInfo FilesGetter =
        typeof(ContentManifest).GetProperty(nameof(ContentManifest.Files))!.GetMethod!;

    private static readonly Dictionary<short, OperandType> OpCodeOperands = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value, o => o.OperandType);

    /// <summary>
    /// Fails when a type outside the allowlist reads <see cref="ContentManifest.Files"/>
    /// directly, or when an allowlisted type no longer does.
    /// </summary>
    [Fact]
    public void DirectFilesReads_MatchShrinkOnlyAllowlist()
    {
        var readers = FindDirectFilesReaders();
        var allowed = ManifestOwners.Concat(PendingMigration).ToHashSet(StringComparer.Ordinal);

        var newReaders = readers.Where(r => !allowed.Contains(r)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var newReadersMessage =
            "New direct reads of ContentManifest.Files: "
            + string.Join(", ", newReaders)
            + ". Resolve the host variant with ManifestVariantResolver.ResolveFiles instead; "
            + "do NOT add entries to PendingMigration.";
        Assert.True(newReaders.Count == 0, newReadersMessage);

        var migrated = allowed.Where(a => !readers.Contains(a)).OrderBy(a => a, StringComparer.Ordinal).ToList();
        var migratedMessage =
            "These types no longer read ContentManifest.Files directly: "
            + string.Join(", ", migrated)
            + ". Remove them from the allowlist so it only shrinks.";
        Assert.True(migrated.Count == 0, migratedMessage);
    }

    /// <summary>
    /// Guards the scanner itself: a known reader must be found, and a type that only
    /// reads through the resolver must not be.
    /// </summary>
    [Fact]
    public void Scanner_FindsKnownReaderAndIgnoresResolverConsumers()
    {
        var readers = FindDirectFilesReaders();

        Assert.Contains(typeof(ManifestVariantResolver).FullName!, readers);
        Assert.DoesNotContain("GenHub.Features.GameProfiles.Services.ProfileVerificationFileSetService", readers);
    }

    private static HashSet<string> FindDirectFilesReaders()
    {
        var readers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assembly in LoadProductionAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                foreach (var method in DeclaredMethods(type))
                {
                    if (ReadsFiles(method))
                    {
                        readers.Add(OwningType(type).FullName!);
                    }
                }
            }
        }

        return readers;
    }

    private static List<Assembly> LoadProductionAssemblies()
    {
        var assemblies = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "GenHub*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !name!.StartsWith("GenHub.Tests", StringComparison.Ordinal))
            .Select(name => Assembly.Load(name!))
            .ToList();

        // The scan is only meaningful if the assemblies that hold the content pipeline
        // were found; an empty scan must not pass as "no direct reads".
        Assert.Contains(typeof(ContentManifest).Assembly, assemblies);
        Assert.Contains(typeof(GenHub.Features.Content.Services.ContentDeliverers.FileSystemDeliverer).Assembly, assemblies);

        return assemblies;
    }

    private static IEnumerable<MethodBase> DeclaredMethods(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        return type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags));
    }

    private static Type OwningType(Type type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type;
    }

    private static bool ReadsFiles(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            return false;
        }

        var position = 0;
        while (position < il.Length)
        {
            short value = il[position++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[position++]));
            }

            var operandType = OpCodeOperands[value];
            if (operandType is OperandType.InlineMethod or OperandType.InlineTok
                && IsFilesGetter(method, BitConverter.ToInt32(il, position)))
            {
                return true;
            }

            position += OperandSize(operandType, il, position);
        }

        return false;
    }

    private static bool IsFilesGetter(MethodBase method, int token)
    {
        try
        {
            var typeArguments = method.DeclaringType is { IsGenericType: true } declaring
                ? declaring.GetGenericArguments()
                : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

            return method.Module.ResolveMember(token, typeArguments, methodArguments) is MethodInfo resolved
                && resolved.Module == FilesGetter.Module
                && resolved.MetadataToken == FilesGetter.MetadataToken;
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or TypeLoadException or BadImageFormatException)
        {
            // A member whose declaring assembly is not loadable here (a platform-only
            // API) cannot be ContentManifest.Files, which lives in a loaded assembly.
            return false;
        }
    }

    private static int OperandSize(OperandType operandType, byte[] il, int position) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, position) * 4),
        _ => 4,
    };
}
