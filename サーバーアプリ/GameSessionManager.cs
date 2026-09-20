using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// ゲームセッションの生成・破棄・ティッカー駆動を管理する。
/// ISessionHost の実装として、セッションの副作用（送信・ログ・お題取得）をサーバーコアへ橋渡しする。
/// </summary>
public class GameSessionManager : ISessionHost
{
    private readonly object _lock = new();
    private readonly Dictionary<string, GameSession> _sessions = new();
    private readonly ServerCore _core;
    private System.Threading.Timer? _ticker;
    private DateTime _lastTickUtc = DateTime.UtcNow;

    public event Action? SessionsChanged;

    public GameSessionManager(ServerCore core)
    {
        _core = core;
    }

    public void StartTicker()
    {
        _ticker = new System.Threading.Timer(_ => TickAll(), null, 0, 250);
    }

    private void TickAll()
    {
        var now = DateTime.UtcNow;
        var delta = (now - _lastTickUtc).TotalSeconds;
        _lastTickUtc = now;

        GameSession[] sessions;
        lock (_lock) sessions = _sessions.Values.ToArray();
        foreach (var s in sessions)
        {
            try { s.Tick(this, now, delta); }
            catch (Exception ex)
            {
                _core.Log.Error("サーバー内部エラー", "", s.PairId, $"セッションTickで例外: {ex.Message}");
            }
        }
    }

