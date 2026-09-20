using System.Text;

namespace DrawingGame.Server;

public enum LogLevel { Info, Warn, Error }

/// <summary>ログ1件</summary>
public class LogEntry
{
    public DateTime Time { get; init; }
    public LogLevel Level { get; init; }
    public string EventKind { get; init; } = "";
    public string Pc { get; init; } = "";
    public string Pair { get; init; } = "";
    public string Message { get; init; } = "";

    public string LevelText => Level switch
    {
        LogLevel.Info => "INFO",
        LogLevel.Warn => "WARN",
        _ => "ERROR",
    };

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(Time.ToString("yyyy/MM/dd HH:mm:ss")).Append("] [").Append(LevelText).Append(']');
        if (EventKind.Length > 0) sb.Append(" [").Append(EventKind).Append(']');
        if (Pc.Length > 0) sb.Append(" [").Append(Pc).Append(']');
        if (Pair.Length > 0) sb.Append(" [").Append(Pair).Append(']');
        sb.Append(' ').Append(Message);
        return sb.ToString();
    }
}

/// <summary>
/// サーバーのログ管理。
/// 画面へのリアルタイム通知と、日付別ログファイルへの保存を行う。
/// </summary>
public class LogManager
{
    private readonly object _lock = new();
    private readonly List<LogEntry> _entries = new();
    private readonly string _logDir;
    private StreamWriter? _writer;
    private string _currentFileName = "";

    public string LogDirectory => _logDir;

    /// <summary>新規ログが追加された（UIスレッドへはInvokeして渡すこと）</summary>
    public event Action<LogEntry>? EntryAdded;

    public LogManager(string logDir)
    {
        _logDir = logDir;
    }

    public void Start()
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_logDir);
                OpenWriterLocked(DateTime.Now);
                Info("起動", "", "", "サーバーアプリケーションを起動しました");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ログファイルを開けませんでした: {ex.Message}");
            }
        }
    }

    private void OpenWriterLocked(DateTime now)
    {
        var name = $"server_{now:yyyyMMdd}.log";
        if (_currentFileName == name && _writer != null) return;
        _writer?.Dispose();
        _currentFileName = name;
        _writer = new StreamWriter(Path.Combine(_logDir, name), append: true, Encoding.UTF8)
        {
            AutoFlush = true,
        };
    }

    public void Info(string eventKind, string pc, string pair, string message)
        => Add(LogLevel.Info, eventKind, pc, pair, message);

    public void Warn(string eventKind, string pc, string pair, string message)
        => Add(LogLevel.Warn, eventKind, pc, pair, message);

    public void Error(string eventKind, string pc, string pair, string message)
        => Add(LogLevel.Error, eventKind, pc, pair, message);

    public void Add(LogLevel level, string eventKind, string pc, string pair, string message)
    {
        var entry = new LogEntry
        {
            Time = DateTime.Now,
            Level = level,
            EventKind = eventKind,
            Pc = pc,
            Pair = pair,
            Message = message,
        };

        lock (_lock)
        {
            _entries.Add(entry);
            try
            {
                OpenWriterLocked(entry.Time);
                _writer?.WriteLine(entry.ToString());
            }
            catch { /* ファイル書き込み失敗は致命的ではない */ }
        }

        Console.WriteLine(entry.ToString());
        EntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock) return _entries.ToArray();
    }

    public void Shutdown()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
