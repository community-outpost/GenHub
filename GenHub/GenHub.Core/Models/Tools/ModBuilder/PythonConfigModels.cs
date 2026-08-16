using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Root wrapper for Python ModBuilder configuration files.
/// </summary>
public sealed class PythonConfigRoot
{
    [JsonPropertyName("bundles")]
    public PythonBundlesConfig? Bundles { get; set; }
}

/// <summary>
/// Python bundles configuration containing items and packs.
/// </summary>
public sealed class PythonBundlesConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("itemsPrefix")]
    public string ItemsPrefix { get; set; } = string.Empty;

    [JsonPropertyName("itemsSuffix")]
    public string ItemsSuffix { get; set; } = string.Empty;

    [JsonPropertyName("packsPrefix")]
    public string PacksPrefix { get; set; } = string.Empty;

    [JsonPropertyName("packsSuffix")]
    public string PacksSuffix { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<PythonBundleItem>? Items { get; set; }

    [JsonPropertyName("packs")]
    public List<PythonBundlePack>? Packs { get; set; }
}

/// <summary>
/// Python bundle item configuration.
/// </summary>
public sealed class PythonBundleItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("namePrefix")]
    public string NamePrefix { get; set; } = string.Empty;

    [JsonPropertyName("nameSuffix")]
    public string NameSuffix { get; set; } = string.Empty;

    [JsonPropertyName("big")]
    public bool Big { get; set; } = true;

    [JsonPropertyName("bigSuffix")]
    public string BigSuffix { get; set; } = string.Empty;

    [JsonPropertyName("setGameLanguageOnInstall")]
    public string SetGameLanguageOnInstall { get; set; } = string.Empty;

    [JsonPropertyName("files")]
    public List<PythonBundleFileGroup>? Files { get; set; }

    [JsonPropertyName("onPreBuild")]
    public PythonBundleEvent? OnPreBuild { get; set; }

    [JsonPropertyName("onBuild")]
    public PythonBundleEvent? OnBuild { get; set; }

    [JsonPropertyName("onPostBuild")]
    public PythonBundleEvent? OnPostBuild { get; set; }
}

/// <summary>
/// Python bundle pack configuration.
/// </summary>
public sealed class PythonBundlePack
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("namePrefix")]
    public string NamePrefix { get; set; } = string.Empty;

    [JsonPropertyName("nameSuffix")]
    public string NameSuffix { get; set; } = string.Empty;

    [JsonPropertyName("allowBuild")]
    public bool AllowBuild { get; set; }

    [JsonPropertyName("allowInstall")]
    public bool AllowInstall { get; set; }

    [JsonPropertyName("setGameLanguageOnInstall")]
    public string SetGameLanguageOnInstall { get; set; } = string.Empty;

    [JsonPropertyName("itemNames")]
    public List<string>? ItemNames { get; set; }

    [JsonPropertyName("onPreBuild")]
    public PythonBundleEvent? OnPreBuild { get; set; }

    [JsonPropertyName("onRelease")]
    public PythonBundleEvent? OnRelease { get; set; }

    [JsonPropertyName("onInstall")]
    public PythonBundleEvent? OnInstall { get; set; }

    [JsonPropertyName("onRun")]
    public PythonBundleEvent? OnRun { get; set; }

    [JsonPropertyName("onUninstall")]
    public PythonBundleEvent? OnUninstall { get; set; }
}

/// <summary>
/// Python file group with source/target mappings.
/// </summary>
public sealed class PythonBundleFileGroup
{
    [JsonPropertyName("sourceParent")]
    public string SourceParent { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("sourceList")]
    public List<string>? SourceList { get; set; }

    [JsonPropertyName("sourceTargetList")]
    public List<PythonSourceTargetPair>? SourceTargetList { get; set; }

    [JsonPropertyName("registryList")]
    public List<string>? RegistryList { get; set; }

    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; set; }
}

/// <summary>
/// Python source-target pair for file mappings.
/// </summary>
public sealed class PythonSourceTargetPair
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;
}

/// <summary>
/// Python bundle event configuration.
/// </summary>
public sealed class PythonBundleEvent
{
    [JsonPropertyName("script")]
    public string Script { get; set; } = string.Empty;

    [JsonPropertyName("args")]
    public string? Args { get; set; }
}

/// <summary>
/// Simplified configuration format used in sample projects.
/// </summary>
public sealed class SimplifiedConfigRoot
{
    [JsonPropertyName("BundleItems")]
    public List<SimplifiedBundleItem>? BundleItems { get; set; }

    [JsonPropertyName("BundlePacks")]
    public List<SimplifiedBundlePack>? BundlePacks { get; set; }
}

/// <summary>
/// Simplified bundle item with wildcard patterns.
/// </summary>
public sealed class SimplifiedBundleItem
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("SourceFiles")]
    public List<string>? SourceFiles { get; set; }

    [JsonPropertyName("OutputFormat")]
    public string? OutputFormat { get; set; }

    [JsonPropertyName("Compression")]
    public string? Compression { get; set; }

    [JsonPropertyName("GenerateMipmaps")]
    public bool GenerateMipmaps { get; set; }
}

/// <summary>
/// Simplified bundle pack format used in sample projects.
/// </summary>
public sealed class SimplifiedBundlePack
{
    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Items")]
    public List<string>? Items { get; set; }

    [JsonPropertyName("ItemNames")]
    public List<string>? ItemNames { get; set; }

    [JsonPropertyName("OutputFile")]
    public string? OutputFile { get; set; }

    [JsonPropertyName("AllowBuild")]
    public bool? AllowBuild { get; set; }

    [JsonPropertyName("AllowInstall")]
    public bool? AllowInstall { get; set; }
}
