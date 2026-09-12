using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Markdown.Avalonia.Utils;

namespace GenHub.Infrastructure.Markdown;

/// <summary>
/// A safe hyperlink command for Markdown viewers that only permits opening HTTP and HTTPS URLs,
/// preventing arbitrary scheme execution (e.g. file://, UNC paths, executable shell outs).
/// </summary>
public sealed class SafeMarkdownHyperlinkCommand : ICommand
{
    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged
    {
        add { /* CanExecute condition is invariant for this command */ }
        remove { /* CanExecute condition is invariant for this command */ }
    }

    /// <inheritdoc/>
    public bool CanExecute(object? parameter)
    {
        return IsSafeUrl(parameter);
    }

    /// <inheritdoc/>
    public void Execute(object? parameter)
    {
        if (!IsSafeUrl(parameter, out var safeUri))
        {
            return;
        }

        try
        {
            DefaultHyperlinkCommand.GoTo(safeUri.AbsoluteUri);
        }
        catch (Win32Exception)
        {
            // Silently ignore browser launch errors.
        }
        catch (InvalidOperationException)
        {
            // Silently ignore browser launch errors.
        }
        catch (IOException)
        {
            // Silently ignore browser launch errors.
        }
        catch (NotSupportedException)
        {
            // Silently ignore browser launch errors.
        }
        catch (UnauthorizedAccessException)
        {
            // Silently ignore browser launch errors.
        }
        catch (ExternalException)
        {
            // Silently ignore browser launch errors.
        }
    }

    private static bool IsSafeUrl(object? parameter)
    {
        return IsSafeUrl(parameter, out _);
    }

    private static bool IsSafeUrl(object? parameter, [NotNullWhen(true)] out Uri? safeUri)
    {
        safeUri = null;
        if (parameter is null)
        {
            return false;
        }

        var urlString = parameter.ToString()?.Trim();
        if (string.IsNullOrEmpty(urlString))
        {
            return false;
        }

        if (Uri.TryCreate(urlString, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            safeUri = uri;
            return true;
        }

        return false;
    }
}
