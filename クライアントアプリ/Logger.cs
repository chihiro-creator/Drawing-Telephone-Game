using System.Text;

namespace DrawingGame.Client;

/// <summary>クライアント側のログ（ファイル保存のみ）</summary>
public class Logger
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public Logger(string pcNumber)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "logs");
        try { Directory.CreateDirectory(dir); } catch { }
        _filePath = Path.Combine(dir, $"client_{pcNumber}_{DateTime.Now:yyyyMMdd}.log");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy/MM/dd HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
        Console.WriteLine(line);
    }
}
