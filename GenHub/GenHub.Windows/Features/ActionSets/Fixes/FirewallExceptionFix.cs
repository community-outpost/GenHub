namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that adds Windows Firewall exceptions for game executables to allow multiplayer.
/// Uses the same rule names as GenPatcher for compatibility.
/// </summary>
public class FirewallExceptionFix(ILogger<FirewallExceptionFix> logger) : BaseActionSet(logger)
{
    // GenPatcher-compatible rule names
    private const string PortRuleUdp16000 = ActionSetConstants.FirewallRules.PortRuleUdp16000;
    private const string PortRuleUdp16001 = ActionSetConstants.FirewallRules.PortRuleUdp16001;
    private const string PortRuleTcp16001 = ActionSetConstants.FirewallRules.PortRuleTcp16001;

    private const string GeneralsRule = ActionSetConstants.FirewallRules.GeneralsRule;
    private const string GeneralsGameDatRule = ActionSetConstants.FirewallRules.GeneralsGameDatRule;
    private const string ZeroHourRule = ActionSetConstants.FirewallRules.ZeroHourRule;
    private const string ZeroHourGameDatRule = ActionSetConstants.FirewallRules.ZeroHourGameDatRule;

    private readonly ILogger<FirewallExceptionFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "FirewallExceptionFix";

    /// <inheritdoc/>
    public override string Title => "Windows Firewall Exceptions";

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
            // Check for GenPatcher's primary rule - if this exists, fix is applied
            // This matches GenPatcher's PerformIsApplied() which checks "GP Open UDP Port 16000"
            var hasPortRule = IsFirewallRuleExists(PortRuleUdp16000);
            _logger.LogInformation("Firewall rule '{RuleName}' exists: {Exists}", PortRuleUdp16000, hasPortRule);
            return Task.FromResult(hasPortRule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking firewall rules status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            // Check if already applied
            if (IsFirewallRuleExists(PortRuleUdp16000))
            {
                details.Add("✓ Firewall rules already applied (found GP Open UDP Port 16000)");
                _logger.LogInformation("Firewall rules already applied");
                return new ActionSetResult(true, null, details);
            }

            // Run firewall commands asynchronously to avoid UI blocking
            await Task.Run(
                () =>
            {
                // Add port rules (like GenPatcher does)
                if (AddPortRule(PortRuleUdp16000, "UDP", 16000))
                {
                    details.Add($"✓ Added rule: {PortRuleUdp16000}");
                }
                else
                {
                    details.Add($"⚠ Failed: {PortRuleUdp16000}");
                }

                if (AddPortRule(PortRuleUdp16001, "UDP", 16001))
                {
                    details.Add($"✓ Added rule: {PortRuleUdp16001}");
                }
                else
                {
                    details.Add($"⚠ Failed: {PortRuleUdp16001}");
                }

                if (AddPortRule(
                    PortRuleTcp16001,
                    "TCP",
                    16001))
                {
                    details.Add($"✓ Added rule: {PortRuleTcp16001}");
                }
                else
                {
                    details.Add($"⚠ Failed: {PortRuleTcp16001}");
                }

                // Add Generals executable rules
                if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
                {
                    var generalsExe = Path.Combine(installation.GeneralsPath, "Generals.exe");
                    var generalsGameDat = Path.Combine(installation.GeneralsPath, "Game.dat");

                    if (File.Exists(generalsExe))
                    {
                        if (AddProgramRule(GeneralsRule, generalsExe))
                        {
                            details.Add($"✓ Added rule: {GeneralsRule}");
                        }
                        else
                        {
                            details.Add($"⚠ Failed: {GeneralsRule}");
                        }
                    }

                    if (File.Exists(generalsGameDat))
                    {
                        if (AddProgramRule(GeneralsGameDatRule, generalsGameDat))
                        {
                            details.Add($"✓ Added rule: {GeneralsGameDatRule}");
                        }
                        else
                        {
                            details.Add($"⚠ Failed: {GeneralsGameDatRule}");
                        }
                    }
                }

                // Add Zero Hour executable rules
                if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
                {
                    // NOTE: Zero Hour often runs via generals.exe (the engine), not the launcher.
                    // However, we add rules for both standard executables just in case.

                    // Add Zero Hour executable rule
                    var zeroHourExe = Path.Combine(installation.ZeroHourPath, ActionSetConstants.FileNames.GeneralsExe);
                    var zeroHourGameDat = Path.Combine(installation.ZeroHourPath, ActionSetConstants.FileNames.GameDat);
                    if (File.Exists(zeroHourExe))
                    {
                        if (AddProgramRule(ZeroHourRule, zeroHourExe))
                        {
                            details.Add($"✓ Added rule: {ZeroHourRule}");
                        }
                        else
                        {
                            details.Add($"⚠ Failed: {ZeroHourRule}");
                        }
                    }

                    if (File.Exists(zeroHourGameDat))
                    {
                        if (AddProgramRule(ZeroHourGameDatRule, zeroHourGameDat))
                        {
                            details.Add($"✓ Added rule: {ZeroHourGameDatRule}");
                        }
                        else
                        {
                            details.Add($"⚠ Failed: {ZeroHourGameDatRule}");
                        }
                    }
                }
            },
                cancellationToken);

            _logger.LogInformation("Firewall rules applied. Details: {Details}", string.Join("; ", details));
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying firewall exception fix");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            await Task.Run(
                () =>
            {
                // Remove all GP rules (like GenPatcher does - runs multiple times for duplicates)
                var rulesToRemove = new[]
                {
                    PortRuleUdp16000,
                    PortRuleUdp16001,
                    PortRuleTcp16001,
                    GeneralsRule,
                    GeneralsGameDatRule,
                    ZeroHourRule,
                    ZeroHourGameDatRule,
                };

                foreach (var ruleName in rulesToRemove)
                {
                    // Remove multiple times in case of duplicates (like GenPatcher)
                    for (int i = 0; i < 3; i++)
                    {
                        RemoveFirewallRule(ruleName);
                    }

                    details.Add($"✓ Removed rule: {ruleName}");
                }
            },
                cancellationToken);

            _logger.LogInformation("Firewall rules removed");
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undoing firewall exception fix");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    private bool IsFirewallRuleExists(string ruleName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // GenPatcher checks: if output contains "No rules", rule doesn't exist
                return !output.Contains("No rules", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if firewall rule exists: {RuleName}", ruleName);
            return false;
        }
    }

    private bool AddPortRule(string ruleName, string protocol, int port)
    {
        try
        {
            // GenPatcher command: netsh advfirewall firewall add rule name="GP Open UDP Port 16000" dir=in action=allow edge=yes protocol=UDP localport=16000
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow edge=yes protocol={protocol} localport={port}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _logger.LogInformation("Running: netsh {Args}", psi.Arguments);

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit();
                return process.ExitCode == 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding port firewall rule: {RuleName}", ruleName);
            return false;
        }
    }

    private bool AddProgramRule(string ruleName, string programPath)
    {
        try
        {
            // GenPatcher command: netsh advfirewall firewall add rule name="GP Command & Conquer Generals" dir=in action=allow edge=yes program="..." enable=yes
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow edge=yes program=\"{programPath}\" enable=yes",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _logger.LogInformation("Running: netsh {Args}", psi.Arguments);

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit();
                return process.ExitCode == 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding program firewall rule: {RuleName}", ruleName);
            return false;
        }
    }

    private bool RemoveFirewallRule(string ruleName)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = $"advfirewall firewall delete rule name=\"{ruleName}\"",
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error removing firewall rule: {RuleName}", ruleName);
            return false;
        }
    }
}
