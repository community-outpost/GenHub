using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Root wrapper for Python ModBuilder configuration files.
/// </summary>
public sealed class PythonConfigRoot
{
    /// <summary>
    /// Gets or sets the bundles configuration.
    /// </summary>
    [JsonPropertyName("bundles")]
    public PythonBundlesConfig? Bundles { get; set; }
}

/// <summary>
/// Python bundles configuration containing items and packs.
/// </summary>
public sealed class PythonBundlesConfig
{
    /// <summary>
    /// Gets or sets the configuration version.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the bundle item name prefix.
    /// </summary>
    [JsonPropertyName("itemsPrefix")]
    public string ItemsPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bundle item name suffix.
    /// </summary>
    [JsonPropertyName("itemsSuffix")]
    public string ItemsSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bundle pack name prefix.
    /// </summary>
    [JsonPropertyName("packsPrefix")]
    public string PacksPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the bundle pack name suffix.
    /// </summary>
    [JsonPropertyName("packsSuffix")]
    public string PacksSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of bundle items.
    /// </summary>
    [JsonPropertyName("items")]
    public List<PythonBundleItem>? Items { get; set; }

    /// <summary>
    /// Gets or sets the list of bundle packs.
    /// </summary>
    [JsonPropertyName("packs")]
    public List<PythonBundlePack>? Packs { get; set; }
}

/// <summary>
/// Python bundle item configuration.
/// </summary>
public sealed class PythonBundleItem
{
    /// <summary>
    /// Gets or sets the bundle item name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item name prefix.
    /// </summary>
    [JsonPropertyName("namePrefix")]
    public string NamePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item name suffix.
    /// </summary>
    [JsonPropertyName("nameSuffix")]
    public string NameSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the item is packed into a BIG file.
    /// </summary>
    [JsonPropertyName("big")]
    public bool Big { get; set; } = true;

