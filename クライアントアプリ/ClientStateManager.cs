using DrawingGame.Shared;

namespace DrawingGame.Client;

/// <summary>
/// サーバーから受信した状態を保持するクライアント側の状態ビュー。
/// ゲームの正しい状態はサーバーが決定し、クライアントは通知された内容をそのまま表示する。
/// </summary>
public class ClientStateManager
{
    private readonly object _lock = new();

    public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;
    public string ConnectionMessage { get; private set; } = "未接続";
    public GameState GameState { get; private set; } = GameState.WaitingForServer;
    public string PairId { get; private set; } = "";
    public int Round { get; private set; }
    public int TotalRounds { get; private set; }
    public Role MyRole { get; private set; } = Role.None;
    public int RemainingSeconds { get; private set; }
    public string? Question { get; private set; }
    public int AnswerCount { get; private set; }
    public int AnswerLimit { get; private set; }
    public int CorrectCount { get; private set; }
    public string? ResultText { get; private set; }
    public bool IsReady { get; private set; }
    public bool EmergencyStopped { get; private set; }
    public string? InfoMessage { get; private set; }
    public string? LastAnswerResult { get; private set; }

    /// <summary>最後の回答が正解だったかどうか（null = 未回答）</summary>
    public bool? LastAnswerIsCorrect { get; private set; }

    /// <summary>回答結果を受信するたびに増える連番（同じ文言でも再表示できるようにするための判定用）</summary>
    public int AnswerResultSeq { get; private set; }

    /// <summary>サーバーから回答エラー通知があった</summary>
    public string? LastError { get; private set; }

    /// <summary>UI更新イベント（UIスレッドへのInvokeは呼び出し側で行う）</summary>
    public event Action? Changed;

    /// <summary>サーバーから描画中継を受信</summary>
    public event Action<StrokeData>? StrokeRelayed;

    /// <summary>ストロークスナップショット受信（再接続時）</summary>
    public event Action<List<StrokeData>>? StrokeSnapshotReceived;

    /// <summary>キャンバス全消去指示</summary>
    public event Action? StrokeCleared;

    /// <summary>システムリセット要求（アプリ再起動）</summary>
    public event Action? SystemResetRequested;

    /// <summary>ペア解除通知</summary>
    public event Action? Unpaired;

    /// <summary>システム復元通知</summary>
    public event Action? SystemRestored;

    public void SetConnection(ConnectionState state, string message)
    {
        lock (_lock)
        {
            ConnectionState = state;
            ConnectionMessage = message;
            if (state == ConnectionState.Connected && GameState == GameState.WaitingForServer)
                GameState = GameState.WaitingForPair;
        }
        Changed?.Invoke();
    }

