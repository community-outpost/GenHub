using System;
using System.Collections.Generic;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;

namespace GenHub.Core.Models.GameProfile
{
    /// <summary>
    /// Represents a game profile configuration.
    /// </summary>
    public class GameProfile : IGameProfile
    {
        /// <summary>
        /// Gets or sets the unique profile ID.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the profile name.
        /// </summary>
        required public string Name { get; set; }

        /// <summary>
        /// Gets or sets the profile description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the game installation ID.
        /// </summary>
        required public string GameInstallationId { get; set; }

        /// <summary>
        /// Gets or sets the game version ID.
        /// </summary>
        required public string GameVersionId { get; set; }

        /// <summary>
        /// Gets or sets the game version object.
        /// </summary>
        required public GameVersion GameVersion { get; set; }

        /// <summary>
        /// Gets or sets the list of enabled content IDs.
        /// </summary>
        public List<string> EnabledContentIds { get; set; } = new();

        /// <summary>
        /// Gets or sets the preferred workspace strategy.
        /// </summary>
        public WorkspaceStrategy PreferredStrategy { get; set; }

        /// <summary>
        /// Gets or sets the launch arguments.
        /// </summary>
        public Dictionary<string, string> LaunchArguments { get; set; } = new();

        /// <summary>
        /// Gets or sets the environment variables.
        /// </summary>
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

        /// <summary>
        /// Gets or sets the custom executable path.
        /// </summary>
        public string? CustomExecutablePath { get; set; }

        /// <summary>
        /// Gets or sets the working directory.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Gets or sets the UTC creation time.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the last launched time.
        /// </summary>
        public DateTime LastLaunchedAt { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this profile is active.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Gets or sets the icon path.
        /// </summary>
        public string? IconPath { get; set; }

        /// <summary>
        /// Gets or sets the theme color.
        /// </summary>
        public string? ThemeColor { get; set; }

        /// <summary>
        /// Gets the version string from the game version.
        /// </summary>
        public string Version => GameVersion.Version;

        /// <summary>
        /// Gets the executable path for the profile.
        /// </summary>
        public string ExecutablePath => CustomExecutablePath ?? GameVersion.ExecutablePath;
    }
}
