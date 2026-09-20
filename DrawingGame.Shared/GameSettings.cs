namespace DrawingGame.Shared;

/// <summary>
/// ゲーム設定（サーバーが保持し、ゲーム開始時にスナップショットとして各セッションへ渡す）
/// </summary>
public class GameSettings
{
    /// <summary>ラウンド数</summary>
    public int Rounds { get; set; } = 3;

    /// <summary>1ラウンドの制限時間（秒）</summary>
    public int TimeSeconds { get; set; } = 60;

    /// <summary>回答制限回数（0 = 無制限）</summary>
    public int AnswerLimit { get; set; } = 0;

    public GameSettings Clone() => new()
    {
        Rounds = Rounds,
        TimeSeconds = TimeSeconds,
        AnswerLimit = AnswerLimit,
    };

    public static void Clamp(GameSettings s)
    {
        s.Rounds = Math.Clamp(s.Rounds, 1, 10);
        s.TimeSeconds = Math.Clamp(s.TimeSeconds, 10, 600);
        s.AnswerLimit = Math.Clamp(s.AnswerLimit, 0, 10);
    }
}
