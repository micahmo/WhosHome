using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace WhosHome.Server.Logging;

/// <summary>
/// One line per entry: <c>[14:22:20 INF] PresenceService: Report from Micah...</c>
/// <para>
/// The default console formatter spends two lines and a full namespace on every entry, which buries
/// the message. This keeps the three things worth scanning for in <c>docker logs</c>: when, how bad,
/// and who said it. Written by hand rather than pulling in a logging framework, because formatting
/// a line of text is not worth a dependency.
/// </para>
/// </summary>
public sealed class CompactConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "compact";

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        string message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception) ?? string.Empty;
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        textWriter.Write('[');
        textWriter.Write(DateTimeOffset.Now.ToString("HH:mm:ss"));
        textWriter.Write(' ');
        textWriter.Write(Abbreviate(logEntry.LogLevel));
        textWriter.Write("] ");
        textWriter.Write(ShortCategory(logEntry.Category));
        textWriter.Write(": ");
        textWriter.Write(message);

        // Exceptions stay on their own lines. A stack trace is the one thing worth breaking the
        // one-line rule for, since folding it up would make it unreadable.
        if (logEntry.Exception is not null)
        {
            textWriter.Write(Environment.NewLine);
            textWriter.Write(logEntry.Exception.ToString());
        }

        textWriter.Write(Environment.NewLine);
    }

    /// <summary>Three characters, so the level column never shifts and stays scannable.</summary>
    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "___",
    };

    /// <summary>
    /// The last segment of the category, so <c>WhosHome.Server.Presence.PresenceService</c> reads as
    /// <c>PresenceService</c>. Framework categories collapse the same way, which is what makes an
    /// unexpected entry from somewhere else stand out rather than blend in.
    /// </summary>
    private static string ShortCategory(string category)
    {
        int lastDot = category.LastIndexOf('.');
        return lastDot < 0 || lastDot == category.Length - 1 ? category : category[(lastDot + 1)..];
    }
}
