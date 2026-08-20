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

    /// <inheritdoc/>
    public override string Id => "FirewallExceptionFix";

    /// <inheritdoc/>
    public override string Title => "Windows Firewall Exceptions";

    /// <inheritdoc/>
    public override string Description => "Adds Windows Defender Firewall inbound exception rules for game executables and multiplayer ports (UDP/TCP 16000-16001).";

    /// <inheritdoc/>
    public override string DetailedDescription => "Windows Firewall frequently blocks the peer-to-peer UDP and TCP packets used by Generals and Zero Hour for multiplayer networking, leading to connection timeouts. This fix creates dedicated inbound firewall rules for game executables and open multiplayer ports (UDP 16000, UDP 16001, TCP 16001).";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Multiplayer;

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
            // Check for GenPatcher's primary rule - if this exists, fix is applied
            // This matches GenPatcher's PerformIsApplied() which checks "GP Open UDP Port 16000"
            var hasPortRule = IsFirewallRuleExists(PortRuleUdp16000);
            logger.LogInformation("Firewall rule '{RuleName}' exists: {Exists}", PortRuleUdp16000, hasPortRule);
            return Task.FromResult(hasPortRule);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking firewall rules status");
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
                logger.LogInformation("Firewall rules already applied");
                return new ActionSetResult(true, null, details);
            }

            int rulesAdded = 0;
            int rulesFailed = 0;

            // Run firewall commands asynchronously to avoid UI blocking
            await Task.Run(
                () =>
            {
                // Add port rules (like GenPatcher does)
                if (AddPortRule(PortRuleUdp16000, ActionSetConstants.FirewallRules.ProtocolUdp, 16000))
                {
                    rulesAdded++;
                    details.Add($"✓ Added rule: {PortRuleUdp16000}");
                }
                else
                {
                    rulesFailed++;
                    details.Add($"⚠ Failed: {PortRuleUdp16000}");
                }

                if (AddPortRule(PortRuleUdp16001, ActionSetConstants.FirewallRules.ProtocolUdp, 16001))
                {
                    rulesAdded++;
                    details.Add($"✓ Added rule: {PortRuleUdp16001}");
                }
                else
                {
                    rulesFailed++;
                    details.Add($"⚠ Failed: {PortRuleUdp16001}");
                }

                if (AddPortRule(
                    PortRuleTcp16001,
                    ActionSetConstants.FirewallRules.ProtocolTcp,
                    16001))
                {
                    rulesAdded++;
                    details.Add($"✓ Added rule: {PortRuleTcp16001}");
                }
                else
                {
                    rulesFailed++;
                    details.Add($"⚠ Failed: {PortRuleTcp16001}");
                }

                // Add Generals executable rules
                if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
                {
                    var generalsExe = Path.Combine(installation.GeneralsPath, ActionSetConstants.FileNames.GeneralsExe);
                    var generalsGameDat = Path.Combine(installation.GeneralsPath, ActionSetConstants.FileNames.GameDat);

                    if (File.Exists(generalsExe))
                    {
                        if (AddProgramRule(GeneralsRule, generalsExe))
                        {
                            rulesAdded++;
                            details.Add($"✓ Added rule: {GeneralsRule}");
                        }
                        else
                        {
                            rulesFailed++;
                            details.Add($"⚠ Failed: {GeneralsRule}");
                        }
                    }

                    if (File.Exists(generalsGameDat))
                    {
                        if (AddProgramRule(GeneralsGameDatRule, generalsGameDat))
                        {
                            rulesAdded++;
                            details.Add($"✓ Added rule: {GeneralsGameDatRule}");
                        }
                        else
                        {
                            rulesFailed++;
                            details.Add($"⚠ Failed: {GeneralsGameDatRule}");
                        }
                    }
                }

                // Add Zero Hour executable rules
                if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
                {
                    var zeroHourExe = Path.Combine(installation.ZeroHourPath, ActionSetConstants.FileNames.GeneralsExe);
                    var zeroHourGameDat = Path.Combine(installation.ZeroHourPath, ActionSetConstants.FileNames.GameDat);
                    if (File.Exists(zeroHourExe))
                    {
                        if (AddProgramRule(ZeroHourRule, zeroHourExe))
                        {
                            rulesAdded++;
                            details.Add($"✓ Added rule: {ZeroHourRule}");
                        }
                        else
                        {
                            rulesFailed++;
                            details.Add($"⚠ Failed: {ZeroHourRule}");
                        }
                    }

                    if (File.Exists(zeroHourGameDat))
                    {
                        if (AddProgramRule(ZeroHourGameDatRule, zeroHourGameDat))
                        {
                            rulesAdded++;
                            details.Add($"✓ Added rule: {ZeroHourGameDatRule}");
                        }
                        else
                        {
                            rulesFailed++;
                            details.Add($"⚠ Failed: {ZeroHourGameDatRule}");
                        }
                    }
                }
            },
                cancellationToken);

            if (rulesAdded == 0 && rulesFailed > 0)
            {
                logger.LogWarning("Firewall rule configuration failed completely. Administrative privileges may be required.");
                return new ActionSetResult(false, "Failed to configure any firewall rules. Administrative privileges may be required.", details);
            }

            if (rulesFailed > 0)
            {
                logger.LogWarning("Firewall exceptions applied with {FailedCount} failures out of {TotalCount}", rulesFailed, rulesAdded + rulesFailed);
                return new ActionSetResult(false, $"Failed to add {rulesFailed} firewall rule(s).", details);
            }

            logger.LogInformation("All {Count} firewall rules added successfully", rulesAdded);
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying firewall exception fix");
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
            details.Add("Removing firewall rules...");
            int rulesRemoved = 0;
            int rulesFailed = 0;

            // Run firewall commands asynchronously to avoid UI blocking
            await Task.Run(
                () =>
            {
                // Remove port rules
                if (RemoveFirewallRule(PortRuleUdp16000))
                {
                    rulesRemoved++;
                    details.Add($"✓ Removed rule: {PortRuleUdp16000}");
                }
                else
                {
                    rulesFailed++;
                    details.Add($"⚠ Failed to remove rule: {PortRuleUdp16000}");
                }

                if (RemoveFirewallRule(PortRuleUdp16001))
                {
                    rulesRemoved++;
                    details.Add($"✓ Removed rule: {PortRuleUdp16001}");
                }
                else
                {
                    rulesFailed++;
                    details.Add($"⚠ Failed to remove rule: {PortRuleUdp16001}");
                }

                if (RemoveFirewallRule(PortRuleTcp16001))
                {
                    rulesRemoved++;
                    details.Add($"✓ Removed rule: {PortRuleTcp16001}");
                }
                else
                {
                    rulesFailed++;
                    details.Add($"⚠ Failed to remove rule: {PortRuleTcp16001}");
                }

                // Remove Generals executable rules
                if (RemoveFirewallRule(GeneralsRule))
                {
                    rulesRemoved++;
                    details.Add($"✓ Removed rule: {GeneralsRule}");
                }

                if (RemoveFirewallRule(GeneralsGameDatRule))
                {
                    rulesRemoved++;
                    details.Add($"✓ Removed rule: {GeneralsGameDatRule}");
                }

                // Remove Zero Hour executable rules
                if (RemoveFirewallRule(ZeroHourRule))
                {
                    rulesRemoved++;
                    details.Add($"✓ Removed rule: {ZeroHourRule}");
                }

                if (RemoveFirewallRule(ZeroHourGameDatRule))
                {
                    rulesRemoved++;
                    details.Add($"✓ Removed rule: {ZeroHourGameDatRule}");
                }
            },
                cancellationToken);

            logger.LogInformation("Firewall rules removal finished: {RemovedCount} removed, {FailedCount} failed", rulesRemoved, rulesFailed);
            if (rulesFailed > 0)
            {
                return new ActionSetResult(false, $"Failed to remove {rulesFailed} firewall rule(s).", details);
            }

            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error undoing firewall exception fix");
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
                _ = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return process.ExitCode == ProcessConstants.ExitCodeSuccess &&
                       !string.IsNullOrWhiteSpace(output) &&
                       !output.Contains("No rules", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking if firewall rule exists: {RuleName}", ruleName);
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

            logger.LogInformation("Running: netsh {Args}", psi.Arguments);

            using var process = Process.Start(psi);
            if (process != null)
            {
                _ = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == ProcessConstants.ExitCodeSuccess;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding port firewall rule: {RuleName}", ruleName);
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

            logger.LogInformation("Running: netsh {Args}", psi.Arguments);

            using var process = Process.Start(psi);
            if (process != null)
            {
                _ = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == ProcessConstants.ExitCodeSuccess;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding program firewall rule: {RuleName}", ruleName);
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
                _ = process.StandardOutput.ReadToEnd();
                _ = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == ProcessConstants.ExitCodeSuccess;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error removing firewall rule: {RuleName}", ruleName);
            return false;
        }
    }
}
