using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// 描画データの受付・検証・中継を担当する。
/// ・描く側のクライアントのみ受け付ける
/// ・ラウンド中のみ受け付ける
/// ・データ形式を検証する
/// ・サーバー側にストロークを保持し（再構築用）、パートナーへ中継する
/// </summary>
public class DrawingRelay
{
    // 許可する色（クライアントが選択できるパレットと一致させる）
    public static readonly string[] AllowedColors =
    {
        "#000000", "#FF0000", "#0000FF", "#00AA00", "#FF8C00", "#800080",
    };

    private readonly ServerCore _core;

    public DrawingRelay(ServerCore core)
    {
        _core = core;
    }

    public void ProcessSegment(GameSession session, string pc, StrokeData stroke, bool isEnd)
    {
        if (!session.CanAcceptStroke(pc, stroke, out var error))
        {
            _core.Log.Warn("描画拒否", pc, session.PairId, error ?? "描画データを拒否しました");
            return;
        }

        // 色の検証（不正な色は黒へ）。消しゴムは白固定
        if (stroke.IsEraser)
        {
            stroke.ColorHex = "#FFFFFF";
        }
        else if (!AllowedColors.Contains(stroke.ColorHex ?? "", StringComparer.OrdinalIgnoreCase))
        {
            _core.Log.Warn("不正なデータ", pc, session.PairId, "許可されていない色のため黒へ置き換えました");
            stroke.ColorHex = "#000000";
        }
        stroke.PenWidth = Math.Clamp(stroke.PenWidth, 1, 20);

        session.AppendStroke(stroke, isEnd);

        var partner = session.PartnerOf(pc);
        if (isEnd)
            _core.Sessions.RelayStrokeEnd(session, partner, stroke.StrokeId);
        else
            _core.Sessions.RelayStroke(session, partner, stroke);
    }

    /// <summary>再接続時に現在の全ストロークを送信する（サーバー側からの再構築）</summary>
    public void SendSnapshot(GameSession session, string toPc)
    {
        _core.Sessions.SendStrokeSnapshot(session, toPc);
    }
}
