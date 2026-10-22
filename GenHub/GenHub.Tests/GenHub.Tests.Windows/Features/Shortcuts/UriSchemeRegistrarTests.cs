using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Windows.Features.Shortcuts;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Xunit;
using Xunit.Abstractions;

namespace GenHub.Tests.Windows.Features.Shortcuts;

/// <summary>
/// Unit tests for <see cref="UriSchemeRegistrar"/>.
/// </summary>
/// <param name="testOutputHelper">Output helper for surfacing test diagnostic messages.</param>
[Collection(WindowsRegistryCollection.Name)]
[SupportedOSPlatform("windows")]
public sealed class UriSchemeRegistrarTests(ITestOutputHelper testOutputHelper) : IDisposable
{
    private const string TargetKeyPath = @"Software\Classes\genhub";
    private readonly RegistryKeySnapshot? _snapshot = OperatingSystem.IsWindows() ? CaptureInitialSnapshot() : null;
    private readonly bool _existedPrior = OperatingSystem.IsWindows() && KeyExists();

    /// <summary>
    /// Verifies that Register creates or updates the genhub registry keys in HKCU.
    /// </summary>
    [Fact]
    public void Register_CreatesOrUpdatesGenhubRegistryKey()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Act
        UriSchemeRegistrar.Register();

        // Assert
        using var key = Registry.CurrentUser.OpenSubKey(TargetKeyPath);
        Assert.NotNull(key);

        var protocolValue = key.GetValue(string.Empty) as string;
        Assert.Equal("URL:genhub protocol", protocolValue);

        var urlProtocolFlag = key.GetValue("URL Protocol");
        Assert.NotNull(urlProtocolFlag);

        using var commandKey = Registry.CurrentUser.OpenSubKey($@"{TargetKeyPath}\shell\open\command");
        Assert.NotNull(commandKey);

        var command = commandKey.GetValue(string.Empty) as string;
        Assert.NotNull(command);
        Assert.Contains("%1", command);
        Assert.Contains(Environment.ProcessPath ?? string.Empty, command, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that Register can be invoked repeatedly without failure or unexpected mutations.
    /// </summary>
    [Fact]
    public void Register_IsIdempotent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Act - Call twice in succession to ensure no exceptions or unintended side effects occur
        UriSchemeRegistrar.Register();
        var ex = Record.Exception(() => UriSchemeRegistrar.Register());

        // Assert
        Assert.Null(ex);
    }

