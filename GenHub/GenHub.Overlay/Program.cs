using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Core.Services.Online.Tun;
using System;
using System.IO;
using System.Net;
using System.Threading;

namespace GenHub.Overlay;

/// <summary>
/// Entry point for the overlay sidecar: sets up the TUN interface, pumps packets, and idles until stopped.
/// </summary>
public static class Program
{
    /// <summary>
    /// Main entry point for the sidecar.
    /// </summary>
    /// <param name="args">Program startup arguments.</param>
    /// <returns>The process exit code.</returns>
    public static int Main(string[] args)
    {
        if (!TryReadConfig(args, out var parsedConfig, out var configExitCode, out var configPath))
        {
            return configExitCode;
        }

        var config = parsedConfig!;
        if (!IPAddress.TryParse(config.OverlayIp, out var overlayIp))
        {
            const string err = "Invalid overlay IP address.";
            Console.Error.WriteLine(err);
            WriteErrorFile(configPath, err);
            return OnlineConstants.SidecarExitConfigError;
        }

        if (!TryCreateDevice(config, overlayIp, configPath, out var tunDevice, out var attachExitCode))
        {
            return attachExitCode;
        }

        using var device = tunDevice!;
        if (!TryResolveRelayEndpoint(config.RelayHost, config.RelayPort, configPath, out var relayEndpoint))
        {
            return OnlineConstants.SidecarExitAttachFailed;
        }

        using var pump = new TunPacketPump(device, relayEndpoint, config.NetworkId, overlayIp, prefixLength: config.PrefixLength);
        pump.Start();

        Console.WriteLine($"ready interface={device.InterfaceName} ip={config.OverlayIp}");

        var readyPath = !string.IsNullOrEmpty(configPath) ? configPath + OnlineConstants.SidecarReadyFileSuffix : null;
        if (!string.IsNullOrEmpty(readyPath))
        {
            try
            {
                File.WriteAllText(readyPath, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Warning: Could not create ready marker: {ex.Message}");
            }
        }

        using var cancelled = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancelled.Cancel();
        };

        cancelled.Token.WaitHandle.WaitOne();

        if (!string.IsNullOrEmpty(readyPath))
        {
            try
            {
                if (File.Exists(readyPath))
                {
                    File.Delete(readyPath);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup.
            }
        }

        pump.StopAsync().GetAwaiter().GetResult();
        return OnlineConstants.SidecarExitSuccess;
    }

    private static bool TryReadConfig(string[] args, out OverlaySidecarConfig? config, out int exitCode, out string? configPath)
    {
        config = null;
        configPath = null;
        string? candidateConfigPath = null;
        string? defaultOverlayIp = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                candidateConfigPath = args[++i];
            }
            else if (string.Equals(args[i], "--ip", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                defaultOverlayIp = args[++i];
            }
        }

        if (string.IsNullOrWhiteSpace(candidateConfigPath))
        {
            Console.Error.WriteLine("Usage: genhub-overlay --config <path> [--ip <overlayIp>]");
            exitCode = OnlineConstants.SidecarExitUsage;
            return false;
        }

        configPath = candidateConfigPath;
        string configContents;
        try
        {
            configContents = File.ReadAllText(configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var err = $"Cannot read config: {ex.Message}";
            Console.Error.WriteLine(err);
            WriteErrorFile(configPath, err);
            exitCode = OnlineConstants.SidecarExitConfigError;
            return false;
        }

        var parsed = OverlaySidecarConfig.Parse(configContents, defaultOverlayIp);
        if (!parsed.Success || parsed.Data is null)
        {
            var err = $"Invalid config: {parsed.AllErrors}";
            Console.Error.WriteLine(err);
            WriteErrorFile(configPath, err);
            exitCode = OnlineConstants.SidecarExitConfigError;
            return false;
        }

        config = parsed.Data;
        exitCode = OnlineConstants.SidecarExitSuccess;
        return true;
    }

    private static bool TryCreateDevice(
        OverlaySidecarConfig config,
        IPAddress overlayIp,
        string? configPath,
        out ITunDevice? tunDevice,
        out int exitCode)
    {
        tunDevice = null;
        if (OperatingSystem.IsWindows())
        {
            var winResult = WindowsTunDevice.CreateOrOpen(
                config.InterfaceName,
                overlayIp,
                config.PrefixLength,
                config.Mtu);

            if (!winResult.Success || winResult.Data is null)
            {
                var err = $"Wintun device creation failed: {winResult.AllErrors}";
                Console.Error.WriteLine(err);
                WriteErrorFile(configPath, err);
                exitCode = OnlineConstants.SidecarExitAttachFailed;
                return false;
            }

            tunDevice = winResult.Data;
        }
        else if (OperatingSystem.IsLinux())
        {
            var linuxResult = LinuxTunDevice.Attach(config.InterfaceName, overlayIp);
            if (!linuxResult.Success || linuxResult.Data is null)
            {
                var err = $"Linux TUN attach failed: {linuxResult.AllErrors}";
                Console.Error.WriteLine(err);
                WriteErrorFile(configPath, err);
                exitCode = OnlineConstants.SidecarExitAttachFailed;
                return false;
            }

            tunDevice = linuxResult.Data;
        }
        else
        {
            const string err = "Unsupported operating system for TUN overlay.";
            Console.Error.WriteLine(err);
            WriteErrorFile(configPath, err);
            exitCode = OnlineConstants.SidecarExitAttachFailed;
            return false;
        }

        exitCode = OnlineConstants.SidecarExitSuccess;
        return true;
    }

    private static void WriteErrorFile(string? configPath, string message)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return;
        }

        try
        {
            File.WriteAllText(configPath + OnlineConstants.SidecarErrorFileSuffix, message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Could not write error file: {ex.Message}");
        }
    }

    private static bool TryResolveRelayEndpoint(
        string relayHost,
        int relayPort,
        string? configPath,
        out IPEndPoint relayEndpoint)
    {
        try
        {
            if (IPAddress.TryParse(relayHost, out var parsedRelayIp))
            {
                relayEndpoint = new IPEndPoint(parsedRelayIp, relayPort);
                return true;
            }

            var addresses = Dns.GetHostAddresses(relayHost);
            var ipv4 = Array.Find(addresses, a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            var resolvedIp = ipv4 ?? (addresses.Length > 0 ? addresses[0] : null);
            if (resolvedIp == null)
            {
                throw new InvalidOperationException($"DNS returned no addresses for relay host '{relayHost}'.");
            }

            relayEndpoint = new IPEndPoint(resolvedIp, relayPort);
            return true;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException)
        {
            var err = $"Failed to resolve relay host '{relayHost}': {ex.Message}";
            Console.Error.WriteLine(err);
            WriteErrorFile(configPath, err);
            relayEndpoint = null!;
            return false;
        }
    }
}
