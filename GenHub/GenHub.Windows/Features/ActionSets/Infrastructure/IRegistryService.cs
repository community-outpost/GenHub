using System;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GenHub.Windows.Features.ActionSets.Infrastructure;

/// <summary>
/// Service for interacting with the Windows Registry.
/// </summary>
public interface IRegistryService
{
    /// <summary>
    /// Gets a value indicating whether the application is running with administrator privileges.
    /// </summary>
    /// <returns>True if running as administrator, false otherwise.</returns>
    bool IsRunningAsAdministrator();

    /// <summary>
    /// Gets a string value from the registry.
    /// </summary>
    /// <param name="keyPath">The path to the registry key.</param>
    /// <param name="valueName">The name of the value to retrieve.</param>
    /// <param name="useWow6432Node">Whether to use the Wow6432Node (32-bit registry view).</param>
    /// <param name="hive">The registry hive to access (defaults to LocalMachine).</param>
    /// <returns>The string value, or null if not found or an error occurred.</returns>
    string? GetStringValue(string keyPath, string valueName, bool useWow6432Node = true, RegistryHive hive = RegistryHive.LocalMachine);

    /// <summary>
    /// Sets a string value in the registry.
    /// </summary>
    /// <param name="keyPath">The path to the registry key.</param>
    /// <param name="valueName">The name of the value to set.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="useWow6432Node">Whether to use the Wow6432Node (32-bit registry view).</param>
    /// <param name="hive">The registry hive to access (defaults to LocalMachine).</param>
    /// <returns>True if successful, false otherwise.</returns>
    bool SetStringValue(string keyPath, string valueName, string value, bool useWow6432Node = true, RegistryHive hive = RegistryHive.LocalMachine);

    /// <summary>
    /// Gets an integer value from the registry.
    /// </summary>
    /// <param name="keyPath">The path to the registry key.</param>
    /// <param name="valueName">The name of the value to retrieve.</param>
    /// <param name="useWow6432Node">Whether to use the Wow6432Node (32-bit registry view).</param>
    /// <param name="hive">The registry hive to access (defaults to LocalMachine).</param>
    /// <returns>The integer value, or null if not found or an error occurred.</returns>
    int? GetIntValue(string keyPath, string valueName, bool useWow6432Node = true, RegistryHive hive = RegistryHive.LocalMachine);

    /// <summary>
    /// Sets an integer value in the registry.
    /// </summary>
    /// <param name="keyPath">The path to the registry key.</param>
    /// <param name="valueName">The name of the value to set.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="useWow6432Node">Whether to use the Wow6432Node (32-bit registry view).</param>
    /// <param name="hive">The registry hive to access (defaults to LocalMachine).</param>
    /// <returns>True if successful, false otherwise.</returns>
    bool SetIntValue(string keyPath, string valueName, int value, bool useWow6432Node = true, RegistryHive hive = RegistryHive.LocalMachine);
}

/// <summary>
/// Implementation of the registry service.
/// </summary>
public class RegistryService(ILogger<RegistryService> logger) : IRegistryService
{
    /// <summary>
    /// Gets a value indicating whether the application is running with administrator privileges.
    /// </summary>
    /// <returns>True if running as administrator, false otherwise.</returns>
    public bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to determine if running as administrator");
            return false;
        }
    }

    /// <summary>
    /// Gets a string value from the registry.
    /// </summary>
    /// <param name="keyPath">The path to the registry key.</param>
    /// <param name="valueName">The name of the value to retrieve.</param>
    /// <param name="useWow6432Node">Whether to use the Wow6432Node (32-bit registry view).</param>
    /// <param name="hive">The registry hive to access (defaults to LocalMachine).</param>
    /// <returns>The string value, or null if not found or an error occurred.</returns>
    public string? GetStringValue(string keyPath, string valueName, bool useWow6432Node = true, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, useWow6432Node ? RegistryView.Registry32 : RegistryView.Default);
            using var subKey = baseKey.OpenSubKey(keyPath);
            return subKey?.GetValue(valueName) as string;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read registry key {KeyPath}\\{ValueName}", keyPath, valueName);
            return null;
        }
    }

    /// <summary>
    /// Sets a string value in the registry.
    /// </summary>
    /// <param name="keyPath">The path to the registry key.</param>
    /// <param name="valueName">The name of the value to set.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="useWow6432Node">Whether to use the Wow6432Node (32-bit registry view).</param>
    /// <param name="hive">The registry hive to access (defaults to LocalMachine).</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool SetStringValue(string keyPath, string valueName, string value, bool useWow6432Node = true, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, useWow6432Node ? RegistryView.Registry32 : RegistryView.Default);
            using var subKey = baseKey.CreateSubKey(keyPath); // CreateSubKey opens it for write if it exists
            subKey.SetValue(valueName, value);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write registry key {KeyPath}\\{ValueName}", keyPath, valueName);
            return false;
        }
    }

    /// <summary>
    /// Gets an integer value from the registry.
    /// </summary>
    /// <param name="keyPath">The path to the registry key.</param>
    /// <param name="valueName">The name of the value to retrieve.</param>
    /// <param name="useWow6432Node">Whether to use the Wow6432Node (32-bit registry view).</param>
    /// <param name="hive">The registry hive to access (defaults to LocalMachine).</param>
    /// <returns>The integer value, or null if not found or an error occurred.</returns>
    public int? GetIntValue(string keyPath, string valueName, bool useWow6432Node = true, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, useWow6432Node ? RegistryView.Registry32 : RegistryView.Default);
            using var subKey = baseKey.OpenSubKey(keyPath);
            return subKey?.GetValue(valueName) as int?;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read registry key {KeyPath}\\{ValueName}", keyPath, valueName);
            return null;
        }
    }

    /// <summary>
    /// Sets an integer value in the registry.
    /// </summary>
    /// <param name="keyPath">The path to the registry key.</param>
    /// <param name="valueName">The name of the value to set.</param>
    /// <param name="value">The value to set.</param>
    /// <param name="useWow6432Node">Whether to use the Wow6432Node (32-bit registry view).</param>
    /// <param name="hive">The registry hive to access (defaults to LocalMachine).</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool SetIntValue(string keyPath, string valueName, int value, bool useWow6432Node = true, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, useWow6432Node ? RegistryView.Registry32 : RegistryView.Default);
            using var subKey = baseKey.CreateSubKey(keyPath);
            subKey.SetValue(valueName, value, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write registry key {KeyPath}\\{ValueName}", keyPath, valueName);
            return false;
        }
    }
}