using DrawingGame.Shared;

namespace DrawingGame.Client;

/// <summary>
/// サーバーから受信したメッセージをゲーム状態・UIイベントへ変換する。
/// サーバーが決定した状態をクライアント側で独自判断せず、そのまま反映する。
/// </summary>
public class GameStateHandler
{
    private readonly ClientStateManager _state;
    private readonly Logger _log;

    public GameStateHandler(ClientStateManager state, Logger log)
    {
        _state = state;
        _log = log;
    }

    public void OnMessageReceived(NetMessage msg)
    {
        _state.Apply(msg);
    }
}
