using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Login38.Core.Diagnostics;

/// <summary>
/// The launcher's log sink: formats once, then fans the line out to the log files
/// and to the in-app log view.
/// </summary>
[ProviderAlias("Launcher")]
public sealed class LauncherLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, LauncherLogger> _loggers = new(StringComparer.Ordinal);
    private readonly LauncherLogOptions _options;
    private readonly ILogBroadcast _broadcast;
    private readonly LauncherLogWriter? _writer;

    public LauncherLoggerProvider(IOptions<LauncherLogOptions> options, ILogBroadcast broadcast)
    {
        _options = options.Value;
        _broadcast = broadcast;
        _writer = _options.WriteToFile ? new LauncherLogWriter(_options) : null;
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new LauncherLogger(this, name, _options.IsStartupCategory(name)));

    private void Emit(string line, bool isStartupDiagnostic)
    {
        _writer?.Write(line, isStartupDiagnostic);
        _broadcast.Publish(line);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _writer?.Dispose();
        _loggers.Clear();
    }

    private sealed class LauncherLogger(LauncherLoggerProvider provider, string category, bool isStartupDiagnostic)
        : ILogger
    {
        /// <summary>Display name: the leading "Login38." is noise in every single line.</summary>
        private readonly string _shortCategory =
            category.StartsWith("Login38.", StringComparison.Ordinal) ? category["Login38.".Length..] : category;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var builder = new StringBuilder(128);
            builder.Append('[').Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("] ");
            builder.Append(Abbreviate(logLevel)).Append(' ');
            builder.Append(_shortCategory).Append(": ");
            builder.Append(formatter(state, exception));

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            provider.Emit(builder.ToString(), isStartupDiagnostic);
        }

        private static string Abbreviate(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}
