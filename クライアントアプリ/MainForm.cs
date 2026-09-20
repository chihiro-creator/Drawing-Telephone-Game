using DrawingGame.Shared;

namespace DrawingGame.Client;

/// <summary>
/// クライアントアプリのメイン画面（ライトテーマ）。
/// サーバー操作待機・準備OK・ゲーム画面（描画/回答）・緊急停止表示を、
/// サーバーから通知されるゲーム状態に従って切り替える。
/// </summary>
public class MainForm : Form
{
    private readonly string _serverIp;
    private readonly string _pcNumber;
    private readonly Logger _log;
    private readonly ServerConnection _connection;
    private readonly ClientStateManager _state;
    private readonly GameStateHandler _handler;
    private readonly ConfigurationManager _config = new();
    private readonly System.Windows.Forms.Timer _uiTimer;

    // レイアウト
    private Label _headerLabel = null!;
    private Label _connStatusLabel = null!;
    private Label _waitingTitleLabel = null!;
    private Panel _waitingPanel = null!;
    private Panel _readyPanel = null!;
    private Panel _gamePanel = null!;
    private Panel _emergencyPanel = null!;
    private Panel _disconnectPanel = null!;
    private Panel _countdownOverlay = null!;
    private Label _countdownLabel = null!;

    // 回答結果オーバーレイ
    private Panel _answerResultOverlay = null!;
    private Label _answerResultLabel = null!;
    private Label _answerResultShadowLabel = null!;
    private Label _answerResultSubLabel = null!;
    private int _shownAnswerSeq = -1;
    private bool _answerResultCorrect;
    private DateTime _answerResultVisibleUntil;

    // ゲーム画面要素
    private Panel _questionBar = null!;
    private Label _questionLabel = null!;
    private DrawingCanvas _canvas = null!;
    private DrawingManager _drawingManager = null!;
    private FlowLayoutPanel _palettePanel = null!;
    private Button _eraserButton = null!;
    private Button _clearButton = null!;
    private TextBox _answerTextBox = null!;
    private Button _answerButton = null!;
    private InputManager _inputManager = null!;
    private Panel _messagePanel = null!;
    private Label _messageLabel = null!;
    private Button _readyButton = null!;
    private Label _readyStatusLabel = null!;
    private Label _waitingStatusLabel = null!;

    private GameState _lastDisplayedState;
    private int _displayRemaining = -1;
    private DateTime _lastDisplaySecond;
    private DateTime _startFlashUntil;
    private DateTime _waitingAnimStart = DateTime.Now;

    public MainForm(string serverIp, string pcNumber)
    {
        _serverIp = serverIp;
        _pcNumber = pcNumber;
        _log = new Logger(pcNumber);
        _state = new ClientStateManager();
        _handler = new GameStateHandler(_state, _log);
        _connection = new ServerConnection();

        BuildUi();
        FullScreen.Attach(this);

        // サーバーイベントの配線
        _connection.StateChanged += (state, message) => SafeInvoke(() =>
        {
            _state.SetConnection(state, message);
            _log.Info($"接続状態: {state} - {message}");
        });
        _connection.MessageReceived += msg => SafeInvoke(() => _handler.OnMessageReceived(msg));

        _state.Changed += () => SafeInvoke(UpdateUi);
        _state.StrokeRelayed += stroke => SafeInvoke(() => _canvas.AddPoints(stroke.StrokeId, stroke.ColorHex, stroke.PenWidth, stroke.Points));
        _state.StrokeSnapshotReceived += strokes => SafeInvoke(() => _canvas.ReplaceAll(strokes));
        _state.StrokeCleared += () => SafeInvoke(() => _canvas.ClearCanvas());
        _state.SystemRestored += () => SafeInvoke(() => _log.Info("システムが復元されました"));
        _state.Unpaired += () => SafeInvoke(() => _log.Info("ペアが解除されました"));
        _state.SystemResetRequested += () => SafeInvoke(RestartForReset);

        // 描画・回答管理
        _drawingManager = new DrawingManager(_canvas, msg => _connection.Send(msg), m => _log.Info(m));
        _inputManager = new InputManager(_answerTextBox, _answerButton, answer =>
        {
            _connection.Send(new NetMessage { Type = MessageType.AnswerSubmit, Answer = answer });
            _log.Info($"回答を送信しました: {answer}");
        });

        _uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _uiTimer.Tick += (_, _) => UpdateUi();
        _uiTimer.Start();

        // サーバーへ接続（メイン画面では既に設定済みのIPを使用）
        _connection.Connect(_serverIp, 40000, _pcNumber, Environment.MachineName);

        // 初回はスナップショット要求（再接続後もStateSyncでサーバーから送られる）
        _connection.MessageReceived += msg =>
        {
            if (msg.Type == MessageType.Welcome && msg.PairId != null)
            {
                _connection.Send(new NetMessage { Type = MessageType.RequestSnapshot });
            }
        };
    }

