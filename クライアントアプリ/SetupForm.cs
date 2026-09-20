using System.Net;
using System.Net.Sockets;
using DrawingGame.Shared;

namespace DrawingGame.Client;

/// <summary>
/// 初回起動・リセット後の初期設定画面（ライトテーマ）。
/// サーバーIPとパソコン番号を入力してサーバーへ接続する。
/// </summary>
public class SetupForm : Form
{
    private readonly ConfigurationManager _config = new();
    private TextBox _ipTextBox = null!;
    private TextBox _pcTextBox = null!;
    private Button _connectButton = null!;
    private Label _statusLabel = null!;
    private ServerConnection? _connection;

    public string ServerIp { get; private set; } = "";
    public string PcNumber { get; private set; } = "";

    public SetupForm()
    {
        Text = "お絵かきゲーム - クライアント初期設定";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(580, 420);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = ClientTheme.Back;
        ForeColor = ClientTheme.Text;
        FullScreen.Attach(this);

        BuildUi();
        Prefill();
    }

    private void BuildUi()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(28) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "お絵かきゲーム クライアント設定",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = ClientTheme.F(17, FontStyle.Bold),
            ForeColor = ClientTheme.Text,
        }, 0, 0);

        var ipRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        ipRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        ipRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ipRow.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "サーバーIP：", TextAlign = ContentAlignment.MiddleRight, Font = ClientTheme.F(12), ForeColor = ClientTheme.Text }, 0, 0);
        _ipTextBox = new TextBox { Dock = DockStyle.Fill, Font = ClientTheme.F(13), BorderStyle = BorderStyle.FixedSingle };
        ipRow.Controls.Add(_ipTextBox, 1, 0);
        layout.Controls.Add(ipRow, 0, 2);

        var pcRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        pcRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        pcRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pcRow.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "パソコン番号：", TextAlign = ContentAlignment.MiddleRight, Font = ClientTheme.F(12), ForeColor = ClientTheme.Text }, 0, 0);
        _pcTextBox = new TextBox { Dock = DockStyle.Fill, Font = ClientTheme.F(13), BorderStyle = BorderStyle.FixedSingle };
        pcRow.Controls.Add(_pcTextBox, 1, 0);
        layout.Controls.Add(pcRow, 0, 3);

        _connectButton = ClientTheme.Button("サーバーへ接続", 13, ClientTheme.AccentBlue, Color.White, FontStyle.Bold);
        _connectButton.Dock = DockStyle.Fill;
        _connectButton.Click += (_, _) => TryConnect();
        layout.Controls.Add(_connectButton, 0, 4);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "接続すると「サーバー操作待機中・・・」になります",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ClientTheme.TextDim,
            Font = ClientTheme.F(10),
        };
        layout.Controls.Add(_statusLabel, 0, 5);

        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "※ サーバーアプリの画面に表示されているIPアドレスを入力してください\n※ パソコン番号は半角英数字で（例: PC01）",
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = ClientTheme.TextDim,
            Font = ClientTheme.F(10),
        }, 0, 6);

        Controls.Add(layout);
    }

    private void Prefill()
    {
        var cfg = _config.Load();
        _ipTextBox.Text = cfg.ServerIp;
        _pcTextBox.Text = cfg.PcNumber;
    }

    private void TryConnect()
    {
        var ip = _ipTextBox.Text.Trim();
        var pc = _pcTextBox.Text.Trim().ToUpperInvariant();

        if (!IPAddress.TryParse(ip, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
        {
            _statusLabel.Text = "サーバーIPはIPv4アドレスを入力してください（例: 192.168.1.10）";
            _statusLabel.ForeColor = ClientTheme.AccentRed;
            return;
        }
        if (pc.Length == 0 || pc.Length > 16 || !pc.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
        {
            _statusLabel.Text = "パソコン番号は半角英数字で入力してください（例: PC01）";
            _statusLabel.ForeColor = ClientTheme.AccentRed;
            return;
        }

        _connectButton.Enabled = false;
        _statusLabel.Text = "サーバーへ接続中…";
        _statusLabel.ForeColor = ClientTheme.AccentBlue;

        _connection?.Dispose();
        _connection = new ServerConnection();
        _connection.StateChanged += (state, message) =>
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                switch (state)
                {
                    case ConnectionState.Connected:
                        OnConnected();
                        break;
                    case ConnectionState.Error:
                        _statusLabel.Text = $"接続エラー: {message}";
                        _statusLabel.ForeColor = ClientTheme.AccentRed;
                        _connectButton.Enabled = true;
                        break;
                    default:
                        _statusLabel.Text = message;
                        _statusLabel.ForeColor = ClientTheme.AccentBlue;
                        break;
                }
            });
        };
        _connection.Connect(ip, ServerCorePort, pc, Environment.MachineName);
    }

    private const int ServerCorePort = 40000;

    private void OnConnected()
    {
        // 設定を保存してメイン画面へ
        ServerIp = _ipTextBox.Text.Trim();
        PcNumber = _pcTextBox.Text.Trim().ToUpperInvariant();
        _config.Save(new ClientConfig { ServerIp = ServerIp, PcNumber = PcNumber });

        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _connection?.Dispose();
        base.OnFormClosed(e);
    }
}
