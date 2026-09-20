using DrawingGame.Shared;

namespace DrawingGame.Client;

/// <summary>
/// 描く側のマウス入力をストローク差分データへ変換し、サーバーへ送信する。
/// 画像全体を毎フレーム送信せず、ストロークの差分（点列）のみを送る。
/// </summary>
public class DrawingManager
{
    // クライアントのパレット（サーバー側の許可色と一致させる）
    public static readonly string[] DrawingRelayPalette =
    {
        "#000000", "#FF0000", "#0000FF", "#00AA00", "#FF8C00", "#800080",
    };

    // 消しゴムはキャンバス背景色（白）のストロークとして表現する
    public const string EraserColorHex = "#FFFFFF";
    public const int EraserWidth = 20;

    private readonly DrawingCanvas _canvas;
    private readonly Action<NetMessage> _send;
    private readonly Action<string> _log;
    private long _strokeSeq;
    private StrokeData? _activeStroke;
    private bool _drawingEnabled;

    public string CurrentColorHex { get; private set; } = "#000000";
    public int PenWidth { get; set; } = 3;
    public bool IsDrawer { get; set; }
    public bool EraserMode { get; private set; }

    public DrawingManager(DrawingCanvas canvas, Action<NetMessage> send, Action<string> log)
    {
        _canvas = canvas;
        _send = send;
        _log = log;

        _canvas.MouseDown += OnMouseDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseUp += OnMouseUp;
    }

    private void SendPending(long strokeId, string colorHex, int penWidth, bool isEraser, IReadOnlyList<StrokePoint> source, bool isEnd)
    {
        var pending = source.Skip(_lastSentCount).Select(p => new StrokePoint(p.X, p.Y)).ToList();
        if (pending.Count == 0)
        {
            var last = source[^1];
            pending.Add(new StrokePoint(last.X, last.Y));
        }
        SendSegmentPoints(strokeId, colorHex, penWidth, isEraser, pending, isEnd);
    }

    public void SetDrawingEnabled(bool enabled, bool isDrawer)
    {
        IsDrawer = isDrawer;
        _drawingEnabled = enabled && isDrawer;
        _canvas.Cursor = _drawingEnabled ? Cursors.Cross : Cursors.Default;
        // 描画権限が失われたら消しゴムモードも解除する（役割交代・ラウンド終了時）
        if (!_drawingEnabled && EraserMode) SetEraserMode(false);
    }

    /// <summary>消しゴムモードの切り替え（白の太いストロークとして描画）</summary>
    public void SetEraserMode(bool on)
    {
        EraserMode = on;
    }

    /// <summary>キャンバス全消去をサーバーへリクエストする（サーバーが両クライアントへ反映）</summary>
    public void RequestClearCanvas()
    {
        _send(new NetMessage { Type = MessageType.ClearCanvasRequest });
    }

    public void SetColor(string colorHex)
    {
        if (DrawingRelayPalette.Contains(colorHex, StringComparer.OrdinalIgnoreCase))
        {
            CurrentColorHex = colorHex;
            SetEraserMode(false); // 色を選んだら消しゴムモードを解除
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (!_drawingEnabled || e.Button != MouseButtons.Left) return;
        // 新しいストローク開始時は送信済みカウントをリセット（前のストロークの値が残ると点が欠落する）
        _lastSentCount = 0;
        _activeStroke = new StrokeData
        {
            StrokeId = ++_strokeSeq,
            ColorHex = EraserMode ? EraserColorHex : CurrentColorHex,
            PenWidth = EraserMode ? EraserWidth : PenWidth,
            IsEraser = EraserMode,
            Points = new List<StrokePoint> { new(e.X, e.Y) },
        };
        _canvas.AddPoints(_activeStroke.StrokeId, _activeStroke.ColorHex, _activeStroke.PenWidth, _activeStroke.Points);
        SendSegmentPoints(_activeStroke.StrokeId, _activeStroke.ColorHex, _activeStroke.PenWidth, _activeStroke.IsEraser, _activeStroke.Points, false);
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_activeStroke == null) return;
        if (e.X == _activeStroke.Points[^1].X && e.Y == _activeStroke.Points[^1].Y) return;

        _activeStroke.Points.Add(new StrokePoint(e.X, e.Y));
        _canvas.AddPoints(_activeStroke.StrokeId, _activeStroke.ColorHex, _activeStroke.PenWidth, new List<StrokePoint> { new(e.X, e.Y) });

        // 一定数たまったら差分を送信（リアルタイム性と帯域のバランス）
        var pending = _activeStroke.Points
            .Skip(_lastSentCount)
            .Select(p => new StrokePoint(p.X, p.Y))
            .ToList();
        if (pending.Count >= 4)
        {
            SendSegmentPoints(_activeStroke.StrokeId, _activeStroke.ColorHex, _activeStroke.PenWidth, _activeStroke.IsEraser, pending, false);
        }
    }

    private int _lastSentCount;

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (_activeStroke == null) return;
        var stroke = _activeStroke;
        _activeStroke = null;

        SendPending(stroke.StrokeId, stroke.ColorHex, stroke.PenWidth, stroke.IsEraser, stroke.Points, true);
        _log($"ストローク送信完了（{stroke.Points.Count}点, 色 {stroke.ColorHex}{(stroke.IsEraser ? ", 消しゴム" : "")}）");
    }

    private void SendSegmentPoints(long id, string colorHex, int width, bool isEraser, List<StrokePoint> points, bool isEnd)
    {
        if (points.Count == 0) return;
        _lastSentCount += points.Count;
        _send(new NetMessage
        {
            Type = isEnd ? MessageType.StrokeEnd : MessageType.StrokeSegment,
            Stroke = new StrokeData
            {
                StrokeId = id,
                ColorHex = colorHex,
                PenWidth = width,
                IsEraser = isEraser,
                Points = points,
            },
        });
    }
}
