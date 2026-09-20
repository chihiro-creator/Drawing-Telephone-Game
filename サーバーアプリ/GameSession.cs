using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// GameSession が外部（サーバーコア）へ要求する副作用を抽象化したインターフェース。
/// セッションはこのインターフェース経由でのみ通信・ログ・お題取得を行う。
/// </summary>
public interface ISessionHost
{
    void SendToPc(string pcNumber, NetMessage msg);
    bool IsConnected(string pcNumber);
    void Log(LogLevel level, string eventKind, string pc, string pair, string message);

    QuestionEntry? PickQuestion(string? excludeText);
    GameSettings CurrentSettings { get; }
    int RandomNext(int maxExclusive);

    /// <summary>両クライアントへStateSyncを送信（役割に応じた情報量で）</summary>
    void BroadcastState(GameSession session);
    /// <summary>残り時間同期を両クライアントへ送信</summary>
    void SendTimeSync(GameSession session);
    /// <summary>描画データをパートナーへ中継</summary>
    void RelayStroke(GameSession session, string toPc, StrokeData segment);
    /// <summary>ストローク終了をパートナーへ通知</summary>
    void RelayStrokeEnd(GameSession session, string toPc, long strokeId);
    /// <summary>指定PCへ現在の全ストロークを送信（再接続時）</summary>
    void SendStrokeSnapshot(GameSession session, string toPc);

    /// <summary>回答結果を通知（正解時は両方、不正解時は回答者へ）</summary>
    void NotifyAnswerResult(GameSession session, string answererPc, string answer, bool isCorrect);

    void SessionChanged(GameSession session);
}

/// <summary>
/// 1ペアのゲームセッション。
/// サーバー側で唯一の状態管理者として、ラウンド・役割・タイマー・お題・
/// ストローク・準備状態を管理する。タイマーはサーバー時刻（UTC）基準。
/// </summary>
public class GameSession
{
    public const int CountdownSeconds = 5;
    public const int ResultDisplaySeconds = 5;

    public required string SessionId { get; init; }
    public required string PairId { get; init; }
    public required string PcA { get; init; }
    public required string PcB { get; init; }

    public GameState State { get; private set; } = GameState.WaitingForReady;

    public int Round { get; private set; }
    public int TotalRounds { get; private set; } = 3;

    public Role RoleA { get; private set; } = Role.None;
    public Role RoleB { get; private set; } = Role.None;

    public string? Question { get; private set; }
    private List<string> _acceptableAnswers = new();

    /// <summary>正解として許容する表記のリスト（ゲーム設定の正解表記）</summary>
    public IReadOnlyList<string> AcceptableAnswers => _acceptableAnswers;

    public int RemainingSeconds { get; private set; }
    public DateTime RoundEndUtc { get; private set; }

    public bool ReadyA { get; private set; }
    public bool ReadyB { get; private set; }

    public int AnswerCount { get; private set; }
    public int AnswerLimit { get; private set; }
    public int CorrectCount { get; private set; }

    public string? ResultText { get; private set; }
    private bool _peerDisconnectedNotified;
    private int _lastSentRemaining = -1;

    public List<StrokeData> CompletedStrokes { get; } = new();
    public StrokeData? CurrentStroke { get; private set; }

    public GameSettings Settings { get; private set; } = new();

    private readonly object _lock = new();

    public void WithLock(Action action)
    {
        lock (_lock) action();
    }

    public bool IsDrawer(string pc) => pc == PcA ? RoleA == Role.Drawer : pc == PcB ? RoleB == Role.Drawer : false;
    public bool IsGuesser(string pc) => pc == PcA ? RoleA == Role.Guesser : pc == PcB ? RoleB == Role.Guesser : false;
    public string PartnerOf(string pc) => pc == PcA ? PcB : pc == PcB ? PcA : "";
    public bool ReadyOf(string pc) => pc == PcA ? ReadyA : pc == PcB ? ReadyB : false;

