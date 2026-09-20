namespace DrawingGame.Client;

/// <summary>
/// クライアントアプリ用のライト（白）テーマ。
/// 白基調で統一し、状態・役割に応じたアクセントカラーを使用する。
/// </summary>
public static class ClientTheme
{
    // ===== 基本色 =====
    public static readonly Color Back = Color.White;
    public static readonly Color HeaderBack = Color.FromArgb(247, 249, 252);
    public static readonly Color Border = Color.FromArgb(212, 220, 230);
    public static readonly Color Text = Color.FromArgb(35, 42, 54);
    public static readonly Color TextDim = Color.FromArgb(122, 130, 144);
    public static readonly Color AccentBlue = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentGreen = Color.FromArgb(34, 150, 80);
    public static readonly Color AccentRed = Color.FromArgb(220, 60, 60);
    public static readonly Color LightBlue = Color.FromArgb(235, 243, 255);
    public static readonly Color LightGreen = Color.FromArgb(235, 250, 240);
    public static readonly Color LightRed = Color.FromArgb(255, 238, 238);
    public static readonly Color LightYellow = Color.FromArgb(255, 249, 230);

    public static Font F(float size, FontStyle style = FontStyle.Regular)
        => new("MS UI Gothic", size, style);

    /// <summary>フラットなボタン</summary>
    public static Button Button(string text, float fontSize, Color back, Color fore, FontStyle style = FontStyle.Regular)
    {
        return new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = F(fontSize, style),
            Cursor = Cursors.Hand,
            FlatAppearance =
            {
                BorderColor = Border,
                BorderSize = 1,
                MouseOverBackColor = Adjust(back, -14),
            },
        };
    }

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

    /// <summary>薄い色の背景を持つ情報ボックス</summary>
    public static Panel InfoBox(Color back)
    {
        return new Panel
        {
            BackColor = back,
            Padding = new Padding(14),
        };
    }

    /// <summary>境界線を下辺に描くパネル</summary>
    public static void DrawBottomBorder(Control c)
    {
        c.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, c.Height - 1, c.Width, c.Height - 1);
        };
    }

    private static Color Adjust(Color c, int amount)
    {
        int R = Math.Clamp(c.R + amount, 0, 255);
        int G = Math.Clamp(c.G + amount, 0, 255);
        int B = Math.Clamp(c.B + amount, 0, 255);
        return Color.FromArgb(c.A, R, G, B);
    }
}
