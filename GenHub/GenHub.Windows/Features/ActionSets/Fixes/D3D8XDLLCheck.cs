namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    private static readonly string[] RequiredDLLs =
    [
        "d3d8.dll",
        "d3d8thk.dll",
        "d3dx9_43.dll",
    ];

    private readonly ILogger<D3D8XDLLCheck> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "D3D8XDLLCheck";

    /// <inheritdoc/>
    public override string Title => "DirectX 8 DLL Check";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            // Check if required DirectX DLLs are present in system directories
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var sysWow64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
            var gameDir = installation.InstallationPath;

            var allPresent = true;
            var missingDLLs = new List<string>();

            foreach (var dll in RequiredDLLs)
            {
                var inSystem32 = File.Exists(Path.Combine(system32, dll));
                var inSysWow64 = File.Exists(Path.Combine(sysWow64, dll));
                var inGameDir = !string.IsNullOrEmpty(gameDir) && File.Exists(Path.Combine(gameDir, dll));

                if (!inSystem32 && !inSysWow64 && !inGameDir)
                {
                    allPresent = false;
                    missingDLLs.Add(dll);
                }
            }

            if (allPresent)
            {
                _logger.LogInformation("All required DirectX 8 DLLs are present");
            }
            else
            {
                _logger.LogWarning("Missing DirectX 8 DLLs: {DLLs}", string.Join(", ", missingDLLs));
            }

            return Task.FromResult(allPresent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking DirectX 8 DLLs");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            // This fix is informational - it checks for DLLs and provides guidance
            // The actual DirectX installation is handled by DirectXRuntimeFix
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var sysWow64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");

            var missingDLLs = new List<string>();

            foreach (var dll in RequiredDLLs)
            {
                var inSystem32 = File.Exists(Path.Combine(system32, dll));
                var inSysWow64 = File.Exists(Path.Combine(sysWow64, dll));

                if (!inSystem32 && !inSysWow64)
                {
                    missingDLLs.Add(dll);
                }
            }

            if (missingDLLs.Count == 0)
            {
                _logger.LogInformation("All required DirectX 8 DLLs are present. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            _logger.LogWarning("The following DirectX 8 DLLs are missing:");
            foreach (var dll in missingDLLs)
            {
                _logger.LogWarning("  - {DLL}", dll);
            }

            _logger.LogInformation("To fix this issue:");
            _logger.LogInformation("1. Run DirectXRuntimeFix to install DirectX 8.1/9.0c runtime");
            _logger.LogInformation("2. This will install all required DirectX 8 DLLs");
            _logger.LogInformation("3. Restart your computer after installation");

            return Task.FromResult(new ActionSetResult(true, null, [$"Missing {missingDLLs.Count} DirectX 8 DLLs in system directories. Please run DirectXRuntimeFix."]));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking DirectX 8 DLLs");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("D3D8XDLLCheck is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