    /// <summary>
    /// Gets or sets the BIG file suffix.
    /// </summary>
    [JsonPropertyName("bigSuffix")]
    public string BigSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game language to apply on install.
    /// </summary>
    [JsonPropertyName("setGameLanguageOnInstall")]
    public string SetGameLanguageOnInstall { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the manifest file path for byte-for-byte reproducible BIG packing.
    /// </summary>
    [JsonPropertyName("manifestFile")]
    public string? ManifestFile { get; set; }

    /// <summary>
    /// Gets or sets the list of file groups.
    /// </summary>
    [JsonPropertyName("files")]
    public List<PythonBundleFileGroup>? Files { get; set; }

    /// <summary>
    /// Gets or sets the pre-build event configuration.
    /// </summary>
    [JsonPropertyName("onPreBuild")]
    public PythonBundleEvent? OnPreBuild { get; set; }

    /// <summary>
    /// Gets or sets the build event configuration.
    /// </summary>
    [JsonPropertyName("onBuild")]
    public PythonBundleEvent? OnBuild { get; set; }

    /// <summary>
    /// Gets or sets the post-build event configuration.
    /// </summary>
    [JsonPropertyName("onPostBuild")]
    public PythonBundleEvent? OnPostBuild { get; set; }
}

/// <summary>
/// Python bundle pack configuration.
/// </summary>
public sealed class PythonBundlePack
{
    /// <summary>
    /// Gets or sets the bundle pack name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the pack name prefix.
    /// </summary>
    [JsonPropertyName("namePrefix")]
    public string NamePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the pack name suffix.
    /// </summary>
    [JsonPropertyName("nameSuffix")]
    public string NameSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether build is allowed.
    /// </summary>
    [JsonPropertyName("allowBuild")]
    public bool AllowBuild { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether installation is allowed.
    /// </summary>
    [JsonPropertyName("allowInstall")]
    public bool AllowInstall { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the pack creates a BIG archive.
    /// </summary>
    [JsonPropertyName("big")]
    public bool? Big { get; set; }

    /// <summary>
    /// Gets or sets the BIG file suffix.
    /// </summary>
    [JsonPropertyName("bigSuffix")]
    public string BigSuffix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the output file path.
    /// </summary>
    [JsonPropertyName("outputFile")]
    public string? OutputFile { get; set; }

    /// <summary>
    /// Gets or sets the manifest file path.
    /// </summary>
    [JsonPropertyName("manifestFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ManifestFile { get; set; }

    /// <summary>
    /// Gets or sets the game language to apply on install.
    /// </summary>
    [JsonPropertyName("setGameLanguageOnInstall")]
    public string SetGameLanguageOnInstall { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of item names in the pack.
    /// </summary>
    [JsonPropertyName("itemNames")]
    public List<string>? ItemNames { get; set; }

    /// <summary>
    /// Gets or sets the pre-build event configuration.
    /// </summary>
    [JsonPropertyName("onPreBuild")]
    public PythonBundleEvent? OnPreBuild { get; set; }

    /// <summary>
    /// Gets or sets the release event configuration.
    /// </summary>
    [JsonPropertyName("onRelease")]
    public PythonBundleEvent? OnRelease { get; set; }

    /// <summary>
    /// Gets or sets the install event configuration.
    /// </summary>
    [JsonPropertyName("onInstall")]
    public PythonBundleEvent? OnInstall { get; set; }

    /// <summary>
    /// Gets or sets the run event configuration.
    /// </summary>
    [JsonPropertyName("onRun")]
    public PythonBundleEvent? OnRun { get; set; }

    /// <summary>
    /// Gets or sets the uninstall event configuration.
    /// </summary>
    [JsonPropertyName("onUninstall")]
    public PythonBundleEvent? OnUninstall { get; set; }
}

/// <summary>
/// Python file group with source/target mappings.
/// </summary>
public sealed class PythonBundleFileGroup
{
    /// <summary>
    /// Gets or sets the source parent directory.
    /// </summary>
    [JsonPropertyName("sourceParent")]
    public string SourceParent { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source file or directory path.
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the target destination path.
    /// </summary>
    [JsonPropertyName("target")]
    public string? Target { get; set; }

    /// <summary>
    /// Gets or sets the list of source patterns.
    /// </summary>
    [JsonPropertyName("sourceList")]
    public List<string>? SourceList { get; set; }

    /// <summary>
    /// Gets or sets the list of source-target pairs.
    /// </summary>
    [JsonPropertyName("sourceTargetList")]
    public List<PythonSourceTargetPair>? SourceTargetList { get; set; }

    /// <summary>
    /// Gets or sets the list of registry files.
    /// </summary>
    [JsonPropertyName("registryList")]
    public List<string>? RegistryList { get; set; }

    /// <summary>
    /// Gets or sets custom parameters for file processing.
    /// </summary>
    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; set; }

    /// <summary>
    /// Gets or sets exclusion marker lists.
    /// </summary>
    [JsonPropertyName("excludeMarkersList")]
    public List<List<string>>? ExcludeMarkersList { get; set; }
}

/// <summary>
/// Python source-target pair for file mappings.
/// </summary>
public sealed class PythonSourceTargetPair
{
    /// <summary>
    /// Gets or sets the source file path.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target file path.
    /// </summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;
}

/// <summary>
/// Python bundle event configuration.
/// </summary>
public sealed class PythonBundleEvent
{
    /// <summary>
    /// Gets or sets the script file to execute.
    /// </summary>
    [JsonPropertyName("script")]
    public string Script { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets optional arguments passed to the script.
    /// </summary>
    [JsonPropertyName("args")]
    public string? Args { get; set; }
}

/// <summary>
/// ModJsonFiles.json master configuration list.
/// </summary>
public sealed class PythonModJsonFilesConfig
{
    /// <summary>
    /// Gets or sets the build configuration section.
    /// </summary>
    [JsonPropertyName("build")]
    public PythonModJsonFilesBuild? Build { get; set; }
}

/// <summary>
/// ModJsonFiles build configuration containing file list.
/// </summary>
public sealed class PythonModJsonFilesBuild
{
    /// <summary>
    /// Gets or sets the configuration version.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the list of JSON configuration file paths.
    /// </summary>
    [JsonPropertyName("files")]
    public List<string>? Files { get; set; }
}

/// <summary>
/// ModFolders.json folders configuration.
/// </summary>
public sealed class PythonModFoldersConfig
{
    /// <summary>
    /// Gets or sets the folders configuration data.
    /// </summary>
    [JsonPropertyName("folders")]
    public PythonModFoldersData? Folders { get; set; }
}

/// <summary>
/// ModFolders configuration containing directory paths.
/// </summary>
public sealed class PythonModFoldersData
{
    /// <summary>
    /// Gets or sets the configuration version.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the build output directory.
    /// </summary>
    [JsonPropertyName("buildDir")]
    public string? BuildDir { get; set; }

    /// <summary>
    /// Gets or sets the release output directory.
    /// </summary>
    [JsonPropertyName("releaseDir")]
    public string? ReleaseDir { get; set; }

    /// <summary>
    /// Gets or sets the target game directory.
    /// </summary>
    [JsonPropertyName("gameDir")]
    public string? GameDir { get; set; }
}

/// <summary>
/// Simplified configuration format used in sample projects.
/// </summary>
public sealed class SimplifiedConfigRoot
{
    /// <summary>
    /// Gets or sets the list of simplified bundle items.
    /// </summary>
    [JsonPropertyName("BundleItems")]
    public List<SimplifiedBundleItem>? BundleItems { get; set; }

    /// <summary>
    /// Gets or sets the list of simplified bundle packs.
    /// </summary>
    [JsonPropertyName("BundlePacks")]
    public List<SimplifiedBundlePack>? BundlePacks { get; set; }

    /// <summary>
    /// Gets or sets the list of simplified bundle manifest definitions.
    /// </summary>
    [JsonPropertyName("BundleManifests")]
    public List<SimplifiedBundleManifest>? BundleManifests { get; set; }
}

/// <summary>
/// Simplified bundle manifest definition grouping packs into one content manifest.
/// </summary>
public sealed class SimplifiedBundleManifest
{
    /// <summary>
    /// Gets or sets the manifest name.
    /// </summary>
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the manifest version string.
    /// </summary>
    [JsonPropertyName("Version")]
    public string? Version { get; set; }

    /// <summary>
    /// Gets or sets the publisher identifier.
    /// </summary>
    [JsonPropertyName("Publisher")]
    public string? Publisher { get; set; }

    /// <summary>
    /// Gets or sets the manifest description.
    /// </summary>
    [JsonPropertyName("Description")]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the names of the bundle packs linked into this manifest.
    /// </summary>
    [JsonPropertyName("Packs")]
    public List<string>? Packs { get; set; }
}

/// <summary>
/// Simplified bundle item with wildcard patterns.
/// </summary>
public sealed class SimplifiedBundleItem
{
    /// <summary>
    /// Gets or sets the bundle item name.
    /// </summary>
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the source file patterns or paths.
    /// </summary>
    [JsonPropertyName("SourceFiles")]
    public List<string>? SourceFiles { get; set; }

    /// <summary>
    /// Gets or sets the base source directory.
    /// </summary>
    [JsonPropertyName("BaseDir")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseDir { get; set; }

    /// <summary>
    /// Gets or sets the target directory.
    /// </summary>
    [JsonPropertyName("TargetDir")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetDir { get; set; }

    /// <summary>
    /// Gets or sets the output conversion format.
    /// </summary>
    [JsonPropertyName("OutputFormat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputFormat { get; set; }

    /// <summary>
    /// Gets or sets the image compression type.
    /// </summary>
    [JsonPropertyName("Compression")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Compression { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to generate mipmaps.
    /// </summary>
    [JsonPropertyName("GenerateMipmaps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool GenerateMipmaps { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item is packed into a BIG archive.
    /// </summary>
    [JsonPropertyName("Big")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Big { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether conversion is skipped.
    /// </summary>
    [JsonPropertyName("NoConvert")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool NoConvert { get; set; }

    /// <summary>
    /// Gets or sets the manifest file path for byte-for-byte reproducible BIG packing.
    /// </summary>
    [JsonPropertyName("ManifestFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ManifestFile { get; set; }

    /// <summary>
    /// Gets or sets the item description.
    /// </summary>
    [JsonPropertyName("Description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the prefix added to the bundle item name.
    /// </summary>
    [JsonPropertyName("NamePrefix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NamePrefix { get; set; }

    /// <summary>
    /// Gets or sets the suffix added to the bundle item name.
    /// </summary>
    [JsonPropertyName("NameSuffix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NameSuffix { get; set; }

    /// <summary>
    /// Gets or sets the game language to set on installation.
    /// </summary>
    [JsonPropertyName("SetGameLanguageOnInstall")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SetGameLanguageOnInstall { get; set; }

    /// <summary>
    /// Gets or sets the suffix added to the .big archive name.
    /// </summary>
    [JsonPropertyName("BigSuffix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BigSuffix { get; set; }
}

/// <summary>
/// Simplified bundle pack format used in sample projects.
/// </summary>
public sealed class SimplifiedBundlePack
{
    /// <summary>
    /// Gets or sets the bundle pack name.
    /// </summary>
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the list of items or wildcard patterns included in the pack.
    /// </summary>
    [JsonPropertyName("Items")]
    public List<string>? Items { get; set; }

    /// <summary>
    /// Gets or sets the list of explicit item names included in the pack.
    /// </summary>
    [JsonPropertyName("ItemNames")]
    public List<string>? ItemNames { get; set; }

    /// <summary>
    /// Gets or sets the output archive file name.
    /// </summary>
    [JsonPropertyName("OutputFile")]
    public string? OutputFile { get; set; }

    /// <summary>
    /// Gets or sets the manifest file path.
    /// </summary>
    [JsonPropertyName("ManifestFile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ManifestFile { get; set; }

    /// <summary>
    /// Gets or sets the game language to apply on install.
    /// </summary>
    [JsonPropertyName("SetGameLanguageOnInstall")]
    public string SetGameLanguageOnInstall { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the pack is packed into a BIG archive.
    /// </summary>
    [JsonPropertyName("Big")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Big { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether building this pack is allowed.
    /// </summary>
    [JsonPropertyName("AllowBuild")]
    public bool? AllowBuild { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether installing this pack is allowed.
    /// </summary>
    [JsonPropertyName("AllowInstall")]
    public bool? AllowInstall { get; set; } = true;

    /// <summary>
    /// Gets or sets the pack description.
    /// </summary>
    [JsonPropertyName("Description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the prefix added to the bundle pack name.
    /// </summary>
    [JsonPropertyName("NamePrefix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NamePrefix { get; set; }

    /// <summary>
    /// Gets or sets the suffix added to the bundle pack name.
    /// </summary>
    [JsonPropertyName("NameSuffix")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NameSuffix { get; set; }
}
