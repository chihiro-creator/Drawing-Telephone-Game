using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// サーバーアプリのメイン画面（ダークテーマ）。
/// サーバーIP表示・接続デバイス一覧・ペアリング操作・ゲーム監視・ゲーム設定・ログ表示・緊急停止を提供する。
/// </summary>
public class MainForm : Form
{
    private readonly ServerCore _core;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly System.Windows.Forms.Timer _animTimer;

    // ヘッダー
    private Label _ipLabel = null!;
    private Label _stateLabel = null!;
    private Label _stateSubLabel = null!;
    private StatusDot _statusDot = null!;
    private ConsoleButton _emergencyButton = null!;
    private ScanlinePanel _headerPanel = null!;
    private BlinkCursor _cursorLabel = null!;

    // ステータスバー
    private ScanlinePanel _statusBarPanel = null!;
    private Label _statusLabel = null!;
    private BlinkCursor _statusCursor = null!;

    // タブ
    private TabControl _tabs = null!;
    private DataGridView _monitorGrid = null!;
    private DataGridView _deviceGrid = null!;
    private ListBox _waitingList = null!;
    private ListBox _pairList = null!;
    private NumericUpDown _roundsInput = null!;
    private NumericUpDown _timeInput = null!;
    private NumericUpDown _answerLimitInput = null!;
    private Label _questionInfoLabel = null!;
    private DataGridView _questionGrid = null!;
    private DataGridView _logGrid = null!;
    private FlowLayoutPanel _previewWall = null!;
    private readonly Dictionary<string, PairPreviewPanel> _previewPanels = new();

    // 緊急停止オーバーレイ
    private Panel _emergencyPanel = null!;
    private BorderedPanel _emergencyBox = null!;
    private Label _emergencyTitleLabel = null!;

    // アニメーション
    private float _pulsePhase;
    private float _scanPhase;
    private int _animTick;
    private int _emergencyFadeStep;
    private const int EmergencyFadeSteps = 10;
    private BootOverlay? _bootOverlay;

    public MainForm()
    {
        _core = new ServerCore(ServerCore.DefaultPort, AppContext.BaseDirectory);
        _core.OnEmergencyStateChanged += () => SafeInvoke(UpdateEmergencyUi);
        _core.OnDevicesChanged += () => SafeInvoke(RefreshPairUi);
        _core.OnPairsChanged += () => SafeInvoke(RefreshPairUi);
        _core.Log.EntryAdded += entry => SafeInvoke(() => AppendLog(entry));

        BuildUi();
        FullScreen.Attach(this);

        _uiTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _uiTimer.Tick += (_, _) =>
        {
            RefreshDeviceGrid();
            RefreshMonitorGrid();
            RefreshPreviewWall();
            RefreshPairUi();
            RefreshStatusBar();
        };
        _uiTimer.Start();

        // アニメーション用タイマー（ステータスドットの脈動・緊急停止オーバーレイのフェード・CRTエフェクト）
        _animTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _animTimer.Tick += (_, _) =>
        {
            _animTick++;
            _pulsePhase = (_pulsePhase + 0.07f) % 1f;
            _statusDot?.Pulse(_pulsePhase);

            // ブートシーケンス進行
            if (_bootOverlay != null)
            {
                _bootOverlay.Tick();
                if (_bootOverlay.Finished)
                {
                    Controls.Remove(_bootOverlay);
                    _bootOverlay.Dispose();
                    _bootOverlay = null;
                }
            }

            // カーソル点滅
            _cursorLabel?.Tick();
            _statusCursor?.Tick();

            // CRTスキャンライン（ゆっくり流れるハイライト帯）
            _scanPhase = (_scanPhase + 0.0025f) % 1f;
            _headerPanel?.Animate(_scanPhase);
            _statusBarPanel?.Animate(_scanPhase);

            // 緊急停止ボタンの脈動
            _emergencyButton?.SetPulse(_pulsePhase);

            // 緊急停止オーバーレイの点滅（タイトル + 枠）
            if (_emergencyPanel.Visible)
            {
                bool blinkOn = (_animTick / 4) % 2 == 0;
                _emergencyTitleLabel.ForeColor = blinkOn ? ServerTheme.AccentRed : Color.FromArgb(150, 30, 22);
                _emergencyBox.BorderColor = blinkOn ? ServerTheme.AccentRed : Color.FromArgb(110, 20, 14);
            }

            if (_emergencyFadeStep < EmergencyFadeSteps)
            {
                _emergencyFadeStep++;
                float t = _emergencyFadeStep / (float)EmergencyFadeSteps;
                _emergencyPanel.BackColor = Blend(Color.FromArgb(120, 40, 26), Color.FromArgb(16, 8, 7), t);
            }
        };
        _animTimer.Start();

        _core.Start();
        UpdateEmergencyUi();
        RefreshPairUi();
        RefreshQuestionGrid();

        // ===== ブートアニメーション =====
        _bootOverlay = new BootOverlay(new[]
        {
            "▸ OMEGA GAME SERVER v1.0",
            "▸ ターミナルを初期化しています...",
            $"▸ ネットワークを起動しています（port {ServerCore.DefaultPort}）...",
            $"▸ お題データを読み込んでいます（{_core.Questions.Count}件）...",
            "▸ サーバー準備完了。クライアントの接続を待機中...",
        })
        {
            Dock = DockStyle.Fill,
        };
        Controls.Add(_bootOverlay);
        _bootOverlay.BringToFront();
    }

