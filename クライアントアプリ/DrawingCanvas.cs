using DrawingGame.Shared;

namespace DrawingGame.Client;

/// <summary>ローカル表示用の1ストローク</summary>
public class LocalStroke
{
    public long StrokeId { get; set; }
    public Color Color { get; set; }
    public int Width { get; set; } = 3;
    public List<Point> Points { get; } = new();
}

/// <summary>
/// 描画キャンバス。ストロークの差分（点列）を受け取って描画する。
/// 描く側は自分の操作で描き、当てる側はサーバーから中継されたデータを受信して描く。
/// </summary>
public class DrawingCanvas : Control
{
    private readonly List<LocalStroke> _strokes = new();
    private readonly object _lock = new();

    public DrawingCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(900, 600);
    }

    /// <summary>ストロークに点列を追加（既存IDなら追加、新規IDなら新ストローク開始）</summary>
    public void AddPoints(long strokeId, string colorHex, int penWidth, List<StrokePoint> points)
    {
        if (points == null || points.Count == 0) return;

        lock (_lock)
        {
            var stroke = _strokes.FirstOrDefault(s => s.StrokeId == strokeId);
            if (stroke == null)
            {
                stroke = new LocalStroke
                {
                    StrokeId = strokeId,
                    Color = ParseColor(colorHex),
                    Width = Math.Clamp(penWidth, 1, 20),
                };
                _strokes.Add(stroke);
            }
            foreach (var p in points) stroke.Points.Add(new Point(p.X, p.Y));
        }
        Invalidate();
    }

    /// <summary>ストローク終了（描画に影響なし。以降の受信ストロークとの区切りとして使用）</summary>
    public void EndStroke(long strokeId)
    {
        // 終了の明示処理は不要（次のIDで自動的に新ストロークになる）
    }

    public void ReplaceAll(IEnumerable<StrokeData> strokes)
    {
        lock (_lock)
        {
            _strokes.Clear();
            foreach (var s in strokes)
            {
                if (s.Points == null || s.Points.Count == 0) continue;
                var stroke = new LocalStroke
                {
                    StrokeId = s.StrokeId,
                    Color = ParseColor(s.ColorHex),
                    Width = Math.Clamp(s.PenWidth, 1, 20),
                };
                foreach (var p in s.Points) stroke.Points.Add(new Point(p.X, p.Y));
                _strokes.Add(stroke);
            }
        }
        Invalidate();
    }

    public void ClearCanvas()
    {
        lock (_lock) _strokes.Clear();
        Invalidate();
    }

    private static Color ParseColor(string? hex)
    {
        try { return ColorTranslator.FromHtml(hex ?? "#000000"); }
        catch { return Color.Black; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        LocalStroke[] strokes;
        lock (_lock) strokes = _strokes.ToArray();

        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count == 1)
            {
                var p = stroke.Points[0];
                float r = stroke.Width / 2f;
                using var brush = new SolidBrush(stroke.Color);
                e.Graphics.FillEllipse(brush, p.X - r, p.Y - r, stroke.Width, stroke.Width);
                continue;
            }
            if (stroke.Points.Count < 2) continue;
            using var pen = new Pen(stroke.Color, stroke.Width)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
            };
            e.Graphics.DrawLines(pen, stroke.Points.ToArray());
        }
    }
}
