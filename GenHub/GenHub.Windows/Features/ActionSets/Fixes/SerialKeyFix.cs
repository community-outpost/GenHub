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

    /// <inheritdoc/>
    public override string Id => "SerialKeyFix";

    /// <inheritdoc/>
    public override string Title => "Fix Serial Keys";

    /// <inheritdoc/>
    public override string Description => "Replaces shared placeholder CD keys in the registry with unique keys to eliminate \"Serial key already in use\" errors.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Digital releases from Steam and the EA App install identical placeholder serial keys for all users, making online multiplayer impossible due to serial key conflicts. This fix generates and registers a unique, valid CD key in your Windows registry so you can play on C&C:Online and LAN without conflicts.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        if (installation.HasGenerals)
        {
            var serial = registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);
            if (IsPlaceholder(serial)) return Task.FromResult(true);
        }

        if (installation.HasZeroHour)
        {
            var serial = registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);
            if (IsPlaceholder(serial)) return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            if (installation.HasGenerals)
            {
                var serial = registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);
                if (IsPlaceholder(serial)) return Task.FromResult(false);
            }

            if (installation.HasZeroHour)
            {
                var serial = registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);
                if (IsPlaceholder(serial)) return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking serial key status");
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
            bool writeFailed = false;

            if (installation.HasGenerals)
            {
                var serial = registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);
                if (IsPlaceholder(serial))
                {
                    var generalsSerial = GenerateRandomSerial();
                    details.Add("  Found placeholder serial for Generals. Generating new one...");
                    if (registryService.SetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty, generalsSerial))
                    {
                        details.Add($"  ✓ Applied new serial to {RegistryConstants.EAAppGeneralsErgcKeyPath}");
                    }
                    else
                    {
                        writeFailed = true;
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
                var serial = registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);
                if (IsPlaceholder(serial))
                {
                    var zeroHourSerial = GenerateRandomSerial();
                    details.Add("  Found placeholder serial for Zero Hour. Generating new one...");
                    if (registryService.SetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty, zeroHourSerial))
                    {
                        details.Add($"  ✓ Applied new serial to {RegistryConstants.EAAppZeroHourErgcKeyPath}");
                    }
                    else
                    {
                        writeFailed = true;
                        details.Add("  ✗ Failed to apply new serial for Zero Hour (permissions?)");
                    }
                }
                else
                {
                    details.Add("  ✓ Zero Hour serial is already valid");
                }
            }

            if (writeFailed)
            {
                return Task.FromResult(new ActionSetResult(false, "Failed to apply one or more serial keys.", details));
            }

            details.Add("✓ Serial key fix completed successfully");
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying serial key fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogInformation("Undoing serial key generation is not supported as removing keys will prevent the game from starting.");
        return Task.FromResult(new ActionSetResult(false, "Undoing serial key configuration is not supported as valid serial keys are required for game execution.", ["Valid serial keys remain in registry."]));
    }

    private static bool IsPlaceholder(string? serial)
    {
        if (string.IsNullOrEmpty(serial)) return true;

        var s = serial.Trim();
        return s == PlaceholderSerial1 ||
               s == PlaceholderSerialZero ||
               s == PlaceholderSerialDashes ||
               s == ActionSetConstants.Serials.DefaultEAAppGeneralsSerial ||
               s == ActionSetConstants.Serials.DefaultEAAppZeroHourSerial;
    }

    private static string GenerateRandomSerial()
    {
        var sb = new System.Text.StringBuilder("GP2", 20);
        for (int i = 0; i < 17; i++)
        {
            sb.Append(System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10));
        }

        return sb.ToString();
    }
}
