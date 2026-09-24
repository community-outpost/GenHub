using GenHub.Core.Constants;
using GenHub.Core.Models.Online;
using GenHub.Core.Services.Online.Tun;
using System;
using System.IO;
using System.Threading;

namespace GenHub.Overlay;

/// <summary>
/// Entry point for the Linux overlay sidecar: attaches the persistent TUN interface and idles until stopped.
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

        var attached = LinuxTunDevice.Attach(parsed.Data.InterfaceName);
        if (!attached.Success || attached.Data is null)
        {
            Console.Error.WriteLine($"TUN attach failed: {attached.AllErrors}");
            return OnlineConstants.SidecarExitAttachFailed;
        }

        using var device = attached.Data;
        Console.WriteLine($"ready interface={device.InterfaceName} ip={parsed.Data.OverlayIp}");

        using var cancelled = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancelled.Cancel();
        };
        cancelled.Token.WaitHandle.WaitOne();

        return OnlineConstants.SidecarExitSuccess;
    }
}
