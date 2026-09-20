using System.Net.Sockets;
using DrawingGame.Shared;

namespace DrawingGame.Tests;

/// <summary>
/// 実クライアントと同一プロトコルで通信するヘッドレステストクライアント。
/// GUIを使わずにサーバーの通信・ゲーム状態遷移を検証するために使用する。
/// </summary>
public class TestClient : IDisposable
{
    private readonly TcpClient _tcp = new();
    private NetworkStream? _stream;
    private readonly object _lock = new();
    private readonly List<NetMessage> _messages = new();
    private volatile bool _running = true;
    private Thread? _receiveThread;
    private System.Threading.Timer? _pingTimer;

    public string PcNumber { get; }
    public string PairId { get; private set; } = "";
    public GameState GameState { get; private set; } = GameState.WaitingForServer;
    public int Round { get; private set; }
    public int TotalRounds { get; private set; }
    public Role MyRole { get; private set; } = Role.None;
    public string? Question { get; private set; }
    public int RemainingSeconds { get; private set; }
    public int AnswerCount { get; private set; }
    public int AnswerLimit { get; private set; }
    public string? CorrectAnswer { get; private set; }
    public bool EmergencyStopped { get; private set; }
    public bool GotSystemReset { get; private set; }

    public bool Connected => _tcp.Connected;

    public TestClient(string ip, int port, string pcNumber)
    {
        PcNumber = pcNumber;
        _tcp.Connect(ip, port);
        _tcp.NoDelay = true;
        _stream = _tcp.GetStream();

        NetUtil.WriteMessage(_stream, new NetMessage
        {
            Type = MessageType.Hello,
            PcNumber = pcNumber,
            DeviceName = "TestPC",
            Version = "1.0.0",
        });

        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        _receiveThread.Start();

        // 実クライアントと同じくPingを送り続ける（サーバーのハートビートで切断されないように）
        _pingTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                if (_running && _stream != null)
                    NetUtil.WriteMessage(_stream, new NetMessage { Type = MessageType.Ping });
            }
            catch { }
        }, null, 3000, 3000);
    }

    private void ReceiveLoop()
    {
        try
        {
            while (_running)
            {
                var msg = NetUtil.ReadMessage(_stream!);
                if (msg == null) break;
                lock (_lock) _messages.Add(msg);
                Apply(msg);
            }
        }
        catch
        {
            // 接続断
        }
        _running = false;
    }

    private void Apply(NetMessage msg)
    {
        switch (msg.Type)
        {
            case MessageType.Welcome:
                PairId = msg.PairId ?? "";
                break;
            case MessageType.StateSync:
                PairId = msg.PairId ?? PairId;
                GameState = msg.GameState;
                Round = msg.Round;
                TotalRounds = msg.TotalRounds;
                MyRole = msg.MyRole;
                Question = msg.Question;
                AnswerCount = msg.AnswerCount;
                AnswerLimit = msg.AnswerLimit;
                break;
            case MessageType.TimeSync:
                RemainingSeconds = msg.RemainingSeconds;
                break;
            case MessageType.AnswerResult:
                AnswerCount = msg.AnswerCount;
                AnswerLimit = msg.AnswerLimit;
                CorrectAnswer = msg.CorrectAnswer;
                break;
            case MessageType.EmergencyStop:
                EmergencyStopped = true;
                break;
            case MessageType.SystemRestored:
                EmergencyStopped = false;
                break;
            case MessageType.SystemReset:
                GotSystemReset = true;
                break;
            case MessageType.Unpaired:
                PairId = "";
                break;
        }
    }

    public void Send(NetMessage msg)
    {
        if (_stream == null) throw new InvalidOperationException("未接続");
        NetUtil.WriteMessage(_stream, msg);
    }

    public void SendReady() => Send(new NetMessage { Type = MessageType.ReadyOk });

    public void SendStroke(long strokeId, string color, int width, List<StrokePoint> points, bool isEnd)
    {
        Send(new NetMessage
        {
            Type = isEnd ? MessageType.StrokeEnd : MessageType.StrokeSegment,
            Stroke = new StrokeData { StrokeId = strokeId, ColorHex = color, PenWidth = width, Points = points },
        });
    }

    public void SendAnswer(string answer) =>
        Send(new NetMessage { Type = MessageType.AnswerSubmit, Answer = answer });

    public void RequestSnapshot() =>
        Send(new NetMessage { Type = MessageType.RequestSnapshot });

    /// <summary>条件を満たすメッセージを待つ</summary>
    public NetMessage WaitFor(Func<NetMessage, bool> predicate, int timeoutMs, out string? matched)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            lock (_lock)
            {
                var found = _messages.FirstOrDefault(predicate);
                if (found != null)
                {
                    matched = found.Type.ToString();
                    return found;
                }
            }
            Thread.Sleep(50);
        }
        matched = null;
        return null!;
    }

    /// <summary>状態が条件を満たすのを待つ</summary>
    public bool WaitForState(Func<bool> predicate, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    /// <summary>受信済みメッセージから最新のものを取り出す（イベント順で再確認するため）</summary>
    public bool HasMessage(Func<NetMessage, bool> predicate)
    {
        lock (_lock) return _messages.Any(predicate);
    }

    /// <summary>受信履歴をクリアする（古いメッセージを新しいイベントと誤認しないため）</summary>
    public void ClearMessages()
    {
        lock (_lock) _messages.Clear();
    }

    public void Dispose()
    {
        _running = false;
        _pingTimer?.Dispose();
        try { _tcp.Close(); } catch { }
    }
}
