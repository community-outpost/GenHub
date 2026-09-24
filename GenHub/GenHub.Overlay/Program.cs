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
        if (!TryReadConfig(args, out var parsedConfig, out var configExitCode))
        {
            return configExitCode;
        }

        var config = parsedConfig!;
        var overlayIp = IPAddress.Parse(config.OverlayIp);

        if (!TryCreateDevice(config, overlayIp, out var tunDevice, out var attachExitCode))
        {
            return attachExitCode;
        }

        using var device = tunDevice!;
        var relayEndpoint = ResolveRelayEndpoint(config.RelayHost, config.RelayPort);

        using var pump = new TunPacketPump(device, relayEndpoint, config.NetworkId, overlayIp);
        pump.Start();

        Console.WriteLine($"ready interface={device.InterfaceName} ip={config.OverlayIp}");

        var readyPath = args.Length >= 2 ? args[1] + ".ready" : null;
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

    private static bool TryReadConfig(string[] args, out OverlaySidecarConfig? config, out int exitCode)
    {
        config = null;
        if (args.Length != 2 || !string.Equals(args[0], "--config", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: genhub-overlay --config <path>");
            exitCode = OnlineConstants.SidecarExitUsage;
            return false;
        }

        string configContents;
        try
        {
            configContents = File.ReadAllText(args[1]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot read config: {ex.Message}");
            exitCode = OnlineConstants.SidecarExitConfigError;
            return false;
        }

        var parsed = OverlaySidecarConfig.Parse(configContents);
        if (!parsed.Success || parsed.Data is null)
        {
            Console.Error.WriteLine($"Invalid config: {parsed.AllErrors}");
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
                Console.Error.WriteLine($"Wintun device creation failed: {winResult.AllErrors}");
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
                Console.Error.WriteLine($"Linux TUN attach failed: {linuxResult.AllErrors}");
                exitCode = OnlineConstants.SidecarExitAttachFailed;
                return false;
            }

            tunDevice = linuxResult.Data;
        }
        else
        {
            Console.Error.WriteLine("Unsupported operating system for TUN overlay.");
            exitCode = OnlineConstants.SidecarExitAttachFailed;
            return false;
        }

        exitCode = OnlineConstants.SidecarExitSuccess;
        return true;
    }

    private static IPEndPoint ResolveRelayEndpoint(string relayHost, int relayPort)
    {
        var relayIp = IPAddress.TryParse(relayHost, out var parsedRelayIp)
            ? parsedRelayIp
            : Dns.GetHostAddresses(relayHost)[0];
        return new IPEndPoint(relayIp, relayPort);
    }
}
