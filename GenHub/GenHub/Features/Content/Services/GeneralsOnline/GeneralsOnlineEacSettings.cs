using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using System;
using System.IO;
using System.Text.Json;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// The Easy Anti-Cheat bootstrapper settings shipped inside a Generals Online package.
/// The bootstrapper starts the binary named here, so the manifest factory reads the
/// same file when declaring the package's launch relationship.
/// </summary>
/// <param name="Executable">The game binary file name the bootstrapper starts.</param>
/// <param name="ProductId">The Epic Online Services product ID, when the settings carry one.</param>
public sealed record GeneralsOnlineEacSettings(string Executable, string? ProductId)
{
    /// <summary>
    /// Normalizes an executable path or file name from settings to just its file name.
    /// Handles forward/back slashes and whitespace.
    /// </summary>
    /// <param name="executable">The executable path or file name to normalize.</param>
    /// <returns>The normalized file name.</returns>
    public static string NormalizeExecutableName(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return string.Empty;
        }

        var normalized = executable.Trim().Replace('\\', '/');
        return Path.GetFileName(normalized);
    }

    /// <summary>
    /// Reads the bootstrapper settings from a package root.
    /// </summary>
    /// <param name="packageRoot">The extracted package directory.</param>
    /// <param name="settings">The parsed settings, or <c>null</c> when absent or unusable.</param>
    /// <returns><c>true</c> when settings named a game binary.</returns>
    public static bool TryRead(string? packageRoot, out GeneralsOnlineEacSettings? settings)
    {
        settings = null;
        if (!TryResolveSettingsPath(packageRoot, out var settingsPath) || settingsPath is null)
        {
            return false;
        }

        return TryParseSettings(settingsPath, out settings);
    }

    private static bool TryResolveSettingsPath(string? packageRoot, out string? settingsPath)
    {
        settingsPath = null;
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            return false;
        }

        try
        {
            var relative = GeneralsOnlineConstants.EacSettingsRelativePath
                .Replace('/', Path.DirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(packageRoot);
            var resolved = Path.GetFullPath(Path.Combine(fullRoot, relative));
            var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!resolved.StartsWith(prefix, PathHelper.PathComparison))
            {
                return false;
            }

            settingsPath = resolved;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryParseSettings(string settingsPath, out GeneralsOnlineEacSettings? settings)
    {
        settings = null;
        try
        {
            var info = new FileInfo(settingsPath);
            if (!info.Exists || info.Length > GeneralsOnlineConstants.EacSettingsMaxSizeBytes)
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return TryExtractSettingsProperties(document.RootElement, out settings);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryExtractSettingsProperties(JsonElement root, out GeneralsOnlineEacSettings? settings)
    {
        settings = null;
        string? executable = null;
        string? productId = null;

        foreach (var property in root.EnumerateObject())
        {
            if (executable is null
                && property.Name.Equals(GeneralsOnlineConstants.EacSettingsExecutableKey, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                executable = property.Value.GetString();
            }
            else if (productId is null
                && property.Name.Equals(GeneralsOnlineConstants.EacSettingsProductIdKey, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                productId = property.Value.GetString();
            }
        }

        var normalizedExe = NormalizeExecutableName(executable);
        if (string.IsNullOrWhiteSpace(normalizedExe))
        {
            return false;
        }

        settings = new GeneralsOnlineEacSettings(
            normalizedExe,
            string.IsNullOrWhiteSpace(productId) ? null : productId.Trim());
        return true;
    }
}