    /// <summary>準備OK受理（サーバーが唯一の準備状態管理者）</summary>
    public void HandleReady(string pc, ISessionHost host)
    {
        lock (_lock)
        {
            if (State != GameState.WaitingForReady)
            {
                host.Log(LogLevel.Warn, "不正なゲーム状態", pc, PairId, $"準備OKはWaitingForReadyでのみ受付（現在: {State}）");
                return;
            }
            if (pc == PcA) ReadyA = true;
            else if (pc == PcB) ReadyB = true;
            else return;

            host.Log(LogLevel.Info, "準備OK", pc, PairId, $"{pc} が準備OKを押しました（{ReadyOf(PcA) && ReadyOf(PcB)}）");

            if (ReadyA && ReadyB)
            {
                host.Log(LogLevel.Info, "ゲーム開始", "", PairId, $"{PairId} の両クライアントが準備完了。ゲームを開始します");
                StartGame(host);
            }
            else
            {
                host.BroadcastState(this);
            }
        }
    }

    private void StartGame(ISessionHost host)
    {
        Settings = host.CurrentSettings.Clone();
        TotalRounds = Settings.Rounds;
        AnswerLimit = Settings.AnswerLimit;
        Round = 0;
        CorrectCount = 0;
        ReadyA = ReadyB = false;
        ResultText = null;

        State = GameState.Countdown;
        RemainingSeconds = CountdownSeconds;
        RoundEndUtc = DateTime.UtcNow.AddSeconds(CountdownSeconds);
        _lastSentRemaining = -1;

        host.Log(LogLevel.Info, "ゲーム開始", "", PairId,
            $"{PairId} がゲームを開始（{TotalRounds}ラウンド, {Settings.TimeSeconds}秒, 回答制限: {(AnswerLimit == 0 ? "無制限" : $"{AnswerLimit}回")}）");
        host.BroadcastState(this);
    }

    /// <summary>
    /// タイマー駆動（GameSessionManagerのティッカーから250msごとに呼ばれる）。
    /// サーバー時刻を基準に状態遷移を行う。
    /// </summary>
    public void Tick(ISessionHost host, DateTime nowUtc, double deltaSeconds)
    {
        lock (_lock)
        {
            if (State == GameState.WaitingForReady || State == GameState.EmergencyStopped) return;

            // メンバー切断中の場合はタイマーを凍結する（ゲーム状態が壊れないように）
            bool anyDisconnected = !host.IsConnected(PcA) || !host.IsConnected(PcB);
            if (anyDisconnected)
            {
                RoundEndUtc = RoundEndUtc.AddSeconds(deltaSeconds);
                if (!_peerDisconnectedNotified)
                {
                    _peerDisconnectedNotified = true;
                    host.Log(LogLevel.Warn, "切断", "", PairId, "ゲーム中のメンバーが切断。タイマーを一時停止します");
                    foreach (var pc in new[] { PcA, PcB })
                    {
                        if (host.IsConnected(pc))
                        {
                            host.SendToPc(pc, new NetMessage
                            {
                                Type = MessageType.StateSync,
                                GameState = State,
                                PairId = PairId,
                                SessionId = SessionId,
                                Round = Round,
                                TotalRounds = TotalRounds,
                                MyRole = RoleOf(pc),
                                Message = "相手PCの接続が切断されました。復帰を待っています…",
                            });
                        }
                    }
                }
                return;
            }

            if (_peerDisconnectedNotified)
            {
                _peerDisconnectedNotified = false;
                host.Log(LogLevel.Info, "再接続", "", PairId, "全メンバーが接続されました。ゲーム状態を同期します");
                host.BroadcastState(this);
            }

            switch (State)
            {
                case GameState.Countdown:
                    if (nowUtc >= RoundEndUtc) StartRound(host, nowUtc);
                    else SyncRemaining(host, CountdownSeconds);
                    break;

                case GameState.Drawing:
                    if (nowUtc >= RoundEndUtc) FinishRoundByTimeout(host, nowUtc);
                    else SyncRemaining(host, Settings.TimeSeconds);
                    break;

                case GameState.RoundResult:
                    if (nowUtc >= RoundEndUtc)
                    {
                        if (Round >= TotalRounds) FinishGame(host, nowUtc);
                        else BeginNextRound(host, nowUtc);
                    }
                    break;

                case GameState.GameFinished:
                    if (nowUtc >= RoundEndUtc) ResetToReady(host);
                    break;
            }
        }
    }

    private void SyncRemaining(ISessionHost host, int maxSeconds)
    {
        int remaining = Math.Max(0, (int)Math.Ceiling((RoundEndUtc - DateTime.UtcNow).TotalSeconds));
        remaining = Math.Min(remaining, maxSeconds);
        RemainingSeconds = remaining;
        if (remaining != _lastSentRemaining)
        {
            _lastSentRemaining = remaining;
            host.SendTimeSync(this);
        }
    }

    private void StartRound(ISessionHost host, DateTime nowUtc)
    {
        Round++;
        AssignRoles(host);

        Question = null;
        _acceptableAnswers.Clear();
        var question = host.PickQuestion(Question);
        if (question != null)
        {
            Question = question.Text;
            _acceptableAnswers = question.Answers.Count > 0 ? new List<string>(question.Answers) : new List<string> { question.Text };
        }
        AnswerCount = 0;
        ResultText = null;
        RemainingSeconds = Settings.TimeSeconds;
        RoundEndUtc = nowUtc.AddSeconds(Settings.TimeSeconds);
        _lastSentRemaining = -1;

        CompletedStrokes.Clear();
        CurrentStroke = null;

        State = GameState.Drawing;

        host.Log(LogLevel.Info, "ラウンド開始", "", PairId,
            $"{PairId} Round {Round}/{TotalRounds} 開始（{RoleA}: {PcA} / {RoleB}: {PcB}）");
        host.BroadcastState(this);
        foreach (var pc in new[] { PcA, PcB })
        {
            host.SendToPc(pc, new NetMessage { Type = MessageType.StrokeClear, PairId = PairId, SessionId = SessionId });
        }
    }

    private void AssignRoles(ISessionHost host)
    {
        if (Round == 1)
        {
            // 1ラウンド目はランダム
            bool aDrawer = host.RandomNext(2) == 0;
            RoleA = aDrawer ? Role.Drawer : Role.Guesser;
            RoleB = aDrawer ? Role.Guesser : Role.Drawer;
        }
        else
        {
            // 2ラウンド目以降は前ラウンドと逆の役割
            RoleA = Opposite(RoleA);
            RoleB = Opposite(RoleB);
        }
    }

    private static Role Opposite(Role r) => r == Role.Drawer ? Role.Guesser : Role.Drawer;

    private void FinishRoundByTimeout(ISessionHost host, DateTime nowUtc)
    {
        State = GameState.RoundResult;
        RemainingSeconds = 0;
        RoundEndUtc = nowUtc.AddSeconds(ResultDisplaySeconds);
        ResultText = "時間切れ";
        host.Log(LogLevel.Info, "ラウンド結果", "", PairId, $"{PairId} Round {Round} が時間切れになりました（答え: {Question}）");
        host.BroadcastState(this);
        foreach (var pc in new[] { PcA, PcB })
        {
            host.SendToPc(pc, new NetMessage
            {
                Type = MessageType.RoundResult,
                PairId = PairId,
                SessionId = SessionId,
                Round = Round,
                TotalRounds = TotalRounds,
                IsCorrect = false,
                CorrectAnswer = Question,
                Question = Question,
                ResultText = ResultText,
            });
        }
    }

    private void BeginNextRound(ISessionHost host, DateTime nowUtc)
    {
        State = GameState.Countdown;
        RemainingSeconds = CountdownSeconds;
        RoundEndUtc = nowUtc.AddSeconds(CountdownSeconds);
        _lastSentRemaining = -1;
        host.Log(LogLevel.Info, "次ラウンド", "", PairId, $"{PairId} 次ラウンド（Round {Round + 1}）へ移行。カウントダウン開始");
        host.BroadcastState(this);
    }

    private void FinishGame(ISessionHost host, DateTime nowUtc)
    {
        State = GameState.GameFinished;
        RemainingSeconds = 0;
        RoundEndUtc = nowUtc.AddSeconds(ResultDisplaySeconds);
        ResultText = "ゲーム終了";
        host.Log(LogLevel.Info, "ゲーム終了", "", PairId, $"{PairId} ゲーム終了（正解数: {CorrectCount}/{TotalRounds}）");
        host.BroadcastState(this);
        foreach (var pc in new[] { PcA, PcB })
        {
            host.SendToPc(pc, new NetMessage
            {
                Type = MessageType.GameFinished,
                PairId = PairId,
                SessionId = SessionId,
                CorrectCount = CorrectCount,
                TotalRounds = TotalRounds,
            });
        }
    }

    /// <summary>ゲーム終了表示後、再び準備状態へ戻す（ペアは維持）</summary>
    public void ResetToReady(ISessionHost host)
    {
        lock (_lock)
        {
            State = GameState.WaitingForReady;
            Round = 0;
            ReadyA = ReadyB = false;
            CorrectCount = 0;
            Question = null;
            _acceptableAnswers.Clear();
            ResultText = null;
            AnswerCount = 0;
            CompletedStrokes.Clear();
            CurrentStroke = null;
            _lastSentRemaining = -1;
            host.Log(LogLevel.Info, "状態リセット", "", PairId, $"{PairId} を準備待ち状態へ戻しました");
            host.BroadcastState(this);
        }
    }

    /// <summary>緊急停止：すべての動作を止める</summary>
    public void EmergencyStop(ISessionHost host)
    {
        lock (_lock)
        {
            if (State == GameState.EmergencyStopped) return;
            State = GameState.EmergencyStopped;
            RemainingSeconds = 0;
            host.Log(LogLevel.Warn, "緊急停止", "", PairId, $"{PairId} を緊急停止しました");
            foreach (var pc in new[] { PcA, PcB })
            {
                host.SendToPc(pc, new NetMessage
                {
                    Type = MessageType.EmergencyStop,
                    PairId = PairId,
                    SessionId = SessionId,
                    Message = "緊急停止ボタンが押されました。しばらくお待ちください。",
                });
            }
        }
    }

    /// <summary>システム復元：ペア・接続は維持し、ゲームを最初からやり直す</summary>
    public void Restore(ISessionHost host)
    {
        lock (_lock)
        {
            State = GameState.WaitingForReady;
            Round = 0;
            ReadyA = ReadyB = false;
            CorrectCount = 0;
            Question = null;
            _acceptableAnswers.Clear();
            ResultText = null;
            AnswerCount = 0;
            CompletedStrokes.Clear();
            CurrentStroke = null;
            _lastSentRemaining = -1;
            host.Log(LogLevel.Info, "システム復元", "", PairId, $"{PairId} をゲーム開始前の状態へ復元しました");
            foreach (var pc in new[] { PcA, PcB })
            {
                host.SendToPc(pc, new NetMessage
                {
                    Type = MessageType.SystemRestored,
                    PairId = PairId,
                    SessionId = SessionId,
                    Message = "システムが復元されました",
                });
            }
            host.BroadcastState(this);
        }
    }

