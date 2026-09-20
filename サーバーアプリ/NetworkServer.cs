using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// TCPサーバー。クライアント接続の受付と、接続状態の監視（ハートビート）を行う。
/// </summary>
public class NetworkServer
{
    public const int DefaultPort = 40000;

    private readonly object _lock = new();
    private readonly List<ClientConnection> _connections = new();
    private TcpListener? _listener;
    private Thread? _acceptThread;
    private volatile bool _running;
    private LogManager _log;

    public int Port { get; private set; }

    public event Action<ClientConnection>? ClientConnected;

    public NetworkServer(LogManager log)
    {
        _log = log;
    }

    public void Start(int port)
    {
        Port = port;
        _running = true;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "AcceptLoop" };
        _acceptThread.Start();

        var heartbeatThread = new Thread(HeartbeatLoop) { IsBackground = true, Name = "Heartbeat" };
        heartbeatThread.Start();
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            try
            {
                var tcp = _listener!.AcceptTcpClient();
                var conn = new ClientConnection(tcp);
                lock (_lock) _connections.Add(conn);
                conn.Disconnected += OnDisconnected;
                _log.Info("接続", "", "", $"クライアントが接続しました（{conn.RemoteEndPoint}）");
                // 受信ループ開始前にイベント購読を完了させる（Hello取りこぼし防止）
                ClientConnected?.Invoke(conn);
                conn.Start();
            }
            catch (Exception ex)
            {
                if (_running) _log.Error("接続エラー", "", "", $"クライアント受け付けに失敗: {ex.Message}");
            }
        }
    }

    private void OnDisconnected(ClientConnection conn, string reason)
    {
        lock (_lock) _connections.Remove(conn);
        _log.Info("切断", conn.PcNumber, "", $"{conn.PcNumber}({conn.RemoteEndPoint}) が切断しました（{reason}）");
    }

    private void HeartbeatLoop()
    {
        while (_running)
        {
            Thread.Sleep(5000);
            List<ClientConnection> stale;
            lock (_lock)
            {
                stale = _connections
                    .Where(c => !c.IsClosed && (DateTime.UtcNow - c.LastSeenUtc).TotalSeconds > 15)
                    .ToList();
            }
            foreach (var c in stale)
            {
                _log.Warn("タイムアウト", c.PcNumber, "", "15秒以上無通信のため切断します");
                c.Close();
            }
        }
    }

    public IReadOnlyList<ClientConnection> Connections()
    {
        lock (_lock) return _connections.ToArray();
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        List<ClientConnection> all;
        lock (_lock) all = _connections.ToList();
        foreach (var c in all) c.Close();
    }

    /// <summary>
    /// 起動中のPCのIPv4アドレス一覧をアダプター名付きで取得する。
    /// 仮想アダプター（VMware/VirtualBox/Hyper-V/WSL/Tailscale/ループバック等）や
    /// リンクローカル・VirtualBoxホストオンリー（192.168.56.0/24）は除外する。
    /// </summary>
    public static List<(string Adapter, string Ip)> GetLocalIPv4Addresses()
    {
        var result = new List<(string, string)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp) continue;

                var name = ni.Name;
                if (name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("VMware", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("WSL", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = addr.Address;
                    // リンクローカル（169.254.0.0/16）は除外
                    if ((ip.GetAddressBytes()[0] & 0xFF) == 169 && (ip.GetAddressBytes()[1] & 0xFF) == 254) continue;
                    // VirtualBoxホストオンリーのデフォルト帯域（192.168.56.0/24）は除外
                    if (IsInSubnet(ip, 192, 168, 56, 0, 24)) continue;
                    // Tailscale（100.64.0.0/10）は除外
                    if (IsInSubnet(ip, 100, 64, 0, 0, 10)) continue;
                    result.Add((ni.Name, ip.ToString()));
                }
            }
        }
        catch
        {
            // 取得失敗時は空リスト
        }
        return result;
    }

    private static bool IsInSubnet(IPAddress ip, byte a, byte b, byte c, byte d, int prefix)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return false;
        uint value = (uint)(bytes[0] << 24) | (uint)(bytes[1] << 16) | (uint)(bytes[2] << 8) | bytes[3];
        uint subnet = (uint)(a << 24) | (uint)(b << 16) | (uint)(c << 8) | d;
        uint mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        return (value & mask) == (subnet & mask);
    }
}