    public GameSession CreateSession(Pair pair)
    {
        var session = new GameSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            PairId = pair.PairId,
            PcA = pair.PcA,
            PcB = pair.PcB,
        };
        lock (_lock) _sessions[pair.PairId] = session;
        _core.Clients.SetPair(pair.PcA, pair.PairId);
        _core.Clients.SetPair(pair.PcB, pair.PairId);
        _core.Log.Info("セッション生成", "", pair.PairId, $"{pair.PairId} のゲームセッションを生成しました");
        // ペアリング完了を両クライアントへ通知する
        SendStateTo(session, pair.PcA);
        SendStateTo(session, pair.PcB);
        SessionsChanged?.Invoke();
        return session;
    }

    public void DestroySession(string pairId)
    {
        GameSession? session;
        lock (_lock)
        {
            if (!_sessions.Remove(pairId, out session)) return;
        }
        _core.Clients.SetPair(session.PcA, null);
        _core.Clients.SetPair(session.PcB, null);
        _core.Log.Info("セッション破棄", "", pairId, $"{pairId} のゲームセッションを破棄しました");
        SessionsChanged?.Invoke();
    }

    public GameSession? GetByPair(string pairId)
    {
        lock (_lock) return _sessions.TryGetValue(pairId, out var s) ? s : null;
    }

    public GameSession? GetByPc(string pcNumber)
    {
        lock (_lock)
            return _sessions.Values.FirstOrDefault(s => s.PcA == pcNumber || s.PcB == pcNumber);
    }

    public IReadOnlyList<GameSession> All()
    {
        lock (_lock) return _sessions.Values.ToArray();
    }

    public void ClearAll()
    {
        lock (_lock) _sessions.Clear();
        SessionsChanged?.Invoke();
    }

    /// <summary>再接続時にクライアントへ現在の正しい状態を送信する</summary>
    public void ResyncClient(GameSession session, string pc)
    {
        SendStateTo(session, pc);
        _core.DrawingRelay.SendSnapshot(session, pc);
    }

    // ==================== ISessionHost 実装 ====================

    public void SendToPc(string pcNumber, NetMessage msg)
    {
        var info = _core.Clients.Get(pcNumber);
        if (info?.Connection != null && !info.Connection.IsClosed)
            info.Connection.Send(msg);
    }

    public bool IsConnected(string pcNumber)
    {
        var info = _core.Clients.Get(pcNumber);
        return info?.IsConnected == true;
    }

    public void Log(LogLevel level, string eventKind, string pc, string pair, string message)
        => _core.Log.Add(level, eventKind, pc, pair, message);

    public QuestionEntry? PickQuestion(string? excludeText)
        => _core.Questions.PickRandom(excludeText);

    public GameSettings CurrentSettings => _core.Settings.Current;

    public int RandomNext(int maxExclusive) => Random.Shared.Next(maxExclusive);

    public void BroadcastState(GameSession session)
    {
        SendStateTo(session, session.PcA);
        SendStateTo(session, session.PcB);
    }

    public void SendStateTo(GameSession session, string pc)
    {
        var role = session.RoleOf(pc);
        var isDrawer = role == Role.Drawer;
        var sendQuestion = isDrawer && session.Question != null
            && session.State is GameState.Drawing or GameState.RoundResult or GameState.GameFinished;

        var msg = new NetMessage
        {
            Type = MessageType.StateSync,
            PairId = session.PairId,
            SessionId = session.SessionId,
            GameState = session.State,
            Round = session.Round,
            TotalRounds = session.TotalRounds,
            RemainingSeconds = session.RemainingSeconds,
            MyRole = role,
            Question = sendQuestion ? session.Question : null,
            AnswerLimit = session.AnswerLimit,
            AnswerCount = session.AnswerCount,
            CorrectCount = session.CorrectCount,
            ResultText = session.ResultText,
            IsReady = session.ReadyOf(pc),
            Seq = DateTime.UtcNow.Ticks,
        };
        SendToPc(pc, msg);
    }

    public void SendTimeSync(GameSession session)
    {
        var msg = new NetMessage
        {
            Type = MessageType.TimeSync,
            PairId = session.PairId,
            SessionId = session.SessionId,
            GameState = session.State,
            RemainingSeconds = session.RemainingSeconds,
            Seq = DateTime.UtcNow.Ticks,
        };
        SendToPc(session.PcA, msg);
        SendToPc(session.PcB, msg);
    }

    public void RelayStroke(GameSession session, string toPc, StrokeData segment)
    {
        var msg = new NetMessage
        {
            Type = MessageType.StrokeRelay,
            PairId = session.PairId,
            SessionId = session.SessionId,
            Stroke = segment,
        };
        SendToPc(toPc, msg);
    }

    public void RelayStrokeEnd(GameSession session, string toPc, long strokeId)
    {
        SendToPc(toPc, new NetMessage
        {
            Type = MessageType.StrokeRelay,
            PairId = session.PairId,
            SessionId = session.SessionId,
            Stroke = new StrokeData { StrokeId = strokeId },
        });
    }

    public void SendStrokeSnapshot(GameSession session, string toPc)
    {
        SendToPc(toPc, new NetMessage
        {
            Type = MessageType.StrokeSnapshot,
            PairId = session.PairId,
            SessionId = session.SessionId,
            Strokes = session.StrokeSnapshot().ToList(),
        });
    }

    public void NotifyAnswerResult(GameSession session, string answererPc, string answer, bool isCorrect)
    {
        var result = new NetMessage
        {
            Type = MessageType.AnswerResult,
            PairId = session.PairId,
            SessionId = session.SessionId,
            Round = session.Round,
            IsCorrect = isCorrect,
            Answer = answer,
            CorrectAnswer = session.Question,
            Question = session.Question,
            AnswerCount = session.AnswerCount,
            AnswerLimit = session.AnswerLimit,
        };
        if (isCorrect)
        {
            // 正解時は両クライアントへ通知
            SendToPc(session.PcA, result);
            SendToPc(session.PcB, result);
        }
        else
        {
            SendToPc(answererPc, result);
        }
    }

    public void SessionChanged(GameSession session) => SessionsChanged?.Invoke();

    // ==================== 緊急停止系 ====================

    public void EmergencyStopAll()
    {
        foreach (var s in All()) s.EmergencyStop(this);
        SessionsChanged?.Invoke();
    }

    public void RestoreAll()
    {
        foreach (var s in All()) s.Restore(this);
        SessionsChanged?.Invoke();
    }
}