    /// <summary>
    /// 回答の受付可否検証（役割・状態・回答回数制限）。
    /// AnswerManager から呼ばれる。
    /// </summary>
    public bool CanAcceptAnswer(string pc, out string? error)
    {
        error = null;
        if (State != GameState.Drawing)
        {
            error = $"現在は回答を受け付けていません（状態: {State}）";
            return false;
        }
        if (!IsGuesser(pc))
        {
            error = "当てる側のクライアントのみ回答できます";
            return false;
        }
        if (AnswerLimit > 0 && AnswerCount >= AnswerLimit)
        {
            error = $"回答回数の制限（{AnswerLimit}回）に達しました";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 回答の判定結果を記録する。正解ならラウンド結果状態へ遷移する。
    /// </summary>
    public void RecordAnswer(string pc, bool isCorrect, ISessionHost host)
    {
        lock (_lock)
        {
            if (!isCorrect)
            {
                AnswerCount++;
                host.Log(LogLevel.Info, "回答", pc, PairId, $"{pc} の回答が不正解でした（残り回数: {MaxRemainingAnswers()}）");
                return;
            }

            CorrectCount++;
            AnswerCount++;
            State = GameState.RoundResult;
            RemainingSeconds = 0;
            RoundEndUtc = DateTime.UtcNow.AddSeconds(ResultDisplaySeconds);
            ResultText = "正解！";
            _lastSentRemaining = -1;

            host.Log(LogLevel.Info, "回答", pc, PairId, $"{pc} の回答が正解でした（お題: {Question}）");
            host.BroadcastState(this);
        }
    }

    public string MaxRemainingAnswers() => AnswerLimit <= 0 ? "無制限" : Math.Max(0, AnswerLimit - AnswerCount).ToString();

    /// <summary>
    /// ストロークデータの受付可否検証（役割・状態・データ形式）。
    /// DrawingRelay から呼ばれる。
    /// </summary>
    public bool CanAcceptStroke(string pc, StrokeData stroke, out string? error)
    {
        error = null;
        if (State != GameState.Drawing)
        {
            error = $"描画はラウンド中のみ受け付けます（状態: {State}）";
            return false;
        }
        if (!IsDrawer(pc))
        {
            error = "描く側のクライアントのみ描画データを送信できます";
            return false;
        }
        if (stroke == null || stroke.Points == null || stroke.Points.Count == 0)
        {
            error = "ストロークデータが空です";
            return false;
        }
        if (stroke.StrokeId <= 0 || stroke.Points.Count > 2000)
        {
            error = "ストロークデータが不正です";
            return false;
        }
        foreach (var p in stroke.Points)
        {
            if (p.X < -10000 || p.X > 100000 || p.Y < -10000 || p.Y > 100000)
            {
                error = "座標が範囲外です";
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// キャンバス全消去リクエストの受付検証と実行。
    /// 描く側のみ・ラウンド中のみ受け付ける。サーバー側の保持ストロークも消去し、
    /// 両クライアントへStrokeClearを送信する（再接続時のスナップショットも正しくなるように）。
    /// </summary>
    public void HandleClearCanvas(string pc, ISessionHost host)
    {
        lock (_lock)
        {
            if (State != GameState.Drawing)
            {
                host.Log(LogLevel.Warn, "不正なゲーム状態", pc, PairId, $"全消去はラウンド中のみ受け付けます（状態: {State}）");
                return;
            }
            if (!IsDrawer(pc))
            {
                host.Log(LogLevel.Warn, "不正な操作", pc, PairId, "描く側のクライアントのみ全消去できます");
                return;
            }

            CompletedStrokes.Clear();
            CurrentStroke = null;
            host.Log(LogLevel.Info, "全消去", pc, PairId, $"{pc} がキャンバスを全消去しました");
            foreach (var target in new[] { PcA, PcB })
            {
                host.SendToPc(target, new NetMessage
                {
                    Type = MessageType.StrokeClear,
                    PairId = PairId,
                    SessionId = SessionId,
                });
            }
        }
    }

    /// <summary>ストローク差分を蓄積する（既存ストロークへの追加 or 新規開始）</summary>
    public void AppendStroke(StrokeData segment, bool isEnd)
    {
        lock (_lock)
        {
            if (CurrentStroke == null || CurrentStroke.StrokeId != segment.StrokeId)
            {
                if (CurrentStroke != null) CompletedStrokes.Add(CurrentStroke);
                CurrentStroke = segment.Clone();
            }
            else
            {
                CurrentStroke.Points.AddRange(segment.Points.Select(p => new StrokePoint(p.X, p.Y)));
            }

            if (isEnd)
            {
                CompletedStrokes.Add(CurrentStroke);
                CurrentStroke = null;
            }
        }
    }

    public Role RoleOf(string pc) => pc == PcA ? RoleA : pc == PcB ? RoleB : Role.None;

    public IReadOnlyList<StrokeData> StrokeSnapshot()
    {
        lock (_lock)
        {
            var list = CompletedStrokes.Select(s => s.Clone()).ToList();
            if (CurrentStroke != null) list.Add(CurrentStroke.Clone());
            return list;
        }
    }
}
