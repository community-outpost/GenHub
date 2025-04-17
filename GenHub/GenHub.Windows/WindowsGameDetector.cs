﻿using System;
using System.Collections.Generic;
using System.IO;
using GenHub.Core;
using Microsoft.Win32;

namespace GenHub.Windows;

public class WindowsGameDetector : IGameDetector
{
    public bool IsSteamInstalled => DoesSteamPathExist();

    public bool IsVanillaInstalled { get; private set; }
    public string VanillaGamePath { get; private set; } = "";

    public bool IsZeroHourInstalled { get; private set; }
    public string ZeroHourGamePath { get; private set; } = "";

    public void Detect()
    {
        if(!TryGetSteamLibraries(out var libraryPaths))
            return;

        foreach (var lib in libraryPaths!)
        {
            string gamePath;

            // Fetch generals
            if (!IsVanillaInstalled)
            {
                gamePath = Path.Combine(lib, "Command and Conquer Generals");
                if (Directory.Exists(gamePath))
                {
                    IsVanillaInstalled = true;
                    VanillaGamePath = gamePath;
                }
            }

            // Fetch zero hour
            if (!IsZeroHourInstalled)
            {
                gamePath = Path.Combine(lib, "Command & Conquer Generals - Zero Hour");
                {
                    IsZeroHourInstalled = true;
                    ZeroHourGamePath = gamePath;
                }
            }
        }

        Console.WriteLine($"Is Vanilla installed? {IsVanillaInstalled} - If yes then it's here: {VanillaGamePath}");
        Console.WriteLine($"Is Zero Hour installed? {IsZeroHourInstalled} - If yes then it's here: {ZeroHourGamePath}");
    }

    // TODO: Move everything steam related into its own 'SteamDetector' class, to not bloat this class and make it easier to also work with EA App and other installations
    /// <summary>
    /// Tries to fetch all steam library folders containing installed games.
    /// </summary>
    /// <param name="steamLibraryPaths">An array of full paths to the common directories for each valid library found.
    /// This will be null if the method returns <c>false</c>.</param>
    /// <returns><c>true</c> if at least one steam library path was found.</returns>
    /// <remarks>
    /// This method reads the "libraryfolders.vdf" file from the main steam installation directory.
    /// </remarks>
    public bool TryGetSteamLibraries(out string[]? steamLibraryPaths)
    {
        steamLibraryPaths = null;

        // Try to get the steam path in order to fetch the libraryfolders.vdf
        if (!TryGetSteamPath(out var steamPath))
            return false;

        // Find libraryfolders.vdf
        var libraryFile = Path.Combine(steamPath!, "steamapps", "libraryfolders.vdf");

        if(!File.Exists(libraryFile))
            return false;

        List<string> results = [];

        // Read all the paths in the vdf and already make them usable.
        // "C:\\Program Files (x86)\\Steam" to "C:\Program Files (x86)\Steam\steamapps\common"
        foreach (var line in File.ReadAllLines(libraryFile))
        {
            if (!line.Contains("\"path\""))
                continue;

            var parts = line.Split('"');
            if(parts.Length < 4)
                continue;

            var dir = parts[3].Trim();

            if (Directory.Exists(dir))
            {
                var path = Path.Combine(dir.Replace(@"\\", @"\"), "steamapps", "common");
                results.Add(path);
            }
        }

        if (results.Count == 0)
            return false;

        steamLibraryPaths = results.ToArray();

        return true;
    }

    /// <summary>
    /// Checks if steam is installed by looking up the installation path of steam in the windows registry
    /// </summary>
    /// <returns><c>true</c> if the steam registry key was found</returns>
    private bool DoesSteamPathExist()
    {
        using var key = GetSteamRegistryKey();
        return key != null;
    }

    /// <summary>
    /// Tries to fetch the installation path of steam from the windows registry.
    /// </summary>
    /// <param name="path">Returns the installation path if successful; otherwise, an empty string</param>
    /// <returns><c>true</c> if the installation path of steam was found</returns>
    private bool TryGetSteamPath(out string? path)
    {
        path = string.Empty;

        using var key = GetSteamRegistryKey();
        if (key == null)
            return false;

        // Find the steam path in current user registry
        path = key.GetValue("SteamPath") as string;

        if (string.IsNullOrEmpty(path))
        {
            // Find the steam path in local machine registry
            // This may or may not cause problems, e.g. when another user has steam installed but the current user
            // doesn't have access to it? - NH
            path = key.GetValue("InstallPath") as string;
            return !string.IsNullOrEmpty(path);
        }

        return true;
    }

    /// <summary>
    /// Returns a disposable RegistryKey. Caller is responsible for disposing it.
    /// </summary>
    /// <returns>Disposable RegistryKey</returns>
    private RegistryKey? GetSteamRegistryKey()
    {
        // Check current user
        var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
        if (key != null)
            return key;

        // Check local machine if the current user doesn't have steam installed.
        // This may or may not cause problems, e.g. when another user has steam installed but the current user
        // doesn't have access to it? - NH
        key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        return key;
    }
}