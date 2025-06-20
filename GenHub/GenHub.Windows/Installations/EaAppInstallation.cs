using System;
using System.IO;
using GenHub.Core;
using Microsoft.Win32;

namespace GenHub.Windows.Installations;

/// <inheritdoc/>
public class EaAppInstallation : IGameInstallation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EaAppInstallation"/> class.
    /// </summary>
    /// <param name="fetch">Value indicating whether <see cref="Fetch"/> should be called while instantiation.</param>
    public EaAppInstallation(bool fetch)
    {
        if (fetch)
            Fetch();
    }

    /// <inheritdoc/>
    public GameInstallationType InstallationType => GameInstallationType.EaApp;

    /// <inheritdoc/>
    public bool IsVanillaInstalled { get; private set; }

    /// <inheritdoc/>
    public string VanillaGamePath { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public bool IsZeroHourInstalled { get; private set; }

    /// <inheritdoc/>
    public string ZeroHourGamePath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the EA App is installed successfully.
    /// </summary>
    /// <remarks>
    /// EA App specific.
    /// </remarks>
    public bool IsEaAppInstalled { get; private set; }

    /// <inheritdoc/>
    public void Fetch()
    {
        IsEaAppInstalled = IsEaAppInstallationSuccessful();
        if(!IsEaAppInstallationSuccessful())
            return;

        if(!TryGetEaGamesGeneralsPath(out var generalsPath))
            return;

        string gamePath;

        if (!IsVanillaInstalled)
        {
            gamePath = Path.Combine(generalsPath!, "Command and Conquer Generals");
            if (Directory.Exists(gamePath))
            {
                // TODO: Add a more sophisticated check? E.g. check for generals.exe.
                // So that an empty folder doesn't cause a false positive
                IsVanillaInstalled = true;
                VanillaGamePath = gamePath;
            }
        }

        if (!IsZeroHourInstalled)
        {
            gamePath = Path.Combine(generalsPath!, "Command and Conquer Generals Zero Hour");
            if (Directory.Exists(gamePath))
            {
                // TODO: Add a more sophisticated check? E.g. check for generals.exe.
                // So that an empty folder doesn't cause a false positive
                IsZeroHourInstalled = true;
                ZeroHourGamePath = gamePath;
            }
        }

        // Just for testing, will probably be removed or refactored with more sophisticated logging - NH
        Console.WriteLine($"EA App: Is Vanilla installed? {IsVanillaInstalled} - If yes then it's here: {VanillaGamePath}");
        Console.WriteLine($"EA App: Is Zero Hour installed? {IsZeroHourInstalled} - If yes then it's here: {ZeroHourGamePath}");
    }

    /// <summary>
    /// Tries to fetch the installation path of the EA App from the windows registry.
    /// </summary>
    /// <param name="path">Returns the installation path if successful; otherwise, an empty string.</param>
    /// <returns><c>true</c> if the installation path of steam was found.</returns>
    private bool TryGetEaAppPath(out string? path)
    {
        path = string.Empty;

        using var key = GetEaAppRegistryKey();
        if (key == null)
            return false;

        // Find the steam path in local machine registry
        path = key.GetValue("InstallLocation") as string;
        return !string.IsNullOrEmpty(path);
    }

    /// <summary>
    /// Tries to fetch the installation path of generals and ZH from the windows registry.
    /// </summary>
    /// <param name="path">Returns the installation path if successful; otherwise, an empty string.</param>
    /// <returns><c>true</c> if the installation path of steam was found.</returns>
    private bool TryGetEaGamesGeneralsPath(out string? path)
    {
        path = string.Empty;

        using var key = GetEaGamesGeneralsRegistryKey();
        if (key == null)
            return false;

        // Find the steam path in local machine registry
        path = key.GetValue("Install Dir") as string;
        return !string.IsNullOrEmpty(path);
    }

    /// <summary>
    /// Checks if the Ea app was installed successfully via the registry.
    /// </summary>
    /// <returns><c>True</c> if Ea App was installed successfully.</returns>
    private bool IsEaAppInstallationSuccessful()
    {
        using var key = GetEaAppRegistryKey();

        var successValue = key?.GetValue("InstallSuccessful") as string;
        if (successValue == null)
            return false;

        if (bool.TryParse(successValue, out var success))
            return success;
        return false;
    }

    /// <summary>
    /// Returns a disposable <see cref="RegistryKey"/>. Caller is responsible for disposing it.
    /// </summary>
    /// <returns>A disposable <see cref="RegistryKey"/>.</returns>
    private RegistryKey? GetEaAppRegistryKey()
    {
        // Check local machine
        // This may or may not cause problems, e.g. when another user has steam installed but the current user
        // doesn't have access to it? - NH
        return Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Electronic Arts\EA Desktop");
    }

    /// <summary>
    /// Returns a disposable <see cref="RegistryKey"/>. Caller is responsible for disposing it.
    /// </summary>
    /// <returns>A disposable <see cref="RegistryKey"/>.</returns>
    private RegistryKey? GetEaGamesGeneralsRegistryKey()
    {
        // Check local machine
        // This may or may not cause problems, e.g. when another user has steam installed but the current user
        // doesn't have access to it? - NH
        return Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\EA Games\Command and Conquer Generals Zero Hour");
    }
}