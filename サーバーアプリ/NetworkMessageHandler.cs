using System.Text.RegularExpressions;
using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// クライアントから受信したメッセージの検証とルーティング。
/// 受信データをそのまま信用せず、PC番号・セッション・役割・状態をサーバー側で検証する。
/// </summary>
public class NetworkMessageHandler
{
    private static readonly Regex PcNumberPattern = new("^[A-Za-z0-9_-]{1,16}$", RegexOptions.Compiled);

    private readonly ServerCore _core;

    public NetworkMessageHandler(ServerCore core)
    {
        _core = core;
    }

    public void Handle(ClientConnection conn, NetMessage msg)
    {
        switch (msg.Type)
        {
            case MessageType.Hello:
                HandleHello(conn, msg);
                break;

            case MessageType.Ping:
                conn.Send(new NetMessage
                {
                    Type = MessageType.Pong,
                    ServerTimeUtc = DateTime.UtcNow.ToString("o"),
                });
                break;

            case MessageType.ReadyOk:
                HandleAsRegistered(conn, msg, (pc, session) =>
                {
                    session.HandleReady(pc, _core.Sessions);
                });
                break;

            case MessageType.StrokeSegment:
            case MessageType.StrokeEnd:
                HandleAsRegistered(conn, msg, (pc, session) =>
                {
                    if (msg.Stroke == null) { Reject(conn, msg, "ストロークデータがありません"); return; }
                    _core.DrawingRelay.ProcessSegment(session, pc, msg.Stroke, msg.Type == MessageType.StrokeEnd);
                });
                break;

            case MessageType.AnswerSubmit:
                HandleAsRegistered(conn, msg, (pc, session) =>
                {
                    _core.AnswerManager.Process(session, pc, msg.Answer);
                });
                break;

            case MessageType.ClearCanvasRequest:
                HandleAsRegistered(conn, msg, (pc, session) =>
                {
                    session.HandleClearCanvas(pc, _core.Sessions);
                });
                break;

            case MessageType.RequestSnapshot:
                HandleAsRegistered(conn, msg, (pc, session) =>
                {
                    _core.Log.Info("状態同期", pc, session.PairId, $"{pc} から状態スナップショット要求を受信しました");
                    _core.Sessions.ResyncClient(session, pc);
                });
                break;

            default:
                _core.Log.Error("不正なパケット", conn.PcNumber, "", $"未知のメッセージ種別を受信しました: {msg.Type}");
                break;
        }
    }

    private void HandleHello(ClientConnection conn, NetMessage msg)
    {
        var pcNumber = msg.PcNumber?.Trim() ?? "";
        if (!PcNumberPattern.IsMatch(pcNumber))
        {
            _core.Log.Error("不正なPC番号", "", "", $"不正なPC番号で接続試行（{conn.RemoteEndPoint}）: \"{pcNumber}\"");
            conn.Send(new NetMessage { Type = MessageType.Error, Message = "PC番号の形式が不正です" });
            conn.Close();
            return;
        }

        if (!_core.Clients.Register(conn, pcNumber, msg.DeviceName ?? "?", out var error))
        {
            conn.Send(new NetMessage { Type = MessageType.Error, Message = error ?? "接続を拒否されました" });
            conn.Close();
            return;
        }

        conn.PcNumber = pcNumber;
        conn.DeviceName = msg.DeviceName ?? "?";
        _core.Log.Info("接続", pcNumber, "", $"{pcNumber}（{conn.DeviceName}, {conn.RemoteEndPoint}）がサーバーへ接続しました{(msg.Reconnect ? "（再接続）" : "")}");

        var session = _core.Sessions.GetByPc(pcNumber);

        conn.Send(new NetMessage
        {
            Type = MessageType.Welcome,
            PcNumber = pcNumber,
            PairId = session?.PairId,
            SessionId = session?.SessionId,
            ServerTimeUtc = DateTime.UtcNow.ToString("o"),
            Message = "接続が確立しました",
        });

        if (session != null)
        {
            // 再接続時：現在の正しいゲーム状態と描画状態を送信する
            _core.Sessions.ResyncClient(session, pcNumber);
            _core.Log.Info("再接続", pcNumber, session.PairId, $"{pcNumber} に現在のゲーム状態を同期しました");
        }
        else
        {
            _core.Sessions.SendToPc(pcNumber, new NetMessage
            {
                Type = MessageType.StateSync,
                GameState = GameState.WaitingForPair,
                Message = "サーバー操作待機中です",
            });
        }
    }

    /// <summary>登録済みクライアントのみ受付、かつセッション所属を検証してから処理する</summary>
    private void HandleAsRegistered(ClientConnection conn, NetMessage msg, Action<string, GameSession> action)
    {
        if (string.IsNullOrEmpty(conn.PcNumber))
        {
            _core.Log.Error("不正なパケット", "", "", "Hello未完了の接続からメッセージを受信しました");
            return;
        }

        var session = _core.Sessions.GetByPc(conn.PcNumber);
        if (session == null)
        {
            _core.Log.Error("存在しないPC番号", conn.PcNumber, "", $"{conn.PcNumber} はペアリングされていません（{msg.Type}）");
            return;
        }

        // セッション・ペアIDの整合性検証（別セッションへのアクセス防止）
        if (msg.SessionId != null && msg.SessionId != session.SessionId)
        {
            _core.Log.Error("セッション不正", conn.PcNumber, session.PairId, "セッションIDが一致しないメッセージを拒否しました");
            return;
        }

        try
        {
            action(conn.PcNumber, session);
        }
        catch (Exception ex)
        {
            _core.Log.Error("サーバー内部エラー", conn.PcNumber, session.PairId, $"メッセージ処理で例外: {ex.Message}");
        }
    }

    private void Reject(ClientConnection conn, NetMessage msg, string reason)
    {
        _core.Log.Warn("不正なデータ", conn.PcNumber, "", $"{msg.Type}: {reason}");
        conn.Send(new NetMessage { Type = MessageType.Error, Message = reason });
    }
}
