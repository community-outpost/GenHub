using System;
using System.IO;
using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GenHub.Windows.Features.Shortcuts;

/// <summary>
/// Registers the <c>genhub://</c> URI scheme with Windows so OS/browser links open GenHub.
/// </summary>
/// <remarks>
/// <para>
/// Windows resolves custom protocols through <c>HKCU\Software\Classes\&lt;scheme&gt;</c>. Without
/// that key the shell shows an "app not installed" dialog when a <c>genhub://</c> link is clicked.
/// The app already parses <c>genhub://subscribe?url=...</c> from its own command line
/// (<c>GenHub.Core.Helpers.CommandLineParser.ExtractSubscriptionUrl</c>); this registrar wires the
/// OS shell to that path.
/// </para>
/// <para>
/// Writes to <c>HKCU</c> (per-user), so no elevation is required. The registration is idempotent
/// and self-repairs: it rewrites the command only when the executable path has changed, which is
/// what happens every time a debug rebuild or Velopack update lands at a new path.
/// </para>
/// </remarks>
public static class UriSchemeRegistrar
{
    private const string SchemeName = CommandLineConstants.SchemeName;
    private const string ClassesSubKey = @"Software\Classes\" + SchemeName;

    /// <summary>
    /// Registers the <c>genhub://</c> scheme for the current user, pointing at the running
    /// executable. Safe to call on every launch.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void Register(ILogger? logger = null)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            logger?.LogWarning("Could not register genhub:// scheme: executable path unavailable.");
            return;
        }

        try
        {
            var desiredCommand = $"\"{executablePath}\" \"%1\"";
            var desiredProtocol = $"URL:{SchemeName} protocol";
            var desiredIcon = $"{executablePath},0";

            // Check if already registered and up-to-date before performing any writes
            using (var existingClassesKey = Registry.CurrentUser.OpenSubKey(ClassesSubKey, writable: false))
            {
                if (existingClassesKey != null)
                {
                    var existingProtocol = existingClassesKey.GetValue(string.Empty) as string;
                    var existingUrlProtocol = existingClassesKey.GetValue("URL Protocol");

                    using var existingCommandKey = existingClassesKey.OpenSubKey(@"shell\open\command", writable: false);
                    var existingCommand = existingCommandKey?.GetValue(string.Empty) as string;

                    if (string.Equals(existingProtocol, desiredProtocol, StringComparison.OrdinalIgnoreCase) &&
                        existingUrlProtocol != null &&
                        string.Equals(existingCommand, desiredCommand, StringComparison.OrdinalIgnoreCase))
                    {
                        logger?.LogDebug("genhub:// scheme is already registered and up-to-date.");
                        return;
                    }
                }
            }

            using var classesKey = Registry.CurrentUser.CreateSubKey(ClassesSubKey, writable: true);

            // URL Protocol flag tells the shell this is a URI handler, not a normal file type.
            classesKey.SetValue(string.Empty, desiredProtocol);
            classesKey.SetValue("URL Protocol", string.Empty);

            using var iconKey = classesKey.CreateSubKey("DefaultIcon");
            iconKey.SetValue(string.Empty, desiredIcon);

            using var commandKey = classesKey.CreateSubKey(@"shell\open\command");
            commandKey.SetValue(string.Empty, desiredCommand);

            logger?.LogInformation("Registered genhub:// scheme -> {ExecutablePath}", executablePath);
        }
        catch (Exception ex)
        {
            // Registration failure must never block app startup; the in-app subscribe paths still
            // work via direct command-line invocation.
            logger?.LogWarning(ex, "Failed to register genhub:// scheme.");
        }
    }
}
