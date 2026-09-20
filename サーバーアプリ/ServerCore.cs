using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// サーバーアプリの構成ルート。
/// 各マネージャーを生成・配線し、クライアント接続の受信メッセージをハンドラーへルーティングする。
/// UIに依存しないため、統合テストからも直接利用できる。
/// </summary>
public class ServerCore
{
    public const int DefaultPort = 40000;

    public LogManager Log { get; }
    public NetworkServer Network { get; }
    public ClientManager Clients { get; }
    public PairManager Pairs { get; }
    public GameSessionManager Sessions { get; }
    public GameSettingsManager Settings { get; }
    public QuestionManager Questions { get; }
    public DrawingRelay DrawingRelay { get; }
    public AnswerManager AnswerManager { get; }
    public EmergencyStopManager Emergency { get; }
    public NetworkMessageHandler Handler { get; }

    /// <summary>緊急停止状態が変化した（UI更新用）</summary>
    public event Action? OnEmergencyStateChanged;

    /// <summary>接続デバイス一覧が変化した（UI更新用）</summary>
    public event Action? OnDevicesChanged;

    /// <summary>ペア一覧が変化した（UI更新用）</summary>
    public event Action? OnPairsChanged;

    public bool IsRunning { get; private set; }

    /// <summary>緊急停止状態の変化を通知する（EmergencyStopManagerから呼ばれる）</summary>
    public void NotifyEmergencyStateChanged() => OnEmergencyStateChanged?.Invoke();

    private readonly int _port;

    public ServerCore(int port, string dataDir)
    {
        _port = port;
        Log = new LogManager(Path.Combine(dataDir, "logs"));
        Network = new NetworkServer(Log);
        Clients = new ClientManager(Log);
        Pairs = new PairManager(Log);
        Sessions = new GameSessionManager(this);
        Settings = new GameSettingsManager(Log);
        Questions = new QuestionManager(Log, dataDir);
        DrawingRelay = new DrawingRelay(this);
        AnswerManager = new AnswerManager(this);
        Emergency = new EmergencyStopManager(this);
        Handler = new NetworkMessageHandler(this);

        Network.ClientConnected += conn =>
        {
            conn.MessageReceived += (c, m) =>
            {
                try { Handler.Handle(c, m); }
                catch (Exception ex)
                {
                    Log.Error("サーバー内部エラー", c.PcNumber, "", $"受信処理で例外: {ex.Message}");
                }
            };
            conn.Disconnected += (c, _) =>
            {
                var info = Clients.GetByConnection(c);
                Clients.Unregister(c);
                OnDevicesChanged?.Invoke();
                if (info?.PcNumber != null && !string.IsNullOrEmpty(info.PcNumber))
                {
                    var session = Sessions.GetByPc(info.PcNumber);
                    if (session != null)
                        Log.Warn("切断", info.PcNumber, session.PairId, $"{info.PcNumber} が切断しました（ゲーム進行中はタイマーを停止します）");
                }
            };
            OnDevicesChanged?.Invoke();
        };

        Pairs.PairsChanged += () => OnPairsChanged?.Invoke();
        Sessions.SessionsChanged += () => OnPairsChanged?.Invoke();
    }

    public void Start()
    {
        Log.Start();
        Questions.Load();
        Network.Start(_port);
        Sessions.StartTicker();
        IsRunning = true;
        Log.Info("起動", "", "", $"サーバーをポート {_port} で開始しました");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        Network.Stop();
        Log.Info("終了", "", "", "サーバーを停止しました");
        Log.Shutdown();
    }

    public List<(string Adapter, string Ip)> GetLocalIPv4Addresses() => NetworkServer.GetLocalIPv4Addresses();
}
