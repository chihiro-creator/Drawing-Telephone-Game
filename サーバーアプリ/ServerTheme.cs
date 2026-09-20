using System.Drawing.Drawing2D;

namespace DrawingGame.Server;

/// <summary>
/// サーバーアプリ用のコンソール（ターミナル）風ダークテーマ。
/// 黒基調・等幅フォント（MS ゴシック）・アクセントカラー（緑/赤/シアン）で
/// ターミナルっぽい見た目に統一する。
/// </summary>
public static class ServerTheme
{
    // ===== ターミナルカラー =====
    public static readonly Color Back = Color.FromArgb(11, 11, 13);
    public static readonly Color Panel = Color.FromArgb(18, 18, 21);
    public static readonly Color PanelLight = Color.FromArgb(29, 29, 34);
    public static readonly Color HeaderBack = Color.FromArgb(15, 15, 18);
    public static readonly Color Border = Color.FromArgb(58, 58, 68);
    public static readonly Color Text = Color.FromArgb(214, 214, 222);
    public static readonly Color TextDim = Color.FromArgb(128, 128, 142);
    public static readonly Color Accent = Color.FromArgb(0, 230, 118);
    public static readonly Color AccentGreen = Color.FromArgb(0, 230, 118);
    public static readonly Color AccentRed = Color.FromArgb(255, 59, 48);
    public static readonly Color AccentBlue = Color.FromArgb(61, 201, 255);
    public static readonly Color AccentAmber = Color.FromArgb(255, 179, 0);
    public static readonly Color Selection = Color.FromArgb(14, 92, 58);
    public static readonly Color TerminalLine = Color.FromArgb(14, 62, 38);

    /// <summary>コンソール風フォント（MS ゴシック）</summary>
    public static Font F(float size, FontStyle style = FontStyle.Regular)
        => new("MS Gothic", size, style);

    public static void ApplyForm(Form form)
    {
        form.BackColor = Back;
        form.ForeColor = Text;
    }

    /// <summary>通常のコンソールボタン（角括弧付き）</summary>
    public static Button Button(string text, float fontSize, FontStyle style = FontStyle.Regular)
        => new ConsoleButton(text, fontSize, style)
        {
            Brackets = true,
        };

    /// <summary>アクセント（緑）コンソールボタン（角括弧付き）</summary>
    public static Button AccentButton(string text, float fontSize, FontStyle style = FontStyle.Bold)
        => new ConsoleButton(text, fontSize, style)
        {
            Fill = Color.FromArgb(10, 28, 17),
            HoverFill = Color.FromArgb(14, 46, 26),
            Outline = AccentGreen,
            TextColor = AccentGreen,
            Brackets = true,
        };

    /// <summary>ログレベルに応じた色（INFO=緑 / WARN=黄 / ERROR=赤）</summary>
    public static Color LevelColor(LogLevel level) => level switch
    {
        LogLevel.Info => AccentGreen,
        LogLevel.Warn => AccentAmber,
        _ => AccentRed,
    };

    public static Label Label(string text, float fontSize, Color? fore = null, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = F(fontSize, style),
            ForeColor = fore ?? Text,
        };
    }

    /// <summary>TableLayoutPanel のセル内で上下中央・左寄せするラベル（入力欄と並べるとき用）</summary>
    public static Label FieldLabel(string text, float fontSize, Color? fore = null, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = F(fontSize, style),
            ForeColor = fore ?? Text,
        };
    }

    /// <summary>TableLayoutPanel のセル内で上下中央・中央寄せするラベル</summary>
    public static Label CenterLabel(string text, float fontSize, Color? fore = null, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = F(fontSize, style),
            ForeColor = fore ?? Text,
        };
    }

    /// <summary>
    /// コンソール風のセクション見出し（左に緑のアクセントバー + 「▸」付きタイトル）。
    /// 高さ 30px 前後の行に入れて使う。
    /// </summary>
    public static Panel ConsoleTitle(string text, float fontSize = 10f)
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Back };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 4));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = AccentGreen };
        var lbl = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Text = "▸ " + text,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = F(fontSize, FontStyle.Bold),
            ForeColor = AccentGreen,
        };
        p.Controls.Add(bar, 0, 0);
        p.Controls.Add(lbl, 1, 0);
        return p;
    }

    /// <summary>DataGridView をターミナル風にスタイルする</summary>
    public static void StyleGrid(DataGridView g)
    {
        g.BackgroundColor = Back;
        g.BorderStyle = BorderStyle.None;
        g.EnableHeadersVisualStyles = false;
        g.RowHeadersVisible = false;
        g.GridColor = Border;
        g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        g.ColumnHeadersHeight = 30;
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = PanelLight,
            ForeColor = AccentGreen,
            Font = F(9.5f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            SelectionBackColor = PanelLight,
            SelectionForeColor = AccentGreen,
        };
        g.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Panel,
            ForeColor = Text,
            Font = F(9.5f),
            SelectionBackColor = Selection,
            SelectionForeColor = Color.White,
            Padding = new Padding(2, 3, 2, 3),
        };
        g.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(15, 15, 18),
            ForeColor = Text,
            Font = F(9.5f),
            SelectionBackColor = Selection,
            SelectionForeColor = Color.White,
            Padding = new Padding(2, 3, 2, 3),
        };
        g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    }

    /// <summary>ListBox をターミナル風にスタイルする（選択時は緑背景の反転表示）</summary>
    public static void StyleList(ListBox lb)
    {
        lb.BackColor = Back;
        lb.ForeColor = Text;
        lb.BorderStyle = BorderStyle.FixedSingle;
        lb.DrawMode = DrawMode.OwnerDrawFixed;
        lb.ItemHeight = Math.Max(lb.ItemHeight, 20);
        lb.DrawItem += (s, e) =>
        {
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var bg = new SolidBrush(selected ? AccentGreen : Back);
            e.Graphics.FillRectangle(bg, e.Bounds);
            if (e.Index >= 0)
            {
                var text = lb.Items[e.Index]?.ToString() ?? "";
                var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(e.Graphics, text, lb.Font, e.Bounds, selected ? Color.Black : Text, flags);
            }
        };
    }

    public static void StyleInput(Control c)
    {
        c.BackColor = PanelLight;
        c.ForeColor = Text;
        if (c is TextBoxBase t) t.BorderStyle = BorderStyle.FixedSingle;
        else if (c is UpDownBase u) u.BorderStyle = BorderStyle.FixedSingle;
    }

    /// <summary>TabControl をターミナル風にスタイルする（選択中タブは緑の上バー + ▸ マーク）</summary>
    public static void StyleTabs(TabControl tabs)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(136, 34);
        tabs.Font = F(10.5f);
        tabs.DrawItem += (s, e) =>
        {
            var rect = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var bg = new SolidBrush(selected ? PanelLight : HeaderBack))
            {
                e.Graphics.FillRectangle(bg, rect);
            }
            if (selected)
            {
                using var accent = new SolidBrush(AccentGreen);
                e.Graphics.FillRectangle(accent, rect.X, rect.Y, rect.Width, 3);
            }
            string display = selected ? "▸ " + tabs.TabPages[e.Index].Text : "  " + tabs.TabPages[e.Index].Text;
            TextRenderer.DrawText(e.Graphics, display, tabs.Font, rect,
                selected ? Color.White : TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };
    }

    internal static Color Adjust(Color c, int amount)
    {
        int R = Math.Clamp(c.R + amount, 0, 255);
        int G = Math.Clamp(c.G + amount, 0, 255);
        int B = Math.Clamp(c.B + amount, 0, 255);
        return Color.FromArgb(c.A, R, G, B);
    }
}

/// <summary>
/// コンソール風ボタン。テキストは TextRenderer で常に中央描画されるため、
/// 絵文字や特殊文字が混じっても中央揃えが崩れない。
/// </summary>
public sealed class ConsoleButton : Button
{
    private bool _hover;
    private bool _down;
    private float _pulse = -1f;

    public Color Fill { get; set; }
    public Color HoverFill { get; set; }
    public Color Outline { get; set; }
    public Color TextColor { get; set; }

    /// <summary>角括弧付き表示（[ GO ] のようなターミナル風）にするか</summary>
    public bool Brackets { get; set; }

    /// <summary>外枠の太さ（2 以上で内側に明るい罫線を描く）</summary>
    public int OutlineWidth { get; set; } = 1;

    /// <summary>テキストの縦位置の微調整（正で下、負で上。視覚的な中央合わせ用）</summary>
    public int TextOffsetY { get; set; }

    public ConsoleButton(string text, float fontSize, FontStyle style = FontStyle.Bold)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Font = ServerTheme.F(fontSize, style);
        Cursor = Cursors.Hand;
        TabStop = true;
        FlatStyle = FlatStyle.Flat;
        Fill = ServerTheme.PanelLight;
        HoverFill = ServerTheme.Adjust(ServerTheme.PanelLight, 14);
        Outline = ServerTheme.Border;
        TextColor = ServerTheme.Text;
    }

    /// <summary>脈動アニメーションの位相（0〜1）を設定する（-1 で無効）</summary>
    public void SetPulse(float phase)
    {
        _pulse = phase;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _down = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _down = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _down = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        Color fill = !Enabled ? ServerTheme.Adjust(Fill, -36)
            : _down ? ServerTheme.Adjust(Fill, -16)
            : _hover ? HoverFill
            : _pulse >= 0 ? ServerTheme.Adjust(Fill, (int)(7 * Math.Sin(_pulse * Math.PI * 2)))
            : Fill;
        using (var b = new SolidBrush(fill))
        {
            g.FillRectangle(b, 0, 0, Width, Height);
        }

        using (var pen = new Pen(Outline, OutlineWidth))
        {
            float hw = OutlineWidth / 2f;
            g.DrawRectangle(pen, hw, hw, Width - OutlineWidth, Height - OutlineWidth);
        }
        if (OutlineWidth >= 2)
        {
            using var inner = new Pen(ServerTheme.Adjust(Outline, 40), 1);
            g.DrawRectangle(inner, 1.5f, 1.5f, Width - 3, Height - 3);
        }

        string display = Brackets ? "[ " + Text + " ]" : Text;
        Color textColor = Enabled ? TextColor : Color.FromArgb(150, TextColor);

        // グリフの輪郭（GraphicsPath）をタイトに計測して完全な中央揃えにする。
        // TextRenderer の VerticalCenter はフォントの行送り込みで中央化するため、MS Gothic では下にずれる。
        using (var path = new GraphicsPath())
        {
            float emSize = Font.Size * g.DpiY / 72f;
            path.AddString(display, Font.FontFamily, (int)Font.Style, emSize, new PointF(0, 0), StringFormat.GenericDefault);
            var bounds = path.GetBounds();

            if (bounds.Width > Width - 8 || bounds.Height > Height - 4)
            {
                // 収まらない場合は省略記号付きで描画（縦位置はタイト計測で合わせる）
                var size = TextRenderer.MeasureText(g, display, Font, new Size(Width, Height),
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                int ty = Math.Max(0, (Height - size.Height) / 2) + TextOffsetY;
                TextRenderer.DrawText(g, display, Font, new Rectangle(2, ty, Width - 4, size.Height), textColor,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }

            float sx = (Width - bounds.Width) / 2f - bounds.X;
            float sy = (Height - bounds.Height) / 2f - bounds.Y + TextOffsetY;
            g.TranslateTransform(sx, sy);
            using (var brush = new SolidBrush(textColor))
            {
                g.FillPath(brush, path);
            }
            g.ResetTransform();
        }
    }
}

/// <summary>
/// 任意の色の太枠を持つパネル（緊急停止オーバーレイのボックスなどに使用）。
/// </summary>
public sealed class BorderedPanel : Panel
{
    private Color _borderColor = ServerTheme.Border;

    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    public int BorderWidth { get; set; } = 2;

    public BorderedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var pen = new Pen(BorderColor, BorderWidth))
        {
            float hw = BorderWidth / 2f;
            e.Graphics.DrawRectangle(pen, hw, hw, Width - BorderWidth, Height - BorderWidth);
        }
        if (BorderWidth >= 2)
        {
            using var inner = new Pen(ServerTheme.Adjust(BorderColor, 40), 1);
            e.Graphics.DrawRectangle(inner, BorderWidth + 1.5f, BorderWidth + 1.5f, Width - 2 * BorderWidth - 3, Height - 2 * BorderWidth - 3);
        }
    }
}

/// <summary>
/// 状態表示用のパルスするドット（ヘッダー用）。
/// 色を変えることで「通常運転（緑）」「緊急停止（赤）」を視覚的に伝える。
/// </summary>
public sealed class StatusDot : Control
{
    private Color _color = ServerTheme.AccentGreen;
    private float _phase;

    public StatusDot()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Size = new Size(18, 18);
        TabStop = false;
    }

    public void SetColor(Color color)
    {
        _color = color;
        Invalidate();
    }

    /// <summary>パルス位相（0〜1）を進めて描画する</summary>
    public void Pulse(float phase)
    {
        _phase = phase;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // ヘッダー背景色で塗って擬似透明にする（ヘッダーに馴染ませる）
        e.Graphics.Clear(ServerTheme.HeaderBack);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float ph = (float)(Math.Sin(_phase * Math.PI * 2) * 0.5 + 0.5); // 0..1 脈動
        float size = Math.Min(Width, Height);

        // 外側へ拡がって消えるリング
        float ringR = size * (0.45f + 0.5f * ph);
        int alpha = (int)(70 * (1 - ph));
        using (var ring = new SolidBrush(Color.FromArgb(alpha, _color)))
        {
            e.Graphics.FillEllipse(ring, (Width - ringR) / 2f, (Height - ringR) / 2f, ringR, ringR);
        }

        // 中心のドット
        float dotR = size * 0.38f;
        using (var dot = new SolidBrush(_color))
        {
            e.Graphics.FillEllipse(dot, (Width - dotR) / 2f, (Height - dotR) / 2f, dotR, dotR);
        }

        // ハイライト（立体感）
        float hiR = size * 0.16f;
        using (var hi = new SolidBrush(Color.FromArgb(140, Color.White)))
        {
            e.Graphics.FillEllipse(hi, Width / 2f - hiR, Height / 2f - hiR * 1.4f, hiR * 2, hiR * 2);
        }
    }
}

/// <summary>
/// 点滅カーソル（コンソール風の「▌」ブロック）。
/// <see cref="SetVisible"/> をタイマーから呼んで点滅させる。
/// </summary>
public sealed class BlinkCursor : Label
{
    private int _intervalTicks;
    private int _tick;
    private bool _on;

    public BlinkCursor()
    {
        Text = "▌";
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleLeft;
        ForeColor = ServerTheme.AccentGreen;
        Font = ServerTheme.F(12, FontStyle.Bold);
        TabStop = false;
    }

    public void SetInterval(int ticks) => _intervalTicks = Math.Max(1, ticks);

    /// <summary>毎フレーム呼ぶ（ticks ごとに表示/非表示を切り替える）</summary>
    public void Tick()
    {
        _tick++;
        if (_tick % _intervalTicks == 0)
        {
            _on = !_on;
            Visible = _on;
        }
    }
}

/// <summary>
/// CRTスキャンライン風のパネル。暗い横線と、ゆっくり流れるハイライト帯を描く。
/// ヘッダーやステータスバーなど、背景が単色のパネルに貼り付けて使う。
/// </summary>
public sealed class ScanlinePanel : Panel
{
    private float _phase;

    public ScanlinePanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>ハイライト帯の位相（0〜1）を進める</summary>
    public void Animate(float phase)
    {
        _phase = phase;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        // 暗い走査線（4px 間隔）
        using (var line = new SolidBrush(Color.FromArgb(18, Color.Black)))
        {
            for (int y = 2; y < Height; y += 4)
            {
                g.FillRectangle(line, 0, y, Width, 1);
            }
        }

        // ゆっくり流れるハイライト帯
        float bandY = _phase * (Height + 120) - 60;
        using (var band = new LinearGradientBrush(
            new Rectangle(0, (int)bandY - 30, Width, 60),
            Color.FromArgb(0, Color.White),
            Color.FromArgb(16, Color.White),
            System.Drawing.Drawing2D.LinearGradientMode.Vertical))
        {
            g.FillRectangle(band, 0, (int)bandY - 30, Width, 60);
        }
    }
}

/// <summary>
/// 起動時のブートシーケンスオーバーレイ。
/// 行を1行ずつタイプアニメーションで表示し、最後にフェードアウトして削除される。
/// </summary>
public sealed class BootOverlay : Control
{
    private readonly string[] _lines;
    private readonly Font _font;
    private readonly Font _titleFont;
    private readonly int _totalChars;

    private int _lineIndex = 1; // 0行目（タイトル）は即表示
    private int _charIndex;
    private int _tick;
    private int _waitTicks;
    private int _fadeTick;
    private bool _finished;

    private const int FadeTicks = 22;

    /// <summary>アニメーションが完全に終わった（削除してよい）</summary>
    public bool Finished => _finished;

    public BootOverlay(string[] lines)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(8, 8, 10);
        Cursor = Cursors.Default;
        _lines = lines;
        _font = ServerTheme.F(11);
        _titleFont = ServerTheme.F(16, FontStyle.Bold);
        _totalChars = lines.Skip(1).Sum(l => l.Length);
    }

    /// <summary>アニメーションタイマーから毎フレーム呼ぶ</summary>
    public void Tick()
    {
        _tick++;
        if (_fadeTick > 0)
        {
            _fadeTick++;
            if (_fadeTick > FadeTicks) _finished = true;
            Invalidate();
            return;
        }
        if (_lineIndex >= _lines.Length)
        {
            _fadeTick = 1;
            Invalidate();
            return;
        }
        if (_waitTicks > 0)
        {
            _waitTicks--;
            Invalidate();
            return;
        }
        if (_charIndex < _lines[_lineIndex].Length)
        {
            _charIndex = Math.Min(_lines[_lineIndex].Length, _charIndex + 3);
        }
        else
        {
            _lineIndex++;
            _waitTicks = 5;
        }
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int alpha = 255;
        if (_fadeTick > 0) alpha = Math.Max(0, 255 - 255 * _fadeTick / FadeTicks);
        if (alpha <= 0) return;

        var green = Color.FromArgb(alpha, ServerTheme.AccentGreen);
        var white = Color.FromArgb(alpha, Color.White);
        var dim = Color.FromArgb(alpha, ServerTheme.TextDim);

        int x = Math.Max(60, Width / 6);
        int baseY = Height / 2 - _lines.Length * 28 / 2 - 30;

        // タイトル
        TextRenderer.DrawText(g, _lines[0], _titleFont, new Rectangle(x, baseY, Width - x * 2, 34), white,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        baseY += 46;

        // タイプ中の行
        for (int i = 1; i < _lines.Length; i++)
        {
            string text = i < _lineIndex ? _lines[i]
                : i == _lineIndex ? _lines[i][..Math.Min(_charIndex, _lines[i].Length)]
                : "";
            var color = i < _lineIndex ? dim : green;
            TextRenderer.DrawText(g, text, _font, new Rectangle(x, baseY, Width - x * 2, 26), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // タイプ中の行末に点滅カーソル
            if (i == _lineIndex && _charIndex < _lines[i].Length && (_tick / 10) % 2 == 0)
            {
                var size = TextRenderer.MeasureText(g, text, _font);
                TextRenderer.DrawText(g, "█", _font, new Point(x + size.Width, baseY + 1), green);
            }
            baseY += 28;
        }

        // プログレスバー
        int typed = 0;
        for (int i = 1; i < Math.Min(_lineIndex, _lines.Length); i++) typed += _lines[i].Length;
        if (_lineIndex < _lines.Length) typed += Math.Min(_charIndex, _lines[_lineIndex].Length);
        int pct = _totalChars == 0 ? 100 : Math.Min(100, typed * 100 / _totalChars);

        var barRect = new Rectangle(x, baseY + 6, 260, 14);
        using (var barPen = new Pen(green, 1))
        {
            g.DrawRectangle(barPen, barRect.X, barRect.Y, barRect.Width, barRect.Height);
        }
        if (pct > 0)
        {
            using var fill = new SolidBrush(green);
            g.FillRectangle(fill, barRect.X + 1, barRect.Y + 1, barRect.Width * pct / 100, barRect.Height - 2);
        }
        TextRenderer.DrawText(g, $"▸ BOOT {pct}%", _font, new Rectangle(barRect.Right + 14, baseY, 200, 26), green,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}
