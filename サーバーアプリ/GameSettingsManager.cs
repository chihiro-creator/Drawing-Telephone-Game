using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// ゲーム設定の管理。
/// GOボタンで確定され、次回ゲーム開始時に適用される（進行中のゲームには影響しない）。
/// </summary>
public class GameSettingsManager
{
    private readonly object _lock = new();
    private readonly LogManager _log;
    private GameSettings _current = new();

    public GameSettings Current
    {
        get { lock (_lock) return _current.Clone(); }
    }

    public GameSettingsManager(LogManager log)
    {
        _log = log;
    }

    /// <summary>
    /// 設定を確定する。進行中のゲームセッション数に応じて適用タイミングをログで明示する。
    /// </summary>
    public void Apply(GameSettings settings)
    {
        GameSettings.Clamp(settings);
        lock (_lock) _current = settings.Clone();

        _log.Info("ゲーム設定", "", "",
            $"ゲーム設定を変更しました（ラウンド数: {_current.Rounds}, 制限時間: {_current.TimeSeconds}秒, 回答制限: {(_current.AnswerLimit == 0 ? "無制限" : $"{_current.AnswerLimit}回")}）");
    }
}