    private void BuildUi()
    {
        Text = $"お絵かきゲーム - クライアント（{_pcNumber}）";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 620);
        Size = new Size(1100, 760);
        BackColor = ClientTheme.Back;
        ForeColor = ClientTheme.Text;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        // ヘッダー（左: ゲーム情報 / 右: 接続状態）
        var header = new Panel { Dock = DockStyle.Fill, BackColor = ClientTheme.HeaderBack, Padding = new Padding(16, 0, 16, 0) };
        ClientTheme.DrawBottomBorder(header);
        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        _headerLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = ClientTheme.F(12, FontStyle.Bold),
        };
        _connStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "● 未接続",
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true,
            Font = ClientTheme.F(10),
            ForeColor = ClientTheme.TextDim,
        };
        headerLayout.Controls.Add(_headerLabel, 0, 0);
        headerLayout.Controls.Add(_connStatusLabel, 1, 0);
        header.Controls.Add(headerLayout);
        root.Controls.Add(header, 0, 0);

        var body = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(body, 0, 1);

        // ===== 待機画面 =====
        _waitingPanel = new Panel { Dock = DockStyle.Fill, BackColor = ClientTheme.Back };
        var waitingLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(40, 0, 40, 0) };
        waitingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        waitingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        waitingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        _waitingTitleLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "サーバー操作待機中・",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(30, FontStyle.Bold),
            ForeColor = ClientTheme.Text,
        };
        waitingLayout.Controls.Add(_waitingTitleLabel, 0, 0);
        var waitingBox = ClientTheme.InfoBox(ClientTheme.LightBlue);
        waitingBox.Dock = DockStyle.Fill;
        _waitingStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(12),
            ForeColor = ClientTheme.Text,
        };
        waitingBox.Controls.Add(_waitingStatusLabel);
        waitingLayout.Controls.Add(waitingBox, 0, 1);
        waitingLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "サーバーからのペアリング・ゲーム開始指示を待っています",
            TextAlign = ContentAlignment.TopCenter,
            Font = ClientTheme.F(10),
            ForeColor = ClientTheme.TextDim,
        }, 0, 2);
        _waitingPanel.Controls.Add(waitingLayout);

        // ===== 準備画面 =====
        _readyPanel = new Panel { Dock = DockStyle.Fill, BackColor = ClientTheme.Back };
        // 上下に余白を取って、コンテンツ全体を縦中央に配置する
        var readyOuter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24) };
        readyOuter.RowStyles.Add(new RowStyle(SizeType.Percent, 12));
        readyOuter.RowStyles.Add(new RowStyle(SizeType.Percent, 76));
        readyOuter.RowStyles.Add(new RowStyle(SizeType.Percent, 12));

        // 左右にも余白を取って、コンテンツを横中央に配置する
        var readyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5 };
        readyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        readyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 76));
        readyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        readyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 16));
        readyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        readyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        readyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 12));
        readyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 12));

        readyLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "ゲームの説明",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(24, FontStyle.Bold),
            ForeColor = ClientTheme.Text,
        }, 1, 0);

        var descriptionBox = ClientTheme.InfoBox(ClientTheme.LightBlue);
        descriptionBox.Dock = DockStyle.Fill;
        descriptionBox.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "このゲームは、2人1組で遊ぶお絵かき当てゲームです。\n" +
                   "ペアの相手と交代で、お題の絵を描いたり、相手の絵が何なのかを当てたりします。\n" +
                   "当てる側は、制限時間内に回答を入力して正解を目指しましょう！\n" +
                   "両方の「準備OK」がそろうと、ゲームが始まります。\n" +
                   "まずは下の「準備OK」ボタンを押してみましょう！",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(13),
            ForeColor = ClientTheme.Text,
        });
        readyLayout.Controls.Add(descriptionBox, 1, 1);

        _readyButton = ClientTheme.Button("準備OK", 20, ClientTheme.AccentGreen, Color.White, FontStyle.Bold);
        _readyButton.Dock = DockStyle.Fill;
        _readyButton.Margin = new Padding(0, 6, 0, 6);
        _readyButton.Click += (_, _) =>
        {
            _connection.Send(new NetMessage { Type = MessageType.ReadyOk });
            _readyButton.Enabled = false;
            _readyStatusLabel.Text = "相手の準備OKを待っています…";
            _log.Info("準備OKを送信しました");
        };
        readyLayout.Controls.Add(_readyButton, 1, 2);

        _readyStatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "両方の準備OKが押されたらゲームが始まります",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(11),
            ForeColor = ClientTheme.TextDim,
        };
        readyLayout.Controls.Add(_readyStatusLabel, 1, 3);

        readyLayout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "" }, 1, 4);
        readyOuter.Controls.Add(readyLayout, 0, 1);
        _readyPanel.Controls.Add(readyOuter);

        // ===== ゲーム画面 =====
        _gamePanel = new Panel { Dock = DockStyle.Fill, BackColor = ClientTheme.Back };
        var gameLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        gameLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        gameLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        gameLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        gameLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        _questionBar = new Panel { Dock = DockStyle.Fill, BackColor = ClientTheme.LightGreen };
        _questionLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "あなたはお題を見て絵を描いてください！",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(15, FontStyle.Bold),
            ForeColor = ClientTheme.AccentGreen,
            Padding = new Padding(8, 0, 8, 0),
        };
        _questionBar.Controls.Add(_questionLabel);
        gameLayout.Controls.Add(_questionBar, 0, 0);

        var canvasHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var canvasFrame = new Panel { Dock = DockStyle.Fill, BackColor = ClientTheme.Border, Padding = new Padding(1) };
        _canvas = new DrawingCanvas { Dock = DockStyle.Fill };
        canvasFrame.Controls.Add(_canvas);
        canvasHost.Controls.Add(canvasFrame);
        gameLayout.Controls.Add(canvasHost, 0, 1);

        var inputRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(10, 6, 10, 6) };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));

        _palettePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        foreach (var colorHex in DrawingManager.DrawingRelayPalette)
        {
            var btn = new Button
            {
                Size = new Size(42, 42),
                Margin = new Padding(4, 4, 4, 4),
                BackColor = ColorTranslator.FromHtml(colorHex),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Tag = colorHex,
                FlatAppearance = { BorderColor = ClientTheme.Border, BorderSize = 1 },
            };
            btn.Click += (s, _) =>
            {
                if (s is Button b && b.Tag is string hex)
                {
                    _drawingManager.SetColor(hex);
                    HighlightPalette(hex);
                }
            };
            _palettePanel.Controls.Add(btn);
        }

        _eraserButton = ClientTheme.Button("消しゴム", 10, Color.White, Color.Black, FontStyle.Regular);
        _eraserButton.Size = new Size(68, 42);
        _eraserButton.Margin = new Padding(8, 4, 4, 4);
        _eraserButton.FlatAppearance.BorderColor = ClientTheme.Border;
        _eraserButton.FlatAppearance.BorderSize = 1;
        _eraserButton.Click += (_, _) => ToggleEraser();
        _palettePanel.Controls.Add(_eraserButton);

        _clearButton = ClientTheme.Button("全消去", 10, Color.White, ClientTheme.AccentRed, FontStyle.Regular);
        _clearButton.Size = new Size(68, 42);
        _clearButton.Margin = new Padding(4, 4, 4, 4);
        _clearButton.FlatAppearance.BorderColor = ClientTheme.Border;
        _clearButton.FlatAppearance.BorderSize = 1;
        _clearButton.Click += (_, _) => RequestClearCanvas();
        _palettePanel.Controls.Add(_clearButton);

        inputRow.Controls.Add(_palettePanel, 0, 0);

        _answerTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = ClientTheme.F(14),
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "回答を入力(漢字でもひらがなでもカタカナでもOK)",
            Enabled = false,
        };
        inputRow.Controls.Add(_answerTextBox, 1, 0);

        _answerButton = ClientTheme.Button("回答する", 13, ClientTheme.AccentBlue, Color.White, FontStyle.Bold);
        _answerButton.Dock = DockStyle.Fill;
        _answerButton.Enabled = false;
        inputRow.Controls.Add(_answerButton, 2, 0);
        gameLayout.Controls.Add(inputRow, 0, 2);

        _messagePanel = new Panel { Dock = DockStyle.Fill, BackColor = ClientTheme.LightYellow, Visible = false, Padding = new Padding(10, 4, 10, 4) };
        _messageLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(12),
            ForeColor = ClientTheme.Text,
        };
        _messagePanel.Controls.Add(_messageLabel);
        gameLayout.Controls.Add(_messagePanel, 0, 3);
        _gamePanel.Controls.Add(gameLayout);

        // ===== オーバーレイ =====
        _disconnectPanel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = ClientTheme.LightYellow };
        _disconnectPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "サーバーとの接続が切断されました。\n自動的に再接続しています…",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(20, FontStyle.Bold),
            ForeColor = ClientTheme.Text,
        });
        body.Controls.Add(_disconnectPanel);

        _emergencyPanel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = ClientTheme.LightRed };
        _emergencyPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "緊急停止ボタンが押されました。\nしばらくお待ちください。",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(20, FontStyle.Bold),
            ForeColor = ClientTheme.AccentRed,
        });
        body.Controls.Add(_emergencyPanel);

        _countdownOverlay = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.FromArgb(190, 12, 16, 24) };
        _countdownLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "5",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(110, FontStyle.Bold),
            ForeColor = Color.White,
        };
        _countdownOverlay.Controls.Add(_countdownLabel);
        body.Controls.Add(_countdownOverlay);

        body.Controls.Add(_waitingPanel);
        body.Controls.Add(_readyPanel);
        body.Controls.Add(_gamePanel);

        // ===== 回答結果オーバーレイ（正解！/不正解！を中央に大きく表示） =====
        _answerResultOverlay = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.FromArgb(190, 12, 16, 24) };
        var resultLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        resultLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        // 影（少し右下にずらした下書き文字で立体感を出す）
        _answerResultShadowLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(150, FontStyle.Bold),
            ForeColor = Color.FromArgb(140, 0, 0, 0),
            Margin = new Padding(6, 6, 0, 0),
        };
        resultLayout.Controls.Add(_answerResultShadowLabel, 0, 0);
        resultLayout.SetRowSpan(_answerResultShadowLabel, 2);
        _answerResultLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = ClientTheme.F(150, FontStyle.Bold),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 6, 6),
        };
        resultLayout.Controls.Add(_answerResultLabel, 0, 0);
        resultLayout.SetRowSpan(_answerResultLabel, 2);
        _answerResultSubLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            TextAlign = ContentAlignment.TopCenter,
            Font = ClientTheme.F(16, FontStyle.Bold),
            ForeColor = Color.White,
        };
        resultLayout.Controls.Add(_answerResultSubLabel, 0, 2);
        _answerResultOverlay.Controls.Add(resultLayout);
        body.Controls.Add(_answerResultOverlay);

        HighlightPalette("#000000");
    }

    private void HighlightPalette(string hex)
    {
        foreach (Control c in _palettePanel.Controls)
        {
            if (c is Button b)
            {
                bool selected = (b.Tag as string) == hex;
                b.FlatAppearance.BorderColor = selected ? ClientTheme.AccentBlue : ClientTheme.Border;
                b.FlatAppearance.BorderSize = selected ? 3 : 1;
            }
        }
    }

    private void ToggleEraser()
    {
        _drawingManager.SetEraserMode(!_drawingManager.EraserMode);
        UpdateEraserButton();
    }

    private void UpdateEraserButton()
    {
        bool on = _drawingManager.EraserMode;
        _eraserButton.BackColor = on ? ClientTheme.LightYellow : Color.White;
        _eraserButton.FlatAppearance.BorderColor = on ? ClientTheme.AccentRed : ClientTheme.Border;
        _eraserButton.FlatAppearance.BorderSize = on ? 3 : 1;
        _eraserButton.Text = on ? "消しゴムON" : "消しゴム";
    }

    private void RequestClearCanvas()
    {
        if (MessageBox.Show("キャンバスを全消去しますか？", "全消去の確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _drawingManager.RequestClearCanvas();
        _log.Info("全消去をリクエストしました");
    }

    private void SafeInvoke(Action action)
    {
        try
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        catch { }
    }

    /// <summary>サーバーから通知された状態をUIへ反映する（表示専用。状態はサーバーが正）</summary>
    private void UpdateUi()
    {
        if (IsDisposed) return;
        var v = _state.GetView();

        // ヘッダー
        var timeText = v.GameState is GameState.Drawing or GameState.Countdown
            ? $"残り {FormatTime(_displayRemaining >= 0 ? _displayRemaining : v.RemainingSeconds)}"
            : "";
        _headerLabel.Text = $"{_pcNumber}  {v.PairId}  Round {Math.Max(v.Round, 0)}/{Math.Max(v.TotalRounds, 0)}  {timeText}";

        _connStatusLabel.Text = v.ConnectionState switch
        {
            ConnectionState.Connected => "● 接続済み",
            ConnectionState.Connecting => "● 接続中…",
            ConnectionState.Reconnecting => "● 再接続中…",
            ConnectionState.Error => "● 接続エラー",
            _ => "● 未接続",
        };
        _connStatusLabel.ForeColor = v.ConnectionState == ConnectionState.Connected
            ? ClientTheme.AccentGreen
            : v.ConnectionState == ConnectionState.Error
                ? ClientTheme.AccentRed
                : ClientTheme.TextDim;

        // タイマー表示のローカル更新（サーバーから毎秒TimeSyncが届き、同期される）
        if (v.GameState is GameState.Drawing or GameState.Countdown)
        {
            if (_displayRemaining != v.RemainingSeconds)
            {
                _displayRemaining = v.RemainingSeconds;
                _lastDisplaySecond = DateTime.Now;
            }
            else if ((DateTime.Now - _lastDisplaySecond).TotalSeconds >= 1 && _displayRemaining > 0)
            {
                _displayRemaining--;
                _lastDisplaySecond = DateTime.Now;
            }
        }
        else
        {
            _displayRemaining = -1;
        }

        // 画面切り替え
        _waitingPanel.Visible = v.GameState is GameState.WaitingForPair or GameState.WaitingForServer;
        _readyPanel.Visible = v.GameState == GameState.WaitingForReady;
        _gamePanel.Visible = v.GameState is GameState.Countdown or GameState.Drawing or GameState.RoundResult or GameState.GameFinished or GameState.NextRound;
        _emergencyPanel.Visible = v.EmergencyStopped;
        _disconnectPanel.Visible = v.ConnectionState is ConnectionState.Reconnecting or ConnectionState.Connecting or ConnectionState.Error
            && !v.EmergencyStopped;

        _waitingStatusLabel.Text = v.ConnectionState == ConnectionState.Connected
            ? "サーバーと接続されています"
            : v.ConnectionMessage;

        // 待機画面のタイトルにドットのアニメーション
        if (_waitingPanel.Visible)
        {
            int dots = (int)((DateTime.Now - _waitingAnimStart).TotalMilliseconds / 400) % 4;
            _waitingTitleLabel.Text = "サーバー操作待機中" + new string('・', dots + 1);
        }

        // 準備画面
        if (v.GameState == GameState.WaitingForReady)
        {
            if (v.IsReady)
            {
                _readyButton.Enabled = false;
                _readyStatusLabel.Text = "相手の準備OKを待っています…";
            }
            else
            {
                _readyButton.Enabled = true;
                _readyStatusLabel.Text = "両方の準備OKが押されたらゲームが始まります";
            }
            if (v.InfoMessage != null) _readyStatusLabel.Text = v.InfoMessage;
        }

        // ゲーム画面
        if (_gamePanel.Visible)
        {
            var isDrawer = v.MyRole == Role.Drawer;
            var isGuesser = v.MyRole == Role.Guesser;

            if (isDrawer)
            {
                _questionLabel.Text = v.GameState == GameState.Drawing && v.Question != null
                    ? $"あなたはお題を見て絵を描いてください！\nお題：{v.Question}"
                    : "あなたはお題を見て絵を描いてください！";
                _questionLabel.ForeColor = ClientTheme.AccentGreen;
                _questionBar.BackColor = ClientTheme.LightGreen;
            }
            else if (isGuesser)
            {
                _questionLabel.Text = v.GameState == GameState.Drawing
                    ? "あなたは描かれた絵が何なのかを当ててください！"
                    : "あなたは描かれた絵が何なのかを当ててください！";
                _questionLabel.ForeColor = ClientTheme.AccentBlue;
                _questionBar.BackColor = ClientTheme.LightBlue;
            }
            else
            {
                // カウントダウン中など役割未確定のとき
                _questionLabel.Text = "まもなくラウンドが始まります…";
                _questionLabel.ForeColor = ClientTheme.TextDim;
                _questionBar.BackColor = ClientTheme.HeaderBack;
            }

            // 描画権限（描く側のみ・ラウンド中のみ）
            var canDraw = v.GameState == GameState.Drawing && isDrawer;
            _drawingManager.SetDrawingEnabled(canDraw, isDrawer);
            _eraserButton.Enabled = canDraw;
            _clearButton.Enabled = canDraw;
            UpdateEraserButton();

            // 回答権限（当てる側のみ・ラウンド中のみ・回答制限内）
            var canAnswer = v.GameState == GameState.Drawing && isGuesser
                && (v.AnswerLimit == 0 || v.AnswerCount < v.AnswerLimit);
            _inputManager.SetEnabled(canAnswer);

            _palettePanel.Visible = isDrawer;
            _answerTextBox.Visible = isGuesser;
            _answerButton.Visible = isGuesser;

            if (isGuesser)
            {
                _answerTextBox.PlaceholderText = v.AnswerLimit > 0
                    ? $"回答を入力（残り {v.AnswerLimit - v.AnswerCount}回）"
                    : "回答を入力（漢字・ひらがな・カタカナOK）";
            }

            // メッセージ表示（エラーを最優先）
            if (v.LastError != null)
            {
                _messagePanel.Visible = true;
                _messagePanel.BackColor = ClientTheme.LightRed;
                _messageLabel.Text = $"エラー: {v.LastError}";
                _messageLabel.ForeColor = ClientTheme.AccentRed;
            }
            else if (v.InfoMessage != null)
            {
                _messagePanel.Visible = true;
                _messagePanel.BackColor = ClientTheme.LightYellow;
                _messageLabel.Text = v.InfoMessage;
                _messageLabel.ForeColor = v.GameState is GameState.RoundResult or GameState.GameFinished
                    ? ClientTheme.AccentRed
                    : ClientTheme.Text;
            }
            else
            {
                _messagePanel.Visible = false;
            }
        }

        // 回答結果オーバーレイ（正解！/不正解！を中央に大きく表示）
        if (v.LastAnswerResult != null && v.AnswerResultSeq != _shownAnswerSeq)
        {
            _shownAnswerSeq = v.AnswerResultSeq;
            _answerResultCorrect = v.LastAnswerIsCorrect == true;
            _answerResultVisibleUntil = DateTime.Now.AddSeconds(3.5);
            if (_answerResultCorrect)
            {
                _answerResultLabel.Text = "正解！";
                _answerResultLabel.Font = ClientTheme.F(150, FontStyle.Bold);
                _answerResultLabel.ForeColor = Color.FromArgb(140, 255, 175);
                _answerResultSubLabel.Text = "お見事！";
                _answerResultSubLabel.Font = ClientTheme.F(18, FontStyle.Bold);
            }
            else
            {
                _answerResultLabel.Text = "不正解！";
                _answerResultLabel.Font = ClientTheme.F(96, FontStyle.Bold);
                _answerResultLabel.ForeColor = Color.FromArgb(255, 138, 138);
                _answerResultSubLabel.Text = v.AnswerLimit == 0
                    ? "制限時間が無くなるまで何回でも答えられます"
                    : Math.Max(0, v.AnswerLimit - v.AnswerCount) > 0
                        ? $"残り {Math.Max(0, v.AnswerLimit - v.AnswerCount)}回 チャレンジできます。がんばって！"
                        : "回答回数を使い切りました。次ラウンドでリベンジ！";
                _answerResultSubLabel.Font = ClientTheme.F(16, FontStyle.Bold);
            }
            _answerResultShadowLabel.Text = _answerResultLabel.Text;
            _answerResultShadowLabel.Font = _answerResultLabel.Font;
        }

        bool showAnswerResult = _answerResultVisibleUntil > DateTime.Now
            && _gamePanel.Visible
            && v.GameState != GameState.Countdown;
        _answerResultOverlay.Visible = showAnswerResult;

        // カウントダウン表示
        if (v.GameState == GameState.Countdown)
        {
            _countdownOverlay.Visible = true;
            _countdownLabel.Text = v.RemainingSeconds > 0 ? v.RemainingSeconds.ToString() : "START";
        }
        else if (_lastDisplayedState == GameState.Countdown && v.GameState == GameState.Drawing)
        {
            _countdownOverlay.Visible = true;
            _countdownLabel.Text = "START！";
            _startFlashUntil = DateTime.Now.AddSeconds(1.2);
        }
        else if (v.GameState != GameState.Countdown)
        {
            if (_startFlashUntil > DateTime.Now)
            {
                _countdownOverlay.Visible = true;
            }
            else
            {
                _countdownOverlay.Visible = false;
            }
        }

        _lastDisplayedState = v.GameState;
    }

    private static string FormatTime(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    /// <summary>システムリセット：設定を削除してアプリを再起動する</summary>
    private void RestartForReset()
    {
        _log.Warn("システムリセット要求を受信。アプリケーションを再起動します");
        try { _config.Delete(); } catch { }
        try
        {
            var exe = Application.ExecutablePath;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error($"再起動に失敗しました: {ex.Message}");
        }
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _uiTimer.Stop();
        _connection.Dispose();
        _log.Info("クライアントアプリを終了しました");
        base.OnFormClosing(e);
    }
}
