namespace DrawingGame.Server;

/// <summary>接続デバイス1台分の情報</summary>
public class ClientInfo
{
    public required string PcNumber { get; init; }
    public string DeviceName { get; set; } = "";
    public ClientConnection? Connection { get; set; }
    public DateTime ConnectedAt { get; init; } = DateTime.Now;
    public DateTime LastSeenAt { get; set; } = DateTime.Now;
    public string? PairId { get; set; }

    public bool IsConnected => Connection != null && !Connection.IsClosed;
    public string IpAddress => Connection?.RemoteEndPoint ?? "-";
}

/// <summary>
/// 接続クライアントの登録管理。
/// PC番号の重複検出・不正接続の排除を行う。
/// </summary>
public class ClientManager
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ClientInfo> _byNumber = new();
    private readonly Dictionary<ClientConnection, ClientInfo> _byConnection = new();
    private readonly LogManager _log;

    public ClientManager(LogManager log)
    {
        _log = log;
    }

    /// <summary>
    /// クライアントを登録する。
    /// 同じPC番号の旧接続が生きている場合は、旧接続を切断して新しい接続を優先する
    /// （クライアント再起動時に旧接続がサーバーに残ったままになると、
    ///  新しいクライアントが永久に接続拒否される問題を防ぐため）。
    /// </summary>
    public bool Register(ClientConnection conn, string pcNumber, string deviceName, out string? error)
    {
        error = null;
        lock (_lock)
        {
            if (_byNumber.TryGetValue(pcNumber, out var existing))
            {
                if (existing.IsConnected && existing.Connection != conn)
                {
                    // 同じPC番号で新しい接続が来た：旧接続を切断して新接続に置き換える
                    var old = existing.Connection!;
                    _byConnection.Remove(old);
                    _log.Warn("重複PC番号", pcNumber, "",
                        $"PC番号「{pcNumber}」の旧接続（{old.RemoteEndPoint}）を切断し、新しい接続を優先しました");
                    old.Close();
                }
                else if (existing.Connection != null && existing.Connection != conn)
                {
                    // 旧接続が既に切断済み：登録情報だけを新接続に置き換える（再接続）
                    _byConnection.Remove(existing.Connection);
                    _log.Warn("再接続", pcNumber, "", "古い接続を破棄して再登録します");
                }
                existing.Connection = conn;
                existing.DeviceName = deviceName;
                existing.LastSeenAt = DateTime.Now;
                _byConnection[conn] = existing;
                return true;
            }

            var info = new ClientInfo { PcNumber = pcNumber, DeviceName = deviceName, Connection = conn };
            _byNumber[pcNumber] = info;
            _byConnection[conn] = info;
            return true;
        }
    }

    /// <summary>接続が切れたときに呼ぶ</summary>
    public void Unregister(ClientConnection conn)
    {
        lock (_lock)
        {
            if (!_byConnection.TryGetValue(conn, out var info)) return;
            if (info.Connection == conn)
            {
                info.Connection = null;
                _byConnection.Remove(conn);
                _log.Info("切断", info.PcNumber, info.PairId ?? "", $"{info.PcNumber} の登録を解除しました");
            }
        }
    }

    public ClientInfo? Get(string pcNumber)
    {
        lock (_lock) return _byNumber.TryGetValue(pcNumber, out var info) ? info : null;
    }

    public ClientInfo? GetByConnection(ClientConnection conn)
    {
        lock (_lock) return _byConnection.TryGetValue(conn, out var info) ? info : null;
    }

    public void Touch(string pcNumber)
    {
        lock (_lock)
        {
            if (_byNumber.TryGetValue(pcNumber, out var info))
                info.LastSeenAt = DateTime.Now;
        }
    }

    public IReadOnlyList<ClientInfo> All()
    {
        lock (_lock) return _byNumber.Values.ToArray();
    }

    /// <summary>待機中（未ペア・接続中）のクライアント一覧</summary>
    public IReadOnlyList<ClientInfo> WaitingForPair()
    {
        lock (_lock)
            return _byNumber.Values.Where(i => i.IsConnected && string.IsNullOrEmpty(i.PairId)).ToArray();
    }

    public void SetPair(string pcNumber, string? pairId)
    {
        lock (_lock)
        {
            if (_byNumber.TryGetValue(pcNumber, out var info))
                info.PairId = pairId;
        }
    }

    /// <summary>全クライアントのペア割り当てを解除する（システムリセット時）</summary>
    public void ClearPairAssignments()
    {
        lock (_lock)
        {
            foreach (var info in _byNumber.Values) info.PairId = null;
        }
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _byNumber.Clear();
            _byConnection.Clear();
        }
    }
}
