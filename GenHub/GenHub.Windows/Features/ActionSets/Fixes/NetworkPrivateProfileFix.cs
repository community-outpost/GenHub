namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that sets network connection to Private (Home) profile for better LAN/online play.
/// </summary>
public class NetworkPrivateProfileFix(ILogger<NetworkPrivateProfileFix> logger) : BaseActionSet(logger)
{
    private readonly ILogger<NetworkPrivateProfileFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "NetworkPrivateProfileFix";

    /// <inheritdoc/>
    public override string Title => "Network Private Profile";

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
            // Check if at least one network adapter is set to Private
            var profiles = GetNetworkProfiles();
            var hasPrivate = profiles.Any(p => p.Equals("Private", StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(hasPrivate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking network profile status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            var profiles = GetNetworkProfiles();
            details.Add($"Found {profiles.Count} network adapter(s)");

            foreach (var profile in profiles)
            {
                details.Add($"• Adapter profile: {profile}");
            }

            if (profiles.All(p => p.Equals("Private", StringComparison.OrdinalIgnoreCase)))
            {
                details.Add("✓ All network profiles are already set to Private.");
                _logger.LogInformation("Network profile is already set to Private. No action needed.");
                return new ActionSetResult(true, null, details);
            }

            _logger.LogInformation("Setting network profile to Private (Home)...");
            details.Add("Setting network profile to Private...");

            // Use PowerShell to set network profile - run asynchronously to avoid blocking UI
            var success = await Task.Run(
                () =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-WindowStyle Hidden -NonInteractive -Command \"Set-NetConnectionProfile -NetworkCategory Private\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }

                return false;
            },
                cancellationToken);

            if (success)
            {
                details.Add("✓ Network profile successfully set to Private (Home).");
                _logger.LogInformation("Network profile successfully set to Private (Home).");
                return new ActionSetResult(true, null, details);
            }
            else
            {
                details.Add("✗ Failed to set network profile.");
                _logger.LogError("Failed to set network profile");
                return new ActionSetResult(false, "Failed to set network profile", details);
            }
        }
        catch (Exception ex)
        {
            details.Add($"✗ Error: {ex.Message}");
            _logger.LogError(ex, "Error applying network private profile fix");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Network Private Profile Fix cannot be easily undone. Network profile must be manually changed through Windows Settings.");
        return Task.FromResult(new ActionSetResult(true, null, ["To undo, manually change network profile in Windows Settings > Network & Internet > Network and Sharing Center"]));
    }

    private List<string> GetNetworkProfiles()
    {
        var profiles = new List<string>();

        try
        {
            // Use PowerShell to get network profiles
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-WindowStyle Hidden -NonInteractive -Command \"Get-NetConnectionProfile | Select-Object -ExpandProperty NetworkCategory\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // Split by newlines and trim each line
                var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        profiles.Add(trimmed);
                    }
                }

                _logger.LogInformation("Current network profiles: {Profiles}", string.Join(", ", profiles));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking network profile");
        }

        return profiles;
    }
}
