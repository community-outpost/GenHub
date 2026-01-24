namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that detects and replaces placeholder serial keys (ergc) in the registry.
/// This prevents "Serial key already in use" errors and enables C&amp;C Online play.
/// </summary>
public class SerialKeyFix(
    IRegistryService registryService,
    ILogger<SerialKeyFix> logger) : BaseActionSet(logger)
{
    private const string PlaceholderSerial1 = "12345678901234567890";
    private const string PlaceholderSerialZero = "00000000000000000000";
    private const string PlaceholderSerialDashes = "0000-0000-0000-0000-0000";

    private readonly IRegistryService _registryService = registryService;
    private readonly ILogger<SerialKeyFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "SerialKeyFix";

    /// <inheritdoc/>
    public override string Title => "Fix Serial Keys";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        if (installation.HasGenerals)
        {
            var serial = _registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);
            if (IsPlaceholder(serial)) return Task.FromResult(true);
        }

        if (installation.HasZeroHour)
        {
            var serial = _registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);
            if (IsPlaceholder(serial)) return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            if (installation.HasGenerals)
            {
                var serial = _registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);
                if (IsPlaceholder(serial)) return Task.FromResult(false);
            }

            if (installation.HasZeroHour)
            {
                var serial = _registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);
                if (IsPlaceholder(serial)) return Task.FromResult(false);
            }

            // If we get here, keys are valid, so IsApplied is false (because it's Not Applicable)
            // But if we return false here, and IsApplicable is false, it shows "NOT APPLICABLE" (Gray)
            // If we return true here, and IsApplicable is false, it shows "APPLIED" (Green)
            // We want "NOT APPLICABLE" if keys are already good.
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking serial key status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("Checking game serial keys...");
            var randomSerial = GenerateRandomSerial();

            if (installation.HasGenerals)
            {
                var serial = _registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);
                if (IsPlaceholder(serial))
                {
                    details.Add("  Found placeholder serial for Generals. Generating new one...");
                    if (_registryService.SetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty, randomSerial))
                    {
                        details.Add($"  ✓ Applied new serial to {RegistryConstants.EAAppGeneralsErgcKeyPath}");
                    }
                    else
                    {
                        details.Add("  ✗ Failed to apply new serial for Generals (permissions?)");
                    }
                }
                else
                {
                    details.Add("  ✓ Generals serial is already valid");
                }
            }

            if (installation.HasZeroHour)
            {
                var serial = _registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);
                if (IsPlaceholder(serial))
                {
                    details.Add("  Found placeholder serial for Zero Hour. Generating new one...");

                    // We can use the same or different serial. GenPatcher uses same for both if applied together.
                    if (_registryService.SetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty, randomSerial))
                    {
                        details.Add($"  ✓ Applied new serial to {RegistryConstants.EAAppZeroHourErgcKeyPath}");
                    }
                    else
                    {
                        details.Add("  ✗ Failed to apply new serial for Zero Hour (permissions?)");
                    }
                }
                else
                {
                    details.Add("  ✓ Zero Hour serial is already valid");
                }
            }

            details.Add("✓ Serial key fix completed successfully");
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying serial key fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing Serial Key Fix is not supported.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private static bool IsPlaceholder(string? serial)
    {
        if (string.IsNullOrEmpty(serial)) return true;

        var s = serial.Trim();
        return s == PlaceholderSerial1 ||
               s == PlaceholderSerialZero ||
               s == PlaceholderSerialDashes;
    }

    private static string GenerateRandomSerial()
    {
        var random = new Random();
        var serial = "GP2";
        for (int i = 0; i < 17; i++)
        {
            serial += random.Next(0, 10).ToString();
        }

        return serial;
    }
}
