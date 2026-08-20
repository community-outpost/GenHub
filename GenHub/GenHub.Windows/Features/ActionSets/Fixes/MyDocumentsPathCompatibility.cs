namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix for My Documents path compatibility issues (e.g. non-English characters or double backslashes).
/// </summary>
public partial class MyDocumentsPathCompatibility(ILogger<MyDocumentsPathCompatibility> logger) : BaseActionSet(logger)
{
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "MyDocumentsPathCompatibility.done");

    /// <inheritdoc/>
    public override string Id => "MyDocumentsPathCompatibility";

    /// <inheritdoc/>
    public override string Title => "My Documents Path Compatibility";

    /// <inheritdoc/>
    public override string Description => "Verifies Windows Documents path contains only ASCII characters to prevent engine crash-on-startup errors.";

    /// <inheritdoc/>
    public override string DetailedDescription => "The 2003 Generals engine relies on legacy ANSI file I/O to load user settings (Options.ini), savegames, and replays from the Documents folder. If your Windows username or Documents path contains non-ASCII, accented, or non-English characters, the game crashes with Technical Difficulties on startup. This fix validates the path and provides relocation steps.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // This fix applies to the User Profile / Documents path, not the game installation itself.
        // But we check it in context of an installation being present.
        if (!installation.HasGenerals && !installation.HasZeroHour)
        {
            return Task.FromResult(false);
        }

        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Task.FromResult(!IsValidPath(documentsPath));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        // If valid, return TRUE (applied/compliant). If invalid, return FALSE (needs fixing).
        return Task.FromResult(IsValidPath(documentsPath));
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        if (IsValidPath(documentsPath))
        {
            return Task.FromResult(new ActionSetResult(true, null, [$"Documents path '{documentsPath}' is compatible."]));
        }

        // Automatic moving of OS User Documents profile is not supported without user manual relocation.
        return Task.FromResult(new ActionSetResult(
            false,
            $"Manual Action Required: Your 'Documents' path '{documentsPath}' contains non-ASCII or unsupported characters.",
            [
                $"Current Documents path: {documentsPath}",
                "Right-click on Documents folder > Properties > Location to relocate to an ASCII-only path.",
            ]));
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        return Task.FromResult(new ActionSetResult(true, null, ["Documents path compatibility does not require undo."]));
    }

    private static bool IsValidPath(string path)
    {
        // Check for double backslashes (excluding the initial network share start if applicable, but usually strictly local)
        // AHK logic: if(InStr(Path, "\\")) return 0
        // C# Path.GetFullPath handles normalization, but if the string *source* has \\ it might be an issue for the game engine.
        if (path.Contains("\\\\"))
        {
            return false;
        }

        // Allowed chars: A-Z, 0-9, space, and specific symbols: `~!@#$%^&()_+-='{}.,;[]
        // AHK logic replaces these out and checks if anything remains.
        // We can use Regex to check if *any* character is NOT in the allowed set.
        // Note: Backslash \ and Colon : are allowed for drive paths e.g. C:\
        // Regex for disallowed characters: [^a-zA-Z0-9 `~!@#$%^&()_+\-='{}\.,;\[\]\:\\]
        // If match found, return false.
        return !DisallowedCharactersRegex().IsMatch(path);
    }

    [GeneratedRegex(@"[^a-zA-Z0-9 `~!@#$%^&()_+\-='{}\.,;\[\]\:\\]")]
    private static partial Regex DisallowedCharactersRegex();
}
