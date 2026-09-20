using System.Text.Json.Serialization;

namespace DrawingGame.Shared;

/// <summary>クライアント⇔サーバー間でやり取りするメッセージの種類</summary>
public enum MessageType
{
    /// <summary>クライアント→サーバー: 初回接続・再接続時の自己紹介</summary>
    Hello,
    /// <summary>クライアント→サーバー: 生存確認</summary>
    Ping,
    /// <summary>サーバー→クライアント: 生存確認応答</summary>
    Pong,
    /// <summary>クライアント→サーバー: 準備OK</summary>
    ReadyOk,
    /// <summary>クライアント→サーバー: 描画ストロークの一部（差分）</summary>
    StrokeSegment,
    /// <summary>クライアント→サーバー: ストローク終了</summary>
    StrokeEnd,
    /// <summary>クライアント→サーバー: 回答送信</summary>
    AnswerSubmit,
    /// <summary>クライアント→サーバー: キャンバス全消去リクエスト</summary>
    ClearCanvasRequest,
    /// <summary>クライアント→サーバー: 状態スナップショット要求</summary>
    RequestSnapshot,
    /// <summary>サーバー→クライアント: 接続受付完了</summary>
    Welcome,
    /// <summary>サーバー→クライアント: ゲーム状態同期</summary>
    StateSync,
    /// <summary>サーバー→クライアント: 残り時間同期（サーバー基準）</summary>
    TimeSync,
    /// <summary>サーバー→クライアント: 描画中継（相手の描いたデータ）</summary>
    StrokeRelay,
    /// <summary>サーバー→クライアント: 現在の全ストローク送信（再接続時など）</summary>
    StrokeSnapshot,
    /// <summary>サーバー→クライアント: キャンバスクリア指示</summary>
    StrokeClear,
    /// <summary>サーバー→クライアント: 回答結果</summary>
    AnswerResult,
    /// <summary>サーバー→クライアント: ラウンド結果</summary>
    RoundResult,
    /// <summary>サーバー→クライアント: ゲーム終了</summary>
    GameFinished,
    /// <summary>サーバー→クライアント: 緊急停止</summary>
    EmergencyStop,
    /// <summary>サーバー→クライアント: システム復元</summary>
    SystemRestored,
    /// <summary>サーバー→クライアント: システムリセット（アプリ再起動要求）</summary>
    SystemReset,
    /// <summary>サーバー→クライアント: ペア解除</summary>
    Unpaired,
    /// <summary>サーバー→クライアント: サーバー終了</summary>
    ServerShutdown,
    /// <summary>エラー通知</summary>
    Error,
}

/// <summary>
/// ゲーム全体で唯一の正となるゲーム状態。
/// サーバーのみが遷移を決定し、クライアントはサーバーから通知された状態に従う。
/// </summary>
public enum GameState
{
    Unknown = 0,
    /// <summary>サーバー未接続</summary>
    WaitingForServer,
    /// <summary>サーバー接続済み・ペア未決定（サーバー操作待機中）</summary>
    WaitingForPair,
    /// <summary>ペア決定済み・準備OK待ち</summary>
    WaitingForReady,
    /// <summary>ラウンド開始カウントダウン中</summary>
    Countdown,
    /// <summary>描画中（当てる側は同時に回答可能）</summary>
    Drawing,
    /// <summary>ラウンド内の回答フェーズ</summary>
    Answering,
    /// <summary>ラウンド結果表示中</summary>
    RoundResult,
    /// <summary>次ラウンド移行中</summary>
    NextRound,
    /// <summary>全ラウンド終了</summary>
    GameFinished,
    /// <summary>緊急停止中</summary>
    EmergencyStopped,
}

/// <summary>ペア内での役割</summary>
public enum Role
{
    None = 0,
    /// <summary>描く側</summary>
    Drawer = 1,
    /// <summary>当てる側</summary>
    Guesser = 2,
}

/// <summary>
/// クライアント⇔サーバー間でやり取りされるメッセージの本体。
/// 1つのDTOに必要フィールドを持たせ、Typeで種類を判別する。
/// </summary>
public class NetMessage
{
    public MessageType Type { get; set; }

    public string? PcNumber { get; set; }
    public string? DeviceName { get; set; }
    public string? Version { get; set; }

    public string? PairId { get; set; }
    public string? SessionId { get; set; }

    public long Seq { get; set; }
    public bool Reconnect { get; set; }
    public bool IsReady { get; set; }

    public GameState GameState { get; set; }
    public int Round { get; set; }
    public int TotalRounds { get; set; }
    public int RemainingSeconds { get; set; }
    public Role MyRole { get; set; }

    public string? Question { get; set; }
    public string? Answer { get; set; }
    public bool IsCorrect { get; set; }
    public string? CorrectAnswer { get; set; }
    public int AnswerCount { get; set; }
    public int AnswerLimit { get; set; }
    public int CorrectCount { get; set; }
    public string? ResultText { get; set; }

    public string? Message { get; set; }
    public string? Reason { get; set; }

    public StrokeData? Stroke { get; set; }
    public List<StrokeData>? Strokes { get; set; }

    public string? ServerTimeUtc { get; set; }
}