    /// <summary>
    /// Verifies that Register points the scheme at the registered custom installation launcher
    /// when a duplicate installation conflict is active, instead of the running copy.
    /// </summary>
    [Fact]
    public void Register_PointsAtCanonicalInstall_WhenDuplicateDetected()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var customDir = Path.Combine(Path.GetTempPath(), "GenHubLinkTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(customDir);
        File.WriteAllText(Path.Combine(customDir, StorageMigrationConstants.VelopackUpdateExe), "stub");
        var exeName = Path.GetFileName(Environment.ProcessPath);
        Assert.False(string.IsNullOrEmpty(exeName));
        File.WriteAllText(Path.Combine(customDir, exeName!), "stub");

        StorageMigrationService.SetCustomInstallRootOverrideForTesting(false);
        StorageMigrationService.SetDefaultInstallRootOverrideForTesting(true);

        object? priorValue = null;
        var hadPriorValue = false;
        RegistryValueKind priorKind = RegistryValueKind.String;
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RegistryConstants.GenHubSubKey, writable: true))
            {
                Assert.NotNull(key);
                priorValue = key.GetValue(RegistryConstants.CustomInstallPathValueName);
                hadPriorValue = priorValue != null;
                if (hadPriorValue)
                {
                    priorKind = key.GetValueKind(RegistryConstants.CustomInstallPathValueName);
                }

                key.SetValue(RegistryConstants.CustomInstallPathValueName, customDir);
            }

            UriSchemeRegistrar.Register();

            using var commandKey = Registry.CurrentUser.OpenSubKey($@"{TargetKeyPath}\shell\open\command");
            Assert.NotNull(commandKey);
            var command = commandKey.GetValue(string.Empty) as string;
            Assert.NotNull(command);
            Assert.Contains(customDir, command, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryConstants.GenHubSubKey, writable: true);
                if (hadPriorValue)
                {
                    key?.SetValue(RegistryConstants.CustomInstallPathValueName, priorValue!, priorKind);
                }
                else
                {
                    key?.DeleteValue(RegistryConstants.CustomInstallPathValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                testOutputHelper.WriteLine($"Failed to restore install tracker registry value: {ex.Message}");
            }

            StorageMigrationService.SetCustomInstallRootOverrideForTesting(null);
            StorageMigrationService.SetDefaultInstallRootOverrideForTesting(null);

            try
            {
                Directory.Delete(customDir, recursive: true);
            }
            catch (IOException ex)
            {
                testOutputHelper.WriteLine($"Failed to delete stub custom install: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (_existedPrior && _snapshot != null)
            {
                using var rootKey = Registry.CurrentUser.CreateSubKey(TargetKeyPath, writable: true);
                if (rootKey != null)
                {
                    RestoreSnapshot(rootKey, _snapshot);
                }
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(TargetKeyPath, throwOnMissingSubKey: false);
            }
        }
        catch (Exception ex)
        {
            testOutputHelper.WriteLine($"Failed to restore registry snapshot during test teardown: {ex.Message}");
        }
    }

    private static bool KeyExists()
    {
        using var rootKey = Registry.CurrentUser.OpenSubKey(TargetKeyPath, writable: false);
        return rootKey != null;
    }

    private static RegistryKeySnapshot? CaptureInitialSnapshot()
    {
        using var rootKey = Registry.CurrentUser.OpenSubKey(TargetKeyPath, writable: false);
        return rootKey != null ? CaptureSnapshot(rootKey) : null;
    }

    private static RegistryKeySnapshot CaptureSnapshot(RegistryKey key)
    {
        var snapshot = new RegistryKeySnapshot
        {
            Name = Path.GetFileName(key.Name),
        };

        foreach (var valueName in key.GetValueNames())
        {
            var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            var kind = key.GetValueKind(valueName);
            snapshot.Values[valueName] = (value, kind);
        }

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            using var subKey = key.OpenSubKey(subKeyName, writable: false);
            if (subKey != null)
            {
                snapshot.SubKeys.Add(CaptureSnapshot(subKey));
            }
        }

        return snapshot;
    }

    private static void RestoreSnapshot(RegistryKey targetKey, RegistryKeySnapshot snapshot)
    {
        // Delete values not present in snapshot
        foreach (var valueName in targetKey.GetValueNames())
        {
            if (!snapshot.Values.ContainsKey(valueName))
            {
                targetKey.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }

        // Restore values
        foreach (var (valueName, (value, kind)) in snapshot.Values)
        {
            if (value != null)
            {
                targetKey.SetValue(valueName, value, kind);
            }
        }

        // Delete subkeys not present in snapshot
        var snapshotSubKeyNames = new HashSet<string>(snapshot.SubKeys.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var subKeyName in targetKey.GetSubKeyNames())
        {
            if (!snapshotSubKeyNames.Contains(subKeyName))
            {
                targetKey.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
            }
        }

        // Restore subkeys recursively
        foreach (var subKeySnapshot in snapshot.SubKeys)
        {
            using var subKey = targetKey.CreateSubKey(subKeySnapshot.Name, writable: true);
            if (subKey != null)
            {
                RestoreSnapshot(subKey, subKeySnapshot);
            }
        }
    }

    private sealed class RegistryKeySnapshot
    {
        public string Name { get; set; } = string.Empty;

        public Dictionary<string, (object? Value, RegistryValueKind Kind)> Values { get; } = [];

        public List<RegistryKeySnapshot> SubKeys { get; } = [];
    }
}
