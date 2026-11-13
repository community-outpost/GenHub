using System;
using System.Collections.Generic;
using System.Globalization;
using YamlDotNet.Serialization;

namespace GenHub.Core.Models.GenLauncher;

/// <summary>
/// Represents a child modification or version manifest in GenLauncher.
/// </summary>
public class GenLauncherVersionManifest
{
    /// <summary>Gets or sets the modification name.</summary>
    [YamlMember(Alias = "Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the version string.</summary>
    [YamlMember(Alias = "Version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>Gets or sets the raw modification type (Mod, Addon, Patch, etc.).</summary>
    [YamlMember(Alias = "ModificationType")]
    public string? ModificationType { get; set; }

    /// <summary>Gets or sets direct download link (e.g. Dropbox, OneDrive, or HTTP mirror).</summary>
    [YamlMember(Alias = "SimpleDownloadLink")]
    public string? SimpleDownloadLink { get; set; }

    /// <summary>Gets or sets the UI image source link (thumbnail/icon).</summary>
    [YamlMember(Alias = "UIImageSourceLink")]
    public string? UIImageSourceLink { get; set; }

    /// <summary>Gets or sets the Discord community link.</summary>
    [YamlMember(Alias = "DiscordLink")]
    public string? DiscordLink { get; set; }

    /// <summary>Gets or sets the ModDB page link.</summary>
    [YamlMember(Alias = "ModDBLink")]
    public string? ModDBLink { get; set; }

    /// <summary>Gets or sets the news / changelog link.</summary>
    [YamlMember(Alias = "NewsLink")]
    public string? NewsLink { get; set; }

    /// <summary>Gets or sets the support/donation link.</summary>
    [YamlMember(Alias = "SupportLink")]
    public string? SupportLink { get; set; }

    /// <summary>Gets or sets the name of the parent modification this item depends on.</summary>
    [YamlMember(Alias = "DependenceName")]
    public string? DependenceName { get; set; }

    /// <summary>Gets or sets a value indicating whether this version is deprecated.</summary>
    [YamlMember(Alias = "Deprecated")]
    public bool Deprecated { get; set; }

    /// <summary>Gets or sets the S3 host endpoint (e.g. gen.insave.ovh:9000).</summary>
    [YamlMember(Alias = "S3HostLink")]
    public string? S3HostLink { get; set; }

    /// <summary>Gets or sets the S3 bucket name.</summary>
    [YamlMember(Alias = "S3BucketName")]
    public string? S3BucketName { get; set; }

    /// <summary>Gets or sets the S3 folder name / prefix.</summary>
    [YamlMember(Alias = "S3FolderName")]
    public string? S3FolderName { get; set; }

    /// <summary>Gets or sets the S3 public key.</summary>
    [YamlMember(Alias = "S3HostPublicKey")]
    public string? S3HostPublicKey { get; set; }

    /// <summary>Gets or sets the S3 secret key.</summary>
    [YamlMember(Alias = "S3HostSecretKey")]
    public string? S3HostSecretKey { get; set; }

    /// <summary>Gets or sets additional network info.</summary>
    [YamlMember(Alias = "NetworkInfo")]
    public string? NetworkInfo { get; set; }

    /// <summary>Gets or sets colors/theming information.</summary>
    [YamlMember(Alias = "ColorsInformation")]
    public Dictionary<string, string>? ColorsInformation { get; set; }

    /// <summary>
    /// Gets the resolved modification type enum.
    /// </summary>
    /// <returns>The resolved modification type.</returns>
    public GenLauncherModificationType GetParsedType()
    {
        if (int.TryParse(ModificationType, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intType) &&
            Enum.IsDefined(typeof(GenLauncherModificationType), intType))
        {
            return (GenLauncherModificationType)intType;
        }

        if (Enum.TryParse<GenLauncherModificationType>(ModificationType, true, out var enumType) &&
            Enum.IsDefined(typeof(GenLauncherModificationType), enumType))
        {
            return enumType;
        }

        return GenLauncherModificationType.Mod;
    }
}
