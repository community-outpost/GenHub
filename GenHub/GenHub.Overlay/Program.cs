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
public class Program
{
    /// <summary>
    /// Main entry point for the sidecar.
    /// </summary>
    /// <param name="args">Program startup arguments.</param>
    /// <returns>The process exit code.</returns>
    public static int Main(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--config", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: genhub-overlay --config <path>");
            return OnlineConstants.SidecarExitUsage;
        }

        string configContents;
        try
        {
            configContents = File.ReadAllText(args[1]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot read config: {ex.Message}");
            return OnlineConstants.SidecarExitConfigError;
        }

        var parsed = OverlaySidecarConfig.Parse(configContents);
        if (!parsed.Success || parsed.Data is null)
        {
            Console.Error.WriteLine($"Invalid config: {parsed.AllErrors}");
            return OnlineConstants.SidecarExitConfigError;
        }

        var overlayIp = IPAddress.Parse(parsed.Data.OverlayIp);
        ITunDevice? tunDevice = null;

        if (OperatingSystem.IsWindows())
        {
            var winResult = WindowsTunDevice.CreateOrOpen(
                parsed.Data.InterfaceName,
                overlayIp,
                parsed.Data.PrefixLength,
                parsed.Data.Mtu);

            if (!winResult.Success || winResult.Data is null)
            {
                Console.Error.WriteLine($"Wintun device creation failed: {winResult.AllErrors}");
                return OnlineConstants.SidecarExitAttachFailed;
            }

            tunDevice = winResult.Data;
        }
        else if (OperatingSystem.IsLinux())
        {
            var linuxResult = LinuxTunDevice.Attach(parsed.Data.InterfaceName, overlayIp);
            if (!linuxResult.Success || linuxResult.Data is null)
            {
                Console.Error.WriteLine($"Linux TUN attach failed: {linuxResult.AllErrors}");
                return OnlineConstants.SidecarExitAttachFailed;
            }

            tunDevice = linuxResult.Data;
        }
        else
        {
            Console.Error.WriteLine("Unsupported operating system for TUN overlay.");
            return OnlineConstants.SidecarExitAttachFailed;
        }

        using var device = tunDevice;

        var relayIp = IPAddress.TryParse(parsed.Data.RelayHost, out var parsedRelayIp)
            ? parsedRelayIp
            : Dns.GetHostAddresses(parsed.Data.RelayHost)[0];
        var relayEndpoint = new IPEndPoint(relayIp, parsed.Data.RelayPort);

        using var pump = new TunPacketPump(
            device,
            relayEndpoint,
            parsed.Data.NetworkId,
            overlayIp);

        pump.Start();

        Console.WriteLine($"ready interface={device.InterfaceName} ip={parsed.Data.OverlayIp}");

        using var cancelled = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancelled.Cancel();
        };

        cancelled.Token.WaitHandle.WaitOne();

        pump.StopAsync().GetAwaiter().GetResult();
        return OnlineConstants.SidecarExitSuccess;
    }
}
