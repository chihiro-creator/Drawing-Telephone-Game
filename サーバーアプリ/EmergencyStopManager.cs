namespace DrawingGame.Server;

/// <summary>
/// 緊急停止・システム復元・システムリセットを管理する。
/// </summary>
public class EmergencyStopManager
{
    private readonly ServerCore _core;

    public bool IsStopped { get; private set; }

    public EmergencyStopManager(ServerCore core)
    {
        _core = core;
    }

    /// <summary>すべてのゲームを緊急停止状態にする</summary>
    public void EmergencyStop()
    {
        if (IsStopped) return;
        IsStopped = true;
        _core.Log.Warn("緊急停止", "", "", "システムを緊急停止しました");
        _core.Sessions.EmergencyStopAll();
        _core.NotifyEmergencyStateChanged();
    }

    /// <summary>ペアリングと接続を維持し、ゲームを開始前の状態へ戻す</summary>
    public void Restore()
    {
        if (!IsStopped) return;
        IsStopped = false;
        _core.Log.Info("システム復元", "", "", "システムを復元しました（ペアリング・接続は維持、ゲームは最初から）");
        _core.Sessions.RestoreAll();
        _core.NotifyEmergencyStateChanged();
    }

    /// <summary>すべてを初期状態へリセットし、クライアントへアプリ再起動を要求する</summary>
    public void Reset()
    {
        _core.Log.Warn("システムリセット", "", "", "システムをリセットします（クライアントは再起動されます）");

        foreach (var s in _core.Sessions.All())
        {
            foreach (var pc in new[] { s.PcA, s.PcB })
            {
                _core.Sessions.SendToPc(pc, new DrawingGame.Shared.NetMessage
                {
                    Type = DrawingGame.Shared.MessageType.SystemReset,
                    Message = "システムがリセットされました。アプリケーションを再起動します。",
                });
            }
        }

        _core.Sessions.ClearAll();
        _core.Pairs.ClearAll();
        _core.Clients.ClearPairAssignments();
        IsStopped = false;
        _core.NotifyEmergencyStateChanged();
    }
}
