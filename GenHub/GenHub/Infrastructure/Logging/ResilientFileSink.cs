using System;
using System.IO;
using System.Text;
using System.Threading;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace GenHub.Infrastructure.Logging;

/// <summary>
/// A resilient Serilog log event sink that dynamically ensures parent directories exist,
/// writes with shared read-write access, and seamlessly handles external file deletion
/// and truncation without losing subsequent logs.
/// </summary>
public sealed class ResilientFileSink : ILogEventSink, IDisposable
{
    private static readonly object FileLock = new();
    private readonly string _filePath;
    private readonly ITextFormatter _formatter;
    private readonly Encoding _encoding;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResilientFileSink"/> class.
    /// </summary>
    /// <param name="filePath">Target log file path.</param>
    /// <param name="outputTemplate">Log output format template.</param>
    /// <param name="formatProvider">Format provider.</param>
    /// <param name="encoding">File encoding (defaults to UTF-8 without BOM).</param>
    public ResilientFileSink(
        string filePath,
        string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        IFormatProvider? formatProvider = null,
        Encoding? encoding = null)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _formatter = new MessageTemplateTextFormatter(outputTemplate, formatProvider);
        _encoding = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        EnsureDirectoryExists();
    }

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        if (_disposed || logEvent == null)
        {
            return;
        }

        lock (FileLock)
        {
            const int maxRetries = 3;
            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    EnsureDirectoryExists();

                    using var stream = new FileStream(
                        _filePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite);

                    using var writer = new StreamWriter(stream, _encoding);
                    _formatter.Format(logEvent, writer);
                    writer.Flush();
                    break;
                }
                catch (IOException) when (attempt < maxRetries)
                {
                    Thread.Sleep(10);
                }
                catch (UnauthorizedAccessException) when (attempt < maxRetries)
                {
                    Thread.Sleep(10);
                }
                catch
                {
                    // Prevent logging exceptions from bubbling up to caller
                    break;
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }

    private void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
