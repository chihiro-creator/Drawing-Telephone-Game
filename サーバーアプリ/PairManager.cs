namespace DrawingGame.Server;

/// <summary>ペア1組</summary>
public class Pair
{
    public required string PairId { get; init; }
    public required string PcA { get; init; }
    public required string PcB { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// ペアの登録・解除管理。
/// 1台のPCは同時に1つのペアにしか所属できない。
/// </summary>
public class PairManager
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Pair> _byId = new();
    private readonly LogManager _log;
    private int _nextNumber = 1;

    public event Action? PairsChanged;

    public PairManager(LogManager log)
    {
        _log = log;
    }

    public bool CreatePair(ClientInfo clientA, ClientInfo clientB, out Pair? pair, out string? error)
    {
        pair = null;
        error = null;

        if (clientA == null || clientB == null)
        {
            error = "クライアントが存在しません";
            return false;
        }
        if (clientA.PcNumber == clientB.PcNumber)
        {
            error = "同じPCをペアリングできません";
            return false;
        }
        if (!clientA.IsConnected || !clientB.IsConnected)
        {
            error = "接続中でないクライアントが含まれています";
            return false;
        }
        lock (_lock)
        {
            if (GetByPcInternal(clientA.PcNumber) != null || GetByPcInternal(clientB.PcNumber) != null)
            {
                error = "既にペアリング済みのクライアントが含まれています";
                return false;
            }

            var pairId = $"PAIR-{_nextNumber++:D3}";
            pair = new Pair { PairId = pairId, PcA = clientA.PcNumber, PcB = clientB.PcNumber };
            _byId[pairId] = pair;
        }
        _log.Info("ペアリング", "", pair.PairId,
            $"{pair.PcA} と {pair.PcB} を{pair.PairId}としてペアリングしました");
        PairsChanged?.Invoke();
        return true;
    }

    public bool RemovePair(string pairId, out string? error)
    {
        error = null;
        Pair? pair;
        lock (_lock)
        {
            if (!_byId.TryGetValue(pairId, out pair))
            {
                error = $"ペア {pairId} は存在しません";
                return false;
            }
            _byId.Remove(pairId);
        }
        _log.Info("ペア解除", "", pairId, $"{pair.PcA}/{pair.PcB} のペアを解除しました");
        PairsChanged?.Invoke();
        return true;
    }

    public Pair? GetById(string pairId)
    {
        lock (_lock) return _byId.TryGetValue(pairId, out var p) ? p : null;
    }

    public Pair? GetByPc(string pcNumber)
    {
        lock (_lock) return GetByPcInternal(pcNumber);
    }

    private Pair? GetByPcInternal(string pcNumber)
    {
        return _byId.Values.FirstOrDefault(p => p.PcA == pcNumber || p.PcB == pcNumber);
    }

    public IReadOnlyList<Pair> All()
    {
        lock (_lock) return _byId.Values.ToArray();
    }

    public void ClearAll()
    {
        lock (_lock)
        {
            _byId.Clear();
            _nextNumber = 1;
        }
    }
}
