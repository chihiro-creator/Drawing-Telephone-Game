using System.Net.Sockets;
using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// 1クライアント接続を表す。
/// 受信ループを専用スレッドで実行し、受信メッセージをイベントで通知する。
/// </summary>
public class ClientConnection : IDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly object _sendLock = new();
    private readonly CancellationTokenSource _cts = new();
    private Thread? _receiveThread;
    private volatile bool _closed;

    public string PcNumber { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string RemoteEndPoint { get; }
    public DateTime LastSeenUtc { get; private set; } = DateTime.UtcNow;
    public bool IsClosed => _closed;

    public event Action<ClientConnection, NetMessage>? MessageReceived;
    public event Action<ClientConnection, string>? Disconnected;

    public ClientConnection(TcpClient tcp)
    {
        _tcp = tcp;
        _tcp.NoDelay = true;
        _stream = tcp.GetStream();
        RemoteEndPoint = tcp.Client.RemoteEndPoint?.ToString() ?? "?";
    }

    public void Start()
    {
        _receiveThread = new Thread(ReceiveLoop)
        {
            IsBackground = true,
            Name = $"Recv:{RemoteEndPoint}",
        };
        _receiveThread.Start();
    }

    private void ReceiveLoop()
    {
        string reason = "";
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var msg = NetUtil.ReadMessage(_stream);
                if (msg == null) { reason = "接続が閉じられました"; break; }
                LastSeenUtc = DateTime.UtcNow;
                try { MessageReceived?.Invoke(this, msg); }
                catch (Exception ex)
                {
                    reason = $"メッセージ処理で内部エラー: {ex.Message}";
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            reason = _cts.IsCancellationRequested ? "サーバー側で切断" : $"通信エラー: {ex.Message}";
        }

        Close();
        try { Disconnected?.Invoke(this, reason); }
        catch { }
    }

    public void Send(NetMessage msg)
    {
        if (_closed) return;
        try
        {
            lock (_sendLock)
            {
                if (_closed) return;
                NetUtil.WriteMessage(_stream, msg);
            }
        }
        catch
        {
            Close();
        }
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        try { _cts.Cancel(); } catch { }
        try { _tcp.Close(); } catch { }
    }

    public void Dispose() => Close();
}