    private void BuildUi()
    {
        Text = "お絵かきゲーム - サーバー";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 640);
        Size = new Size(1200, 760);
        ServerTheme.ApplyForm(this);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 3));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);

        // ===== ヘッダー =====
        _headerPanel = new ScanlinePanel { Dock = DockStyle.Fill, BackColor = ServerTheme.HeaderBack, Padding = new Padding(14, 8, 14, 10) };
        var header = _headerPanel;
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));

        var ipPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        ipPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        ipPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        ipPanel.Controls.Add(ServerTheme.FieldLabel("SERVER TERMINAL // クライアントに設定するサーバーIP", 9.5f, ServerTheme.TextDim), 0, 0);
        _ipLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false,
            Font = ServerTheme.F(12, FontStyle.Bold),
            ForeColor = ServerTheme.AccentGreen,
        };
        var ipRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        ipRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ipRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ipRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
        _cursorLabel = new BlinkCursor
        {
            Dock = DockStyle.Fill,
            Font = ServerTheme.F(12, FontStyle.Bold),
        };
        _cursorLabel.SetInterval(8);
        ipRow.Controls.Add(_ipLabel, 0, 0);
        ipRow.Controls.Add(_cursorLabel, 1, 0);
        ipPanel.Controls.Add(ipRow, 0, 1);

        // 状態表示（パルスドット + [ RUNNING ] + 日本語ステータス）
        var statePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        statePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
        statePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        statePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _statusDot = new StatusDot { Dock = DockStyle.None, Anchor = AnchorStyles.None };
        statePanel.Controls.Add(_statusDot, 0, 0);
        _stateLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "[ RUNNING ]",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = ServerTheme.F(12, FontStyle.Bold),
            ForeColor = ServerTheme.AccentGreen,
        };
        statePanel.Controls.Add(_stateLabel, 1, 0);
        _stateSubLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "通常運転中",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = ServerTheme.F(9.5f),
            ForeColor = ServerTheme.AccentGreen,
        };
        statePanel.Controls.Add(_stateSubLabel, 1, 1);

        _emergencyButton = new ConsoleButton("EMERGENCY STOP", 16)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(10, 14, 4, 14),
            Fill = ServerTheme.AccentRed,
            HoverFill = Color.FromArgb(255, 82, 62),
            Outline = Color.FromArgb(120, 20, 10),
            OutlineWidth = 2,
            TextColor = Color.White,
            Brackets = false,
            TextOffsetY = -3,
        };
        _emergencyButton.Click += (_, _) => ConfirmEmergencyStop();

        headerLayout.Controls.Add(ipPanel, 0, 0);
        headerLayout.Controls.Add(statePanel, 1, 0);
        headerLayout.Controls.Add(_emergencyButton, 2, 0);
        header.Controls.Add(headerLayout);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = ServerTheme.TerminalLine }, 0, 1);

        // ===== タブ =====
        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(BuildMonitorTab());
        _tabs.TabPages.Add(BuildDeviceTab());
        _tabs.TabPages.Add(BuildPairingTab());
        _tabs.TabPages.Add(BuildSettingsTab());
        _tabs.TabPages.Add(BuildLogTab());
        ServerTheme.StyleTabs(_tabs);
        root.Controls.Add(_tabs, 0, 2);

        // ===== ステータスバー =====
        _statusBarPanel = new ScanlinePanel { Dock = DockStyle.Fill, BackColor = ServerTheme.HeaderBack, Padding = new Padding(14, 0, 14, 0) };
        var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 18));
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = ServerTheme.F(9.5f),
            ForeColor = ServerTheme.TextDim,
            Text = "▸ システムを起動しています...",
        };
        _statusCursor = new BlinkCursor
        {
            Dock = DockStyle.Fill,
            Font = ServerTheme.F(9.5f, FontStyle.Bold),
        };
        _statusCursor.SetInterval(8);
        statusLayout.Controls.Add(_statusLabel, 0, 0);
        statusLayout.Controls.Add(_statusCursor, 1, 0);
        _statusBarPanel.Controls.Add(statusLayout);
        root.Controls.Add(_statusBarPanel, 0, 3);

        // ===== 緊急停止オーバーレイ =====
        _emergencyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Visible = false,
            BackColor = Color.FromArgb(16, 8, 7),
        };

        _emergencyBox = new BorderedPanel
        {
            BackColor = Color.FromArgb(14, 8, 7),
            BorderColor = ServerTheme.AccentRed,
            BorderWidth = 3,
        };
        var box = _emergencyBox;
        _emergencyPanel.Resize += (_, _) =>
        {
            box.Size = new Size((int)(_emergencyPanel.Width * 0.62), (int)(_emergencyPanel.Height * 0.58));
            box.Location = new Point((_emergencyPanel.Width - box.Width) / 2, (_emergencyPanel.Height - box.Height) / 2);
        };

        var emergencyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18) };
        emergencyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        emergencyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 12));
        emergencyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
        emergencyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 15));
        emergencyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 26));

        _emergencyTitleLabel = ServerTheme.CenterLabel("■ EMERGENCY STOP ■", 26, ServerTheme.AccentRed, FontStyle.Bold);
        emergencyLayout.Controls.Add(_emergencyTitleLabel, 0, 0);
        emergencyLayout.Controls.Add(ServerTheme.CenterLabel("システムを緊急停止しました", 13, Color.White, FontStyle.Bold), 0, 1);

        var restoreButton = ServerTheme.AccentButton("システムを復元", 14);
        restoreButton.Dock = DockStyle.Fill;
        restoreButton.Click += (_, _) => _core.Emergency.Restore();

        var resetButton = ServerTheme.Button("システムをリセット", 14);
        resetButton.Dock = DockStyle.Fill;
        resetButton.Click += (_, _) => ConfirmSystemReset();

        emergencyLayout.Controls.Add(restoreButton, 0, 2);
        emergencyLayout.Controls.Add(resetButton, 0, 3);
        emergencyLayout.Controls.Add(ServerTheme.CenterLabel(
            "復元: ペアリング・接続を維持してゲームを最初から\nリセット: 全クライアントを初期設定画面から再起動", 10, ServerTheme.TextDim), 0, 4);

        box.Controls.Add(emergencyLayout);
        _emergencyPanel.Controls.Add(box);
        Controls.Add(_emergencyPanel);
        _emergencyPanel.BringToFront();
    }

    // ===== タブ構築 =====

    private TabPage BuildMonitorTab()
    {
        var page = new TabPage("ゲーム監視") { BackColor = ServerTheme.Back };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 62));

        layout.Controls.Add(ServerTheme.ConsoleTitle("MONITOR // ゲーム監視"), 0, 0);

        _monitorGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _monitorGrid.Columns.Add("pair", "ペア");
        _monitorGrid.Columns.Add("members", "メンバー");
        _monitorGrid.Columns.Add("round", "ラウンド");
        _monitorGrid.Columns.Add("state", "状態");
        _monitorGrid.Columns.Add("time", "残り時間");
        _monitorGrid.Columns.Add("roleA", "A役割");
        _monitorGrid.Columns.Add("roleB", "B役割");
        _monitorGrid.Columns.Add("answers", "回答/制限");
        _monitorGrid.Columns.Add("correct", "正解数");
        ServerTheme.StyleGrid(_monitorGrid);
        layout.Controls.Add(_monitorGrid, 0, 1);

        layout.Controls.Add(ServerTheme.ConsoleTitle("PREVIEW WALL // ペア別リアルタイム描画（ペアごとに自動表示）"), 0, 2);

        _previewWall = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = ServerTheme.Back,
            Padding = new Padding(4),
        };
        layout.Controls.Add(_previewWall, 0, 3);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildDeviceTab()
    {
        var page = new TabPage("接続デバイス") { BackColor = ServerTheme.Back };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(ServerTheme.ConsoleTitle("DEVICES // 接続デバイス"), 0, 0);

        _deviceGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _deviceGrid.Columns.Add("pc", "PC番号");
        _deviceGrid.Columns.Add("name", "デバイス名");
        _deviceGrid.Columns.Add("ip", "IPアドレス");
        _deviceGrid.Columns.Add("state", "接続状態");
        _deviceGrid.Columns.Add("pair", "ペア");
        _deviceGrid.Columns.Add("game", "ゲーム状態");
        _deviceGrid.Columns.Add("round", "ラウンド");
        _deviceGrid.Columns.Add("role", "役割");
        _deviceGrid.Columns.Add("time", "残り時間");
        _deviceGrid.Columns.Add("last", "最終通信");
        ServerTheme.StyleGrid(_deviceGrid);
        layout.Controls.Add(_deviceGrid, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPairingTab()
    {
        var page = new TabPage("ペアリング") { BackColor = ServerTheme.Back };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        // 左: 待機中クライアント
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        left.Controls.Add(ServerTheme.ConsoleTitle("PAIRING // 待機中クライアント"), 0, 0);

        _waitingList = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, Font = ServerTheme.F(12) };
        ServerTheme.StyleList(_waitingList);
        left.Controls.Add(_waitingList, 0, 1);

        var goButton = ServerTheme.AccentButton("GO（ペアリング）", 11);
        goButton.Dock = DockStyle.Fill;
        goButton.Click += (_, _) => DoPairing();
        left.Controls.Add(goButton, 0, 2);

        // 右: 現在のペア
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        right.Controls.Add(ServerTheme.ConsoleTitle("PAIRING // 現在のペア"), 0, 0);

        _pairList = new ListBox { Dock = DockStyle.Fill, Font = ServerTheme.F(12) };
        ServerTheme.StyleList(_pairList);
        right.Controls.Add(_pairList, 0, 1);

        var unpairButton = ServerTheme.Button("選択したペアを解除", 11);
        unpairButton.Dock = DockStyle.Fill;
        unpairButton.Click += (_, _) => DoUnpair();
        right.Controls.Add(unpairButton, 0, 2);

        layout.Controls.Add(left, 0, 0);
        layout.Controls.Add(right, 1, 0);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("ゲーム設定") { BackColor = ServerTheme.Back };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(ServerTheme.ConsoleTitle("SETTINGS // ゲーム設定"), 0, 0);

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(8) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        panel.Controls.Add(ServerTheme.FieldLabel("ラウンド数（1〜10）", 11), 0, 0);
        _roundsInput = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 1, Maximum = 10, Value = 3, Font = ServerTheme.F(12) };
        ServerTheme.StyleInput(_roundsInput);
        panel.Controls.Add(_roundsInput, 1, 0);

        panel.Controls.Add(ServerTheme.FieldLabel("1ラウンドの制限時間（秒）", 11), 0, 1);
        _timeInput = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 10, Maximum = 600, Value = 60, Increment = 10, Font = ServerTheme.F(12) };
        ServerTheme.StyleInput(_timeInput);
        panel.Controls.Add(_timeInput, 1, 1);

        panel.Controls.Add(ServerTheme.FieldLabel("回答制限回数（0 = 無制限）", 11), 0, 2);
        _answerLimitInput = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 10, Value = 0, Font = ServerTheme.F(12) };
        ServerTheme.StyleInput(_answerLimitInput);
        panel.Controls.Add(_answerLimitInput, 1, 2);

        var goButton = ServerTheme.AccentButton("GO（設定を確定）", 12);
        goButton.Dock = DockStyle.Fill;
        goButton.Click += (_, _) => ApplySettings();
        panel.Controls.Add(goButton, 1, 3);
        panel.Controls.Add(ServerTheme.FieldLabel("進行中のゲームには影響せず、次回のゲーム開始時に適用されます", 9.5f, ServerTheme.TextDim), 0, 3);

        var questionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        questionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        questionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        questionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var questionHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        questionHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        questionHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        questionHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        questionHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        _questionInfoLabel = new Label { Dock = DockStyle.Fill, Text = "", TextAlign = ContentAlignment.MiddleLeft, Font = ServerTheme.F(10), ForeColor = ServerTheme.Text };
        var addQuestionButton = ServerTheme.AccentButton("お題を追加", 10);
        addQuestionButton.Dock = DockStyle.Fill;
        addQuestionButton.Click += (_, _) => ShowAddQuestionDialog();
        var deleteQuestionButton = ServerTheme.Button("選択を削除", 10);
        deleteQuestionButton.Dock = DockStyle.Fill;
        deleteQuestionButton.Click += (_, _) => DeleteSelectedQuestion();
        var reloadButton = ServerTheme.Button("再読み込み", 10);
        reloadButton.Dock = DockStyle.Fill;
        reloadButton.Click += (_, _) => { _core.Questions.Reload(); RefreshQuestionGrid(); };
        questionHeader.Controls.Add(_questionInfoLabel, 0, 0);
        questionHeader.Controls.Add(addQuestionButton, 1, 0);
        questionHeader.Controls.Add(deleteQuestionButton, 2, 0);
        questionHeader.Controls.Add(reloadButton, 3, 0);

        _questionGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _questionGrid.Columns.Add("text", "お題");
        _questionGrid.Columns.Add("answers", "正解表記");
        _questionGrid.Columns["answers"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        ServerTheme.StyleGrid(_questionGrid);

        questionPanel.Controls.Add(questionHeader, 0, 0);
        questionPanel.Controls.Add(_questionGrid, 0, 1);
        questionPanel.Controls.Add(ServerTheme.FieldLabel("お題はここから追加・削除できます（questions.json に保存されます）", 9.5f, ServerTheme.TextDim), 0, 2);

        layout.Controls.Add(panel, 0, 1);
        layout.Controls.Add(questionPanel, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    /// <summary>お題追加ダイアログを表示して追加する</summary>
    private void ShowAddQuestionDialog()
    {
        using var dialog = new Form
        {
            Text = "お題を追加",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(460, 200),
            BackColor = ServerTheme.Back,
            ForeColor = ServerTheme.Text,
            Font = ServerTheme.F(10),
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(16), BackColor = ServerTheme.Back };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));

        layout.Controls.Add(ServerTheme.FieldLabel("お題", 10), 0, 0);
        var textBox = new TextBox { Dock = DockStyle.Fill, Font = ServerTheme.F(11) };
        ServerTheme.StyleInput(textBox);
        layout.Controls.Add(textBox, 1, 0);

        layout.Controls.Add(ServerTheme.FieldLabel("正解表記（カンマ区切り）", 10), 0, 1);
        var answersBox = new TextBox { Dock = DockStyle.Fill, Font = ServerTheme.F(11) };
        ServerTheme.StyleInput(answersBox);
        layout.Controls.Add(answersBox, 1, 1);

        var buttonRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var okButton = ServerTheme.AccentButton("追加", 11);
        okButton.Dock = DockStyle.Fill;
        okButton.Click += (_, _) => dialog.DialogResult = DialogResult.OK;
        var cancelButton = ServerTheme.Button("キャンセル", 11);
        cancelButton.Dock = DockStyle.Fill;
        cancelButton.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;
        buttonRow.Controls.Add(okButton, 0, 0);
        buttonRow.Controls.Add(cancelButton, 1, 0);
        layout.Controls.Add(buttonRow, 0, 2);
        layout.SetColumnSpan(buttonRow, 2);

        dialog.Controls.Add(layout);
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var text = textBox.Text.Trim();
        var answers = answersBox.Text
            .Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => a.Length > 0)
            .ToList();
        if (!_core.Questions.Add(text, answers, out var error))
        {
            MessageBox.Show(this, error ?? "追加に失敗しました", "お題の追加", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        RefreshQuestionGrid();
    }

    /// <summary>選択中の行をお題一覧から削除する</summary>
    private void DeleteSelectedQuestion()
    {
        if (_questionGrid.SelectedRows.Count == 0)
        {
            MessageBox.Show(this, "削除するお題を選択してください。", "お題の削除", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var text = _questionGrid.SelectedRows[0].Cells["text"].Value?.ToString() ?? "";
        if (text.Length == 0) return;

        var result = MessageBox.Show(this, $"お題「{text}」を削除します。よろしいですか？", "お題の削除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;

        if (!_core.Questions.Remove(text, out var error))
        {
            MessageBox.Show(this, error ?? "削除に失敗しました", "お題の削除", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        RefreshQuestionGrid();
    }

    /// <summary>お題一覧グリッドを最新のデータで再構築する</summary>
    private void RefreshQuestionGrid()
    {
        if (_questionGrid == null || _questionGrid.IsDisposed) return;
        _questionGrid.SuspendLayout();
        _questionGrid.Rows.Clear();
        foreach (var q in _core.Questions.GetAll())
        {
            _questionGrid.Rows.Add(q.Text, string.Join(" / ", q.Answers));
        }
        _questionGrid.ResumeLayout();
        RefreshQuestionInfo();
    }

    private TabPage BuildLogTab()
    {
        var page = new TabPage("ログ表示") { BackColor = ServerTheme.Back };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(ServerTheme.ConsoleTitle("LOG // システムログ"), 0, 0);

        _logGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        _logGrid.Columns.Add("time", "日時");
        _logGrid.Columns.Add("level", "レベル");
        _logGrid.Columns.Add("event", "イベント");
        _logGrid.Columns.Add("pc", "対象PC");
        _logGrid.Columns.Add("pair", "対象ペア");
        _logGrid.Columns.Add("message", "内容");
        _logGrid.Columns["message"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        ServerTheme.StyleGrid(_logGrid);
        layout.Controls.Add(_logGrid, 0, 1);
        page.Controls.Add(layout);

        foreach (var entry in _core.Log.Snapshot())
        {
            AppendLog(entry);
        }
        return page;
    }

    // ===== 操作処理 =====

    private void ConfirmEmergencyStop()
    {
        var result = MessageBox.Show(this,
            "すべてのゲームを緊急停止します。よろしいですか？",
            "EMERGENCY STOP",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result == DialogResult.Yes) _core.Emergency.EmergencyStop();
    }

    private void ConfirmSystemReset()
    {
        var result = MessageBox.Show(this,
            "すべてのクライアントを初期設定画面から再起動します。よろしいですか？",
            "システムリセット",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result == DialogResult.Yes) _core.Emergency.Reset();
    }

    private void DoPairing()
    {
        var selected = _waitingList.SelectedItems.Cast<string>().ToList();
        if (selected.Count != 2)
        {
            MessageBox.Show(this, "待機中のクライアントを2台選択してください。", "ペアリング", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var a = _core.Clients.Get(selected[0]);
        var b = _core.Clients.Get(selected[1]);
        if (!_core.Pairs.CreatePair(a!, b!, out var pair, out var error))
        {
            MessageBox.Show(this, error ?? "ペアリングに失敗しました", "ペアリングエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        _core.Sessions.CreateSession(pair!);
        RefreshPairUi();
        _tabs.SelectedIndex = 0;
    }

    private void DoUnpair()
    {
        var selected = _pairList.SelectedItem as string;
        if (selected == null) return;

        var pairId = selected.Split(' ')[0];
        if (!_core.Pairs.RemovePair(pairId, out var error))
        {
            MessageBox.Show(this, error ?? "解除に失敗しました", "ペア解除", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 解除前にセッションを破棄し、クライアントへ通知する
        var session = _core.Sessions.GetByPair(pairId);
        if (session != null)
        {
            _core.Sessions.SendToPc(session.PcA, new NetMessage { Type = MessageType.Unpaired, Message = "ペアが解除されました" });
            _core.Sessions.SendToPc(session.PcB, new NetMessage { Type = MessageType.Unpaired, Message = "ペアが解除されました" });
            _core.Sessions.DestroySession(pairId);
        }
        RefreshPairUi();
    }

    private void ApplySettings()
    {
        var settings = new GameSettings
        {
            Rounds = (int)_roundsInput.Value,
            TimeSeconds = (int)_timeInput.Value,
            AnswerLimit = (int)_answerLimitInput.Value,
        };
        _core.Settings.Apply(settings);
        MessageBox.Show(this, "ゲーム設定を確定しました。\n次回のゲーム開始時に適用されます。", "ゲーム設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ===== 表示更新 =====

    private void SafeInvoke(Action action)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        catch { }
    }

    private void UpdateEmergencyUi()
    {
        var stopped = _core.Emergency.IsStopped;
        if (stopped && !_emergencyPanel.Visible)
        {
            // 表示直後は明るい色からベース色へフェードインさせる
            _emergencyFadeStep = 0;
            _emergencyPanel.BackColor = Color.FromArgb(120, 40, 26);
        }
        _emergencyPanel.Visible = stopped;
        _tabs.Enabled = !stopped;
        _emergencyButton.Enabled = !stopped;
        _stateLabel.Text = stopped ? "[ EMERGENCY ]" : "[ RUNNING ]";
        _stateLabel.ForeColor = stopped ? ServerTheme.AccentRed : ServerTheme.AccentGreen;
        _stateSubLabel.Text = stopped ? "緊急停止中" : "通常運転中";
        _stateSubLabel.ForeColor = stopped ? ServerTheme.AccentRed : ServerTheme.AccentGreen;
        _statusDot.SetColor(stopped ? ServerTheme.AccentRed : ServerTheme.AccentGreen);
        if (!stopped) RefreshIpLabel();
    }

    private static Color Blend(Color from, Color to, float t)
    {
        int R = (int)(from.R + (to.R - from.R) * t);
        int G = (int)(from.G + (to.G - from.G) * t);
        int B = (int)(from.B + (to.B - from.B) * t);
        return Color.FromArgb(255, R, G, B);
    }

    private void RefreshIpLabel()
    {
        var addrs = _core.GetLocalIPv4Addresses();
        if (addrs.Count == 0)
        {
            _ipLabel.Text = "IPアドレスを取得できませんでした";
            return;
        }
        _ipLabel.Text = string.Join("\n", addrs.Select(a => $"▸ {a.Ip}（{a.Adapter}）"));
    }

    /// <summary>下部ステータスバー（時刻・接続数・最新ログ）を更新する</summary>
    private void RefreshStatusBar()
    {
        if (_statusLabel == null || _statusLabel.IsDisposed) return;
        var clients = _core.Clients.All();
        int connected = clients.Count(c => c.IsConnected);
        int pairs = _core.Pairs.All().Count;
        var last = _core.Log.Snapshot().LastOrDefault();
        string time = DateTime.Now.ToString("HH:mm:ss");
        _statusLabel.Text = $"{time}  ▸ 接続: {connected}台 / ペア: {pairs}  ▸ {last?.ToString() ?? "ログ待機中"}";
    }

    private void RefreshDeviceGrid()
    {
        if (_deviceGrid == null || _deviceGrid.IsDisposed) return;
        var clients = _core.Clients.All();
        var rows = new List<DataGridViewRow>();
        foreach (var c in clients)
        {
            var session = _core.Sessions.GetByPc(c.PcNumber);
            var row = new DataGridViewRow();
            row.CreateCells(_deviceGrid,
                c.PcNumber,
                c.DeviceName,
                c.IpAddress,
                c.IsConnected ? "接続中" : "切断",
                c.PairId ?? "-",
                session?.State.ToString() ?? "-",
                session != null && session.Round > 0 ? $"{session.Round}/{session.TotalRounds}" : "-",
                session != null ? session.RoleOf(c.PcNumber).ToString() : "-",
                session != null ? $"{session.RemainingSeconds}秒" : "-",
                c.LastSeenAt.ToString("HH:mm:ss"));
            rows.Add(row);
        }
        _deviceGrid.SuspendLayout();
        _deviceGrid.Rows.Clear();
        _deviceGrid.Rows.AddRange(rows.ToArray());
        _deviceGrid.ResumeLayout();
    }

    private void RefreshMonitorGrid()
    {
        if (_monitorGrid == null || _monitorGrid.IsDisposed) return;
        var sessions = _core.Sessions.All();
        _monitorGrid.SuspendLayout();
        _monitorGrid.Rows.Clear();
        foreach (var s in sessions)
        {
            var drawerA = s.RoleA == Role.Drawer ? "描く" : "当てる";
            var drawerB = s.RoleB == Role.Drawer ? "描く" : "当てる";
            _monitorGrid.Rows.Add(
                s.PairId,
                $"{s.PcA} / {s.PcB}",
                s.Round > 0 ? $"{s.Round} / {s.TotalRounds}" : "-",
                StateText(s),
                s.State is GameState.Drawing or GameState.Countdown ? $"{s.RemainingSeconds}秒" : "-",
                $"{s.PcA}:{drawerA}",
                $"{s.PcB}:{drawerB}",
                $"{s.AnswerCount} / {(s.AnswerLimit == 0 ? "無制限" : s.AnswerLimit.ToString())}",
                $"{s.CorrectCount}");

            var stateColor = StateCellColor(s.State);
            if (stateColor.HasValue)
            {
                var stateCell = _monitorGrid.Rows[_monitorGrid.Rows.Count - 1].Cells["state"];
                stateCell.Style.BackColor = stateColor.Value;
                stateCell.Style.SelectionBackColor = ServerTheme.Selection;
            }
        }
        _monitorGrid.ResumeLayout();
    }

    /// <summary>
    /// ペアごとの描画プレビュー（マルチビュー）を更新する。
    /// セッションの増減に合わせて画面を自動生成・破棄し、各ペアの描画をリアルタイム表示する。
    /// </summary>
    private void RefreshPreviewWall()
    {
        if (_previewWall == null || _previewWall.IsDisposed) return;
        var sessions = _core.Sessions.All();

        // 新しいセッション（ペア）の画面を追加
        foreach (var s in sessions)
        {
            if (_previewPanels.TryGetValue(s.PairId, out var existing)) continue;
            var panel = new PairPreviewPanel(s.PairId);
            _previewPanels[s.PairId] = panel;
            _previewWall.Controls.Add(panel);
        }

        // 消えたセッションの画面を削除
        if (_previewPanels.Count > sessions.Count)
        {
            var removed = _previewPanels.Keys
                .Where(id => sessions.All(s => s.PairId != id))
                .ToList();
            foreach (var id in removed)
            {
                var panel = _previewPanels[id];
                _previewPanels.Remove(id);
                _previewWall.Controls.Remove(panel);
                panel.Dispose();
            }
        }

        // 各画面のヘッダーと描画を更新
        foreach (var s in sessions)
        {
            if (!_previewPanels.TryGetValue(s.PairId, out var panel)) continue;

            var drawerA = s.RoleA == Role.Drawer ? "描く" : "当てる";
            var drawerB = s.RoleB == Role.Drawer ? "描く" : "当てる";
            var round = s.Round > 0 ? $"Round {s.Round}/{s.TotalRounds}" : "Round -";
            var time = s.State is GameState.Drawing or GameState.Countdown ? $"残り{s.RemainingSeconds}秒" : "";
            panel.SetHeader(s.PairId, $"{s.PcA}({drawerA}) / {s.PcB}({drawerB})",
                $"[{StateText(s)}] {round} {time}", HeaderColor(s.State));
            DrawPreview(panel.Canvas, s);
        }
    }

    private static Color HeaderColor(GameState state) => state switch
    {
        GameState.Drawing => ServerTheme.AccentGreen,
        GameState.Countdown => ServerTheme.AccentBlue,
        GameState.RoundResult => ServerTheme.AccentAmber,
        GameState.GameFinished => ServerTheme.TextDim,
        GameState.EmergencyStopped => ServerTheme.AccentRed,
        _ => ServerTheme.TextDim,
    };

    private static string StateText(GameSession s) => s.State switch
    {
        GameState.WaitingForReady => "準備待ち",
        GameState.Countdown => "カウントダウン",
        GameState.Drawing => "描画中",
        GameState.RoundResult => "ラウンド結果",
        GameState.GameFinished => "ゲーム終了",
        GameState.EmergencyStopped => "緊急停止",
        _ => s.State.ToString(),
    };

    /// <summary>モニタグリッドの「状態」セルを状態に応じて色分けする</summary>
    private static Color? StateCellColor(GameState state) => state switch
    {
        GameState.Countdown => Color.FromArgb(28, 56, 92),
        GameState.Drawing => Color.FromArgb(20, 72, 48),
        GameState.RoundResult => Color.FromArgb(78, 62, 18),
        GameState.GameFinished => Color.FromArgb(52, 52, 64),
        GameState.EmergencyStopped => Color.FromArgb(98, 34, 28),
        _ => null,
    };

    /// <summary>指定のPictureBoxへセッションの現在の描画（ストローク）を描画する</summary>
    private void DrawPreview(PictureBox box, GameSession session)
    {
        if (box == null || box.IsDisposed) return;

        int w = Math.Max(1, box.Width);
        int h = Math.Max(1, box.Height);
        var strokes = session.StrokeSnapshot();
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            if (strokes.Count > 0)
            {
                // 座標範囲を求めてフィットさせる
                var all = strokes.SelectMany(s => s.Points).ToList();
                if (all.Count > 0)
                {
                    int minX = all.Min(p => p.X), maxX = all.Max(p => p.X);
                    int minY = all.Min(p => p.Y), maxY = all.Max(p => p.Y);
                    int spanX = Math.Max(1, maxX - minX);
                    int spanY = Math.Max(1, maxY - minY);
                    float scale = Math.Min((float)(bmp.Width - 16) / spanX, (float)(bmp.Height - 16) / spanY);
                    float ox = (bmp.Width - spanX * scale) / 2 - minX * scale;
                    float oy = (bmp.Height - spanY * scale) / 2 - minY * scale;

                    foreach (var stroke in strokes)
                    {
                        if (stroke.Points.Count == 0) continue;
                        var color = stroke.IsEraser
                            ? Color.White
                            : DrawingRelay.AllowedColors.Contains(stroke.ColorHex ?? "", StringComparer.OrdinalIgnoreCase)
                                ? ColorTranslator.FromHtml(stroke.ColorHex!)
                                : Color.Black;
                        var width = Math.Max(1, stroke.PenWidth);
                        if (stroke.Points.Count == 1)
                        {
                            var p = stroke.Points[0];
                            float r = Math.Max(1, width) * scale / 2f;
                            using var brush = new SolidBrush(color);
                            g.FillEllipse(brush, p.X * scale + ox - r, p.Y * scale + oy - r, r * 2, r * 2);
                            continue;
                        }
                        using var pen = new Pen(color, Math.Max(1, width));
                        var pts = stroke.Points.Select(p => new PointF(p.X * scale + ox, p.Y * scale + oy)).ToArray();
                        g.DrawLines(pen, pts);
                    }
                }
            }
            else
            {
                // 描画データが無い間は「待機中」であることが分かるようにする
                using var hintFont = ServerTheme.F(9.5f, FontStyle.Bold);
                TextRenderer.DrawText(g, "■ 描画データ待機中", hintFont,
                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                    Color.FromArgb(170, 180, 175),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
        var old = box.Image;
        box.Image = bmp;
        old?.Dispose();
    }

    private void RefreshPairUi()
    {
        if (_waitingList == null || _waitingList.IsDisposed) return;
        var waiting = _core.Clients.WaitingForPair().Select(c => c.PcNumber).ToList();
        var pairs = _core.Pairs.All().Select(p =>
        {
            var session = _core.Sessions.GetByPair(p.PairId);
            var state = session != null ? StateText(session) : "セッションなし";
            return $"{p.PairId}  {p.PcA} + {p.PcB}  [{state}]";
        }).ToList();

        // 内容が変わったときだけ再構築（選択状態を維持するため）
        var waitingKey = string.Join("\n", waiting);
        var pairKey = string.Join("\n", pairs);
        if (waitingKey == _waitingListCache && pairKey == _pairListCache) return;

        var waitingSelected = _waitingList.SelectedItems.Cast<string>().ToList();
        var pairSelected = _pairList.SelectedItem as string;

        _waitingList.BeginUpdate();
        _waitingList.Items.Clear();
        foreach (var pc in waiting) _waitingList.Items.Add(pc);
        _waitingList.EndUpdate();
        foreach (var item in waitingSelected)
        {
            var idx = _waitingList.Items.IndexOf(item);
            if (idx >= 0) _waitingList.SetSelected(idx, true);
        }

        _pairList.BeginUpdate();
        _pairList.Items.Clear();
        foreach (var item in pairs) _pairList.Items.Add(item);
        _pairList.EndUpdate();
        if (pairSelected != null)
        {
            var idx = _pairList.Items.IndexOf(pairSelected);
            if (idx >= 0) _pairList.SelectedIndex = idx;
        }

        _waitingListCache = waitingKey;
        _pairListCache = pairKey;
        RefreshQuestionInfo();
    }

    private string _waitingListCache = "";
    private string _pairListCache = "";

    private void RefreshQuestionInfo()
    {
        if (_questionInfoLabel == null || _questionInfoLabel.IsDisposed) return;
        _questionInfoLabel.Text = $"お題データ: {_core.Questions.Count}件（{_core.Questions.FilePath}）";
    }

    private void AppendLog(LogEntry entry)
    {
        if (_logGrid == null || _logGrid.IsDisposed) return;
        _logGrid.Rows.Add(entry.Time.ToString("yyyy/MM/dd HH:mm:ss"), entry.LevelText, entry.EventKind, entry.Pc, entry.Pair, entry.Message);
        var row = _logGrid.Rows[_logGrid.Rows.Count - 1];
        row.Cells["level"].Style.ForeColor = ServerTheme.LevelColor(entry.Level);
        row.Cells["level"].Style.Font = ServerTheme.F(9.5f, FontStyle.Bold);
        if (_logGrid.Rows.Count > 2000)
        {
            _logGrid.Rows.RemoveAt(0);
        }
        _logGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, _logGrid.Rows.Count - 1);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _uiTimer.Stop();
        _animTimer.Stop();
        _core.Stop();
        base.OnFormClosing(e);
    }
}

/// <summary>
/// 1ペア分の描画プレビュー画面（マルチビュー用）。
/// どのペアの画面かを示すヘッダー（ペアID・メンバー・状態・ラウンド・残り時間）と
/// リアルタイム描画を表示するPictureBoxを持つ。
/// </summary>
public sealed class PairPreviewPanel : Panel
{
    public string PairId { get; }
    public PictureBox Canvas { get; }

    private readonly Label _titleLabel;
    private readonly Label _statusLabel;

    public PairPreviewPanel(string pairId)
    {
        PairId = pairId;
        BackColor = ServerTheme.Panel;
        BorderStyle = BorderStyle.FixedSingle;
        Margin = new Padding(6);
        Size = new Size(400, 210);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        _titleLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = ServerTheme.F(9.5f, FontStyle.Bold),
            ForeColor = ServerTheme.AccentGreen,
            Text = pairId,
        };
        Canvas = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = ServerTheme.F(8.5f),
            ForeColor = ServerTheme.TextDim,
            Text = "",
        };

        layout.Controls.Add(_titleLabel, 0, 0);
        layout.Controls.Add(Canvas, 0, 1);
        layout.Controls.Add(_statusLabel, 0, 2);
        Controls.Add(layout);
    }

    /// <summary>ヘッダー（ペア名 + メンバー + 状態）を更新する</summary>
    public void SetHeader(string pairId, string members, string status, Color statusColor)
    {
        _titleLabel.Text = $"{pairId}  ▸  {members}";
        _titleLabel.ForeColor = statusColor;
        _statusLabel.Text = status;
        _statusLabel.ForeColor = ServerTheme.TextDim;
    }
}
