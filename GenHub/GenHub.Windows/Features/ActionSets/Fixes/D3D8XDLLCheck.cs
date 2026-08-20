namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that checks for DirectX 8 DLLs required by the game.
/// This fix verifies that necessary DirectX 8 runtime files are present
/// and provides guidance if they are missing.
/// </summary>
public class D3D8XDLLCheck(ILogger<D3D8XDLLCheck> logger) : BaseActionSet(logger)
{
    // DirectX 8/9 DLLs that Generals and Zero Hour may require (Retail only)
    private static readonly IReadOnlyList<string> RequiredDLLs =
    [
        "d3d8.dll",
        "d3d8thk.dll",
        "d3dx9_43.dll",
    ];

    /// <inheritdoc/>
    public override string Id => "D3D8XDLLCheck";

    /// <inheritdoc/>
    public override string Title => "DirectX 8 DLL Check";

    /// <inheritdoc/>
    public override string Description => "Scans system directories for legacy DirectX 8/9 runtime DLLs (d3d8.dll, d3dx9_43.dll) required to launch the game.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Modern Windows systems do not pre-install legacy DirectX 8 and 9 runtime libraries by default. This diagnostic check verifies whether essential graphics binaries (d3d8.dll, d3d8thk.dll, and d3dx9_43.dll) exist in SysWOW64 or the game directory to prevent missing DLL startup errors.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Compatibility;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            var missingDLLs = GetMissingDlls(installation);
            var allPresent = missingDLLs.Count == 0;

            if (allPresent)
            {
                logger.LogInformation("All required DirectX 8 DLLs are present");
            }
            else
            {
                logger.LogWarning("Missing DirectX 8 DLLs: {DLLs}", string.Join(", ", missingDLLs));
            }

            return Task.FromResult(allPresent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking DirectX 8 DLLs");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            var missingDLLs = GetMissingDlls(installation);

            if (missingDLLs.Count == 0)
            {
                logger.LogInformation("All required DirectX 8 DLLs are present. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            logger.LogWarning("The following DirectX 8 DLLs are missing:");
            foreach (var dll in missingDLLs)
            {
                logger.LogWarning("  - {DLL}", dll);
            }

            logger.LogInformation("To fix this issue:");
            logger.LogInformation("1. Run DirectXRuntimeFix to install DirectX 8.1/9.0c runtime");
            logger.LogInformation("2. This will install all required DirectX 8 DLLs");
            logger.LogInformation("3. Restart your computer after installation");

            return Task.FromResult(new ActionSetResult(false, $"Missing {missingDLLs.Count} DirectX 8 DLL(s). Please run DirectX Runtime Fix to install required runtime libraries.", [$"Missing {missingDLLs.Count} DirectX 8 DLL(s). Please run DirectX Runtime Fix."]));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking DirectX 8 DLLs");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("D3D8XDLLCheck is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private static IReadOnlyList<string> GetMissingDlls(GameInstallation installation)
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var sysWow64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");

        var checkPaths = new List<string>();
        if (!string.IsNullOrEmpty(installation.InstallationPath))
        {
            checkPaths.Add(installation.InstallationPath);
        }

        if (!string.IsNullOrEmpty(installation.GeneralsPath))
        {
            checkPaths.Add(installation.GeneralsPath);
        }

        if (!string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            checkPaths.Add(installation.ZeroHourPath);
        }

        var missing = new List<string>();
        foreach (var dll in RequiredDLLs)
        {
            var inSystem32 = File.Exists(Path.Combine(system32, dll));
            var inSysWow64 = File.Exists(Path.Combine(sysWow64, dll));
            var inGameDir = checkPaths.Exists(p => File.Exists(Path.Combine(p, dll)));

            if (!inSystem32 && !inSysWow64 && !inGameDir)
            {
                missing.Add(dll);
            }
        }

        return missing;
    }
}