    /// <summary>サーバーメッセージを適用する</summary>
    public void Apply(NetMessage msg)
    {
        switch (msg.Type)
        {
            case MessageType.Welcome:
                lock (_lock)
                {
                    GameState = GameState.WaitingForPair;
                    PairId = msg.PairId ?? "";
                    InfoMessage = null;
                }
                break;

            case MessageType.StateSync:
                lock (_lock)
                {
                    GameState = msg.GameState;
                    PairId = msg.PairId ?? PairId;
                    Round = msg.Round;
                    TotalRounds = msg.TotalRounds;
                    MyRole = msg.MyRole;
                    RemainingSeconds = msg.RemainingSeconds;
                    Question = msg.Question;
                    AnswerLimit = msg.AnswerLimit;
                    AnswerCount = msg.AnswerCount;
                    CorrectCount = msg.CorrectCount;
                    ResultText = msg.ResultText;
                    IsReady = msg.IsReady;
                    InfoMessage = msg.Message;
                    LastError = null;
                    // StateSync が緊急停止状態の正とする（復元・再同期で解除されるようにする）
                    EmergencyStopped = msg.GameState == GameState.EmergencyStopped;
                    if (msg.GameState == GameState.WaitingForPair) PairId = "";
                }
                break;

            case MessageType.TimeSync:
                lock (_lock)
                {
                    GameState = msg.GameState == GameState.Unknown ? GameState : msg.GameState;
                    RemainingSeconds = msg.RemainingSeconds;
                }
                break;

            case MessageType.StrokeRelay:
                if (msg.Stroke != null) StrokeRelayed?.Invoke(msg.Stroke);
                return;

            case MessageType.StrokeSnapshot:
                if (msg.Strokes != null) StrokeSnapshotReceived?.Invoke(msg.Strokes);
                return;

            case MessageType.StrokeClear:
                StrokeCleared?.Invoke();
                return;

            case MessageType.AnswerResult:
                lock (_lock)
                {
                    AnswerCount = msg.AnswerCount;
                    AnswerLimit = msg.AnswerLimit;
                    AnswerResultSeq++;
                    LastAnswerIsCorrect = msg.IsCorrect;
                    LastAnswerResult = msg.IsCorrect
                        ? $"正解！お題は「{msg.CorrectAnswer}」でした"
                        : msg.AnswerLimit > 0
                            ? $"不正解！残り {Math.Max(0, msg.AnswerLimit - msg.AnswerCount)}回"
                            : "不正解！制限時間が無くなるまで何回でも答えられます";
                    InfoMessage = LastAnswerResult;
                }
                break;

            case MessageType.RoundResult:
                lock (_lock)
                {
                    InfoMessage = msg.IsCorrect
                        ? $"Round {msg.Round} 正解！お題は「{msg.CorrectAnswer}」でした"
                        : $"Round {msg.Round} 時間切れ…答えは「{msg.CorrectAnswer}」でした";
                }
                break;

            case MessageType.GameFinished:
                lock (_lock)
                {
                    CorrectCount = msg.CorrectCount;
                    TotalRounds = msg.TotalRounds;
                    InfoMessage = $"ゲーム終了！正解数: {msg.CorrectCount} / {msg.TotalRounds}";
                }
                break;

            case MessageType.EmergencyStop:
                lock (_lock)
                {
                    EmergencyStopped = true;
                    GameState = GameState.EmergencyStopped;
                    InfoMessage = msg.Message ?? "緊急停止ボタンが押されました。しばらくお待ちください。";
                }
                break;

            case MessageType.SystemRestored:
                lock (_lock)
                {
                    EmergencyStopped = false;
                    GameState = GameState.WaitingForReady;
                    Question = null;
                    ResultText = null;
                    InfoMessage = msg.Message ?? "システムが復元されました";
                }
                SystemRestored?.Invoke();
                break;

            case MessageType.SystemReset:
                SystemResetRequested?.Invoke();
                return;

            case MessageType.Unpaired:
                lock (_lock)
                {
                    EmergencyStopped = false;
                    GameState = GameState.WaitingForPair;
                    PairId = "";
                    Question = null;
                    Round = 0;
                    MyRole = Role.None;
                }
                Unpaired?.Invoke();
                break;

            case MessageType.Error:
                lock (_lock) LastError = msg.Message;
                break;
        }
        Changed?.Invoke();
    }

    public ClientStateView GetView()
    {
        lock (_lock)
        {
            return new ClientStateView
            {
                ConnectionState = ConnectionState,
                ConnectionMessage = ConnectionMessage,
                GameState = GameState,
                PairId = PairId,
                Round = Round,
                TotalRounds = TotalRounds,
                MyRole = MyRole,
                RemainingSeconds = RemainingSeconds,
                Question = Question,
                AnswerCount = AnswerCount,
                AnswerLimit = AnswerLimit,
                CorrectCount = CorrectCount,
                ResultText = ResultText,
                IsReady = IsReady,
                EmergencyStopped = EmergencyStopped,
                InfoMessage = InfoMessage,
                LastError = LastError,
                LastAnswerResult = LastAnswerResult,
                LastAnswerIsCorrect = LastAnswerIsCorrect,
                AnswerResultSeq = AnswerResultSeq,
            };
        }
    }
}

public class ClientStateView
{
    public ConnectionState ConnectionState { get; init; }
    public string ConnectionMessage { get; init; } = "";
    public GameState GameState { get; init; }
    public string PairId { get; init; } = "";
    public int Round { get; init; }
    public int TotalRounds { get; init; }
    public Role MyRole { get; init; }
    public int RemainingSeconds { get; init; }
    public string? Question { get; init; }
    public int AnswerCount { get; init; }
    public int AnswerLimit { get; init; }
    public int CorrectCount { get; init; }
    public string? ResultText { get; init; }
    public bool IsReady { get; init; }
    public bool EmergencyStopped { get; init; }
    public string? InfoMessage { get; init; }
    public string? LastError { get; init; }
    public string? LastAnswerResult { get; init; }
    public bool? LastAnswerIsCorrect { get; init; }
    public int AnswerResultSeq { get; init; }
}
