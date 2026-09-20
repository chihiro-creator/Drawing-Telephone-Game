using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DrawingGame.Shared;

/// <summary>
/// 通信フレーム処理。
/// メッセージは「4バイト長さ + UTF-8 JSON」で送受信する。
/// </summary>
public static class NetUtil
{
    public const int MaxMessageBytes = 8 * 1024 * 1024;

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void WriteMessage(NetworkStream stream, NetMessage msg)
    {
        var json = JsonSerializer.Serialize(msg, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaxMessageBytes)
            throw new InvalidOperationException($"メッセージが大きすぎます: {bytes.Length}");

        Span<byte> len = stackalloc byte[4];
        BitConverter.TryWriteBytes(len, bytes.Length);
        stream.Write(len);
        stream.Write(bytes);
        stream.Flush();
    }

    /// <summary>
    /// 1メッセージ読み取り。
    /// 接続が閉じられた場合は null を返す。不正なフレームは例外を投げる。
    /// </summary>
    public static NetMessage? ReadMessage(NetworkStream stream)
    {
        var lenBuf = new byte[4];
        if (!ReadExactly(stream, lenBuf, 4)) return null;

        int len = BitConverter.ToInt32(lenBuf, 0);
        if (len <= 0 || len > MaxMessageBytes)
            throw new InvalidDataException($"不正なメッセージ長: {len}");

        var data = new byte[len];
        if (!ReadExactly(stream, data, len)) return null;

        var msg = JsonSerializer.Deserialize<NetMessage>(Encoding.UTF8.GetString(data), JsonOpts);
        if (msg == null) throw new InvalidDataException("メッセージのJSON解析に失敗");
        return msg;
    }

    private static bool ReadExactly(NetworkStream stream, byte[] buf, int count)
    {
        int got = 0;
        while (got < count)
        {
            int n = stream.Read(buf, got, count - got);
            if (n <= 0) return false;
            got += n;
        }
        return true;
    }
}
