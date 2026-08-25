namespace Login38.Core.Diagnostics;

/// <summary>
/// Appends formatted lines to the launcher's two log files.
/// </summary>
/// <remarks>
/// The files are opened once and held, rather than reopened per line as the
/// reference implementation did. Writes are flushed immediately so a crash still
/// leaves a usable tail — which is the whole point of these files.
/// </remarks>
internal sealed class LauncherLogWriter : IDisposable
{
    private readonly Lock _gate = new();
    private readonly LauncherLogOptions _options;

    private StreamWriter? _general;
    private StreamWriter? _startup;
    private bool _disposed;

    public LauncherLogWriter(LauncherLogOptions options) => _options = options;

    public void Write(string line, bool isStartupDiagnostic)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var writer = isStartupDiagnostic
                ? _startup ??= TryOpen(_options.StartupFilePath)
                : _general ??= TryOpen(_options.GeneralFilePath);

            // TryOpen returns null when the directory is not writable — a read-only
            // install location, say. Losing the log is acceptable; failing to launch
            // the game because of it is not.
            writer?.WriteLine(line);
        }
    }

    private static StreamWriter? TryOpen(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var writer = new StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine();
            writer.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"==== session started {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} ===="));
            return writer;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _general?.Dispose();
            _startup?.Dispose();
            _general = null;
            _startup = null;
        }
    }
}
