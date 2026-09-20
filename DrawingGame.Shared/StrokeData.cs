namespace DrawingGame.Shared;

/// <summary>ストローク上の1点（クライアント座標系）</summary>
public class StrokePoint
{
    public int X { get; set; }
    public int Y { get; set; }

    public StrokePoint() { }
    public StrokePoint(int x, int y) { X = x; Y = y; }
}

/// <summary>
/// 描画ストロークの差分データ。
/// 1本のストロークは複数の StrokeSegment メッセージに分割されて送信される。
/// </summary>
public class StrokeData
{
    /// <summary>ストロークを識別する連番（クライアントが採番）</summary>
    public long StrokeId { get; set; }

    /// <summary>色（#RRGGBB）</summary>
    public string ColorHex { get; set; } = "#000000";

    /// <summary>ペン幅</summary>
    public int PenWidth { get; set; } = 3;

    /// <summary>消しゴムストロークかどうか（サーバーは白固定で受け付ける）</summary>
    public bool IsEraser { get; set; }

    /// <summary>このセグメントに含まれる点列</summary>
    public List<StrokePoint> Points { get; set; } = new();

    public StrokeData Clone() => new()
    {
        StrokeId = StrokeId,
        ColorHex = ColorHex,
        PenWidth = PenWidth,
        IsEraser = IsEraser,
        Points = new List<StrokePoint>(Points),
    };
}
