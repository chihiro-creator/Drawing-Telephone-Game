using System.Net.Sockets;
using DrawingGame.Shared;

namespace DrawingGame.Client;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error,
}

/// <summary>
/// サーバーへの接続・再接続・受信を管理する。
/// 切断時は指数バックオフで自動再接続し、再接続時にHello(Reconnect=true)を送る。
/// </summary>
public class ServerConnection : IDisposable
{
    private const int ConnectTimeoutMs = 5000;

    private readonly object _lock = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Thread? _runThread;
    private System.Threading.Timer? _pingTimer;
    private volatile bool _disposed;

    private string _serverIp = "";
    private int _port;
    private string _pcNumber = "";
    private string _deviceName = "";
    private bool _hasConnectedOnce;
    private volatile bool _rejected;
    private volatile bool _welcomeReceived;

    public event Action<ConnectionState, string>? StateChanged;
    public event Action<NetMessage>? MessageReceived;

    public bool IsConnected => _tcp?.Connected == true && _stream != null;

    public void Connect(string serverIp, int port, string pcNumber, string deviceName)
    {
        _serverIp = serverIp;
        _port = port;
        _pcNumber = pcNumber;
        _deviceName = deviceName;
        _cts = new CancellationTokenSource();
        _runThread = new Thread(RunLoop) { IsBackground = true, Name = "ClientConn" };
        _runThread.Start();
        _pingTimer = new System.Threading.Timer(_ => SendPing(), null, 3000, 3000);
    }

    private void RunLoop()
    {
        int retryDelayMs = 1000;
        while (!_disposed && !_cts!.IsCancellationRequested && !_rejected)
        {
            _welcomeReceived = false;
            var isReconnect = _hasConnectedOnce;
            try
            {
                StateChanged?.Invoke(isReconnect ? ConnectionState.Reconnecting : ConnectionState.Connecting, isReconnect ? "サーバーに再接続しています…" : "サーバーに接続しています…");

                var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync(_serverIp, _port);
                if (!connectTask.Wait(ConnectTimeoutMs))
                {
                    tcp.Close();
                    throw new TimeoutException("接続タイムアウト");
                }

                _tcp = tcp;
                _tcp.NoDelay = true;
                _stream = tcp.GetStream();

                NetUtil.WriteMessage(_stream, new NetMessage
                {
                    Type = MessageType.Hello,
                    PcNumber = _pcNumber,
                    DeviceName = _deviceName,
                    Version = "1.0.0",
                    Reconnect = isReconnect,
                });

                _hasConnectedOnce = true;
                retryDelayMs = 1000;
                StateChanged?.Invoke(ConnectionState.Connected, "接続済み");

                ReceiveLoop();
            }
            catch (Exception ex)
            {
                if (_disposed || _cts!.IsCancellationRequested) break;
                StateChanged?.Invoke(ConnectionState.Error, $"接続エラー: {ex.Message}");
            }
            finally
            {
                CloseSocket();
            }

            if (_disposed || _cts!.IsCancellationRequested || _rejected) break;

            StateChanged?.Invoke(ConnectionState.Reconnecting, $"{retryDelayMs / 1000}秒後に再接続します…");
            try { Thread.Sleep(retryDelayMs); } catch (ThreadInterruptedException) { break; }
            retryDelayMs = Math.Min(retryDelayMs * 2, 10000);
        }
    }

    private void ReceiveLoop()
    {
        while (!_disposed)
        {
            var msg = NetUtil.ReadMessage(_stream!);
            if (msg == null) throw new IOException("サーバーから切断されました");

            // Welcome 受信前（ハンドシェイク中）に Error を受信した場合はサーバーからの接続拒否。
            // リトライしても解決しない（PC番号重複など）ため、リトライを止めて理由を表示する。
            if (!_welcomeReceived && msg.Type == MessageType.Error)
            {
                _rejected = true;
                StateChanged?.Invoke(ConnectionState.Error, $"接続を拒否されました: {msg.Message ?? "サーバーから接続を拒否されました"}");
                return;
            }
            if (msg.Type == MessageType.Welcome) _welcomeReceived = true;

            try { MessageReceived?.Invoke(msg); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"メッセージ処理で例外: {ex.Message}");
            }
        }
    }

    private void SendPing()
    {
        if (!IsConnected || _disposed) return;
        try
        {
            lock (_lock)
            {
                if (_stream != null)
                {
                    NetUtil.WriteMessage(_stream, new NetMessage { Type = MessageType.Ping });
                }
            }
        }
        catch { /* 切断は受信ループ側で検知される */ }
    }

    public void Send(NetMessage msg)
    {
        if (!IsConnected || _disposed) return;
        try
        {
            lock (_lock)
            {
                if (_stream != null) NetUtil.WriteMessage(_stream, msg);
            }
        }
        catch (Exception ex)
        {
            StateChanged?.Invoke(ConnectionState.Error, $"送信エラー: {ex.Message}");
        }
    }

    private void CloseSocket()
    {
        try { _stream?.Close(); } catch { }
        try { _tcp?.Close(); } catch { }
        _stream = null;
        _tcp = null;
    }

    public void Dispose()
    {
        _disposed = true;
        try { _cts?.Cancel(); } catch { }
        _pingTimer?.Dispose();
        CloseSocket();
    }
}
