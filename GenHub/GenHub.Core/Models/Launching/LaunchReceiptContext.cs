using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Launching;

/// <summary>
/// Everything a launch supplies for a receipt to be recorded from.
/// </summary>
public class LaunchReceiptContext
{
    /// <summary>Gets or sets the launch identifier.</summary>
    public string LaunchId { get; set; } = string.Empty;

    /// <summary>Gets or sets the profile being launched.</summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>Gets or sets the game client identifier, when the profile declares one.</summary>
    public string? GameClientId { get; set; }

    /// <summary>Gets or sets the game being launched.</summary>
    public GameType GameType { get; set; }

    /// <summary>Gets or sets the workspace the launch runs from.</summary>
    public string WorkspaceId { get; set; } = string.Empty;

    /// <summary>Gets or sets the workspace directory the receipt is written into.</summary>
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the executable being started.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the working directory the process is started in.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the environment GenHub built for the child process — retail archive
    /// roots plus profile-defined variables, never the inherited process environment.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Gets or sets the resolved variant and entry-point identity, when a game client
    /// manifest is part of the launch.
    /// </summary>
    public LaunchReceiptVariant? Variant { get; set; }

    /// <summary>Gets or sets the manifest identifiers resolved for the launch.</summary>
    public IReadOnlyList<string> ManifestIds { get; set; } = [];

    /// <summary>Gets or sets the manifest versions resolved for the launch, keyed by manifest identifier.</summary>
    public IReadOnlyDictionary<string, string> ManifestVersions { get; set; } = new Dictionary<string, string>();
}
