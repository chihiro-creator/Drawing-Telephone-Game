using System.Text;
using DrawingGame.Server;
using DrawingGame.Shared;

namespace DrawingGame.Tests;

/// <summary>
/// 統合テスト：サーバーコアを実際に起動し、実プロトコルで通信するヘッドレスクライアントを使って
/// 通信・ペアリング・準備・ゲーム状態遷移・役割交代・描画中継・回答判定・回答制限・再接続同期・
/// セッション分離・緊急停止・復元・リセットを検証する。
/// 実行: dotnet run --project DrawingGame.Tests
/// </summary>
internal static class Program
{
    private const int Port = 41001;
    private static int _passCount;
    private static int _failCount;
    private static readonly List<string> Failures = new();

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var dataDir = Path.Combine(Path.GetTempPath(), "drawinggame_test");
        try { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); } catch { }
        Directory.CreateDirectory(dataDir);

        var core = new ServerCore(Port, dataDir);
        core.Start();
        core.Settings.Apply(new GameSettings { Rounds = 2, TimeSeconds = 10, AnswerLimit = 5 });

        TestClient? pc01 = null, pc02 = null, pc03 = null, pc04 = null;
        try
        {
            Test1_ConnectionAndHello(core, ref pc01, ref pc02);
            Test2_DuplicatePcReplaced(core, ref pc01);
            Test3_PairingAndReady(core, pc01!, pc02!);
            Test4_Round1RolesAndDrawing(core, pc01!, pc02!);
            Test5_AnswerJudge(core, pc01!, pc02!);
            Test6_RoleSwap(core, pc01!, pc02!);
            Test7_ReconnectResync(core, ref pc01, ref pc02);
            Test8_AnswerLimitAndTimeout(core, pc01!, pc02!);
            Test9_SessionIsolation(core, ref pc03, ref pc04, pc01!, pc02!);
            Test10_EmergencyStopRestoreReset(core, pc01!, pc02!, pc03!, pc04!);
        }
        finally
        {
            pc01?.Dispose(); pc02?.Dispose(); pc03?.Dispose(); pc04?.Dispose();
            core.Stop();
        }

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine($"テスト結果: 成功 {_passCount} / 失敗 {_failCount}");
        foreach (var f in Failures) Console.WriteLine($"  FAIL: {f}");
        Console.WriteLine("========================================");
        return _failCount == 0 ? 0 : 1;
    }

    private static void Check(bool ok, string name)
    {
        if (ok) { _passCount++; Console.WriteLine($"  PASS: {name}"); }
        else { _failCount++; Failures.Add(name); Console.WriteLine($"  FAIL: {name}"); }
    }

    // ---------- 1. 接続とHello ----------

    private static void Test1_ConnectionAndHello(ServerCore core, ref TestClient? pc01, ref TestClient? pc02)
    {
        Console.WriteLine("[1] 接続・Hello・サーバー操作待機中");

        pc01 = new TestClient("127.0.0.1", Port, "PC01");
        pc02 = new TestClient("127.0.0.1", Port, "PC02");
        var c1 = pc01!;
        var c2 = pc02!;

        Check(c1.WaitForState(() => c1.GameState == GameState.WaitingForPair, 5000),
            "PC01 が Welcome を受信し WaitingForPair");
        Check(c2.WaitForState(() => c2.GameState == GameState.WaitingForPair, 5000),
            "PC02 が Welcome を受信し WaitingForPair");
        Check(core.Clients.All().Count == 2, "サーバーの接続デバイス一覧が2台");
        Check(core.Log.Snapshot().Any(e => e.Pc == "PC01" && e.EventKind == "接続"),
            "ログに PC01 の接続が記録された");
    }

    // ---------- 2. 重複PC番号（旧接続を切断して新接続を優先） ----------

    private static void Test2_DuplicatePcReplaced(ServerCore core, ref TestClient? pc01)
    {
        Console.WriteLine("[2] 重複PC番号（旧接続を切断して新しい接続を優先）");

        // PC01 の旧接続が生きている状態で同じPC番号から接続する
        using var dup = new TestClient("127.0.0.1", Port, "PC01");
        Check(dup.WaitForState(() => dup.GameState == GameState.WaitingForPair, 3000),
            "重複PC番号でも新しい接続は受け付けられる（Welcome受信）");
        Check(core.Log.Snapshot().Any(e => e.EventKind == "重複PC番号"),
            "ログに重複PC番号の切り替えが記録された");

        // 旧接続はサーバー側で切断され、登録数は2台のまま
        Thread.Sleep(500);
        Check(core.Clients.All().Count == 2, "登録数は2台のまま");
        Check(core.Clients.Get("PC01")?.IsConnected == true, "PC01 の登録は新しい接続に置き換わっている");

        // 旧接続は使い物にならないため破棄し、後続テスト用に再接続する
        var old = pc01;
        pc01 = null;
        old?.Dispose();
        var reconnected = new TestClient("127.0.0.1", Port, "PC01");
        pc01 = reconnected;
        Check(reconnected.WaitForState(() => reconnected.GameState == GameState.WaitingForPair, 3000),
            "PC01 が再接続された");
    }

    // ---------- 3. ペアリングと準備OK ----------

    private static void Test3_PairingAndReady(ServerCore core, TestClient pc01, TestClient pc02)
    {
        Console.WriteLine("[3] ペアリングと準備OK（片方だけ準備OKでは開始しない）");

        var a = core.Clients.Get("PC01")!;
        var b = core.Clients.Get("PC02")!;
        Check(core.Pairs.CreatePair(a, b, out var pair, out var err) && pair != null,
            $"ペアリング作成（{pair?.PairId}）");
        var session = core.Sessions.CreateSession(pair!);
        Check(session != null, "ゲームセッションが生成された");

        Check(pc01.WaitForState(() => pc01.PairId == "PAIR-001" && pc01.GameState == GameState.WaitingForReady, 5000),
            "PC01 へペアリング完了通知（WaitingForReady）");
        Check(pc02.WaitForState(() => pc02.PairId == "PAIR-001" && pc02.GameState == GameState.WaitingForReady, 5000),
            "PC02 へペアリング完了通知（WaitingForReady）");

        pc01.SendReady();
        Thread.Sleep(1500);
        Check(pc01.GameState == GameState.WaitingForReady && pc02.GameState == GameState.WaitingForReady,
            "片方だけ準備OKではゲーム開始しない");

        pc02.SendReady();
        Check(pc01.WaitForState(() => pc01.GameState == GameState.Countdown, 4000) &&
              pc02.WaitForState(() => pc02.GameState == GameState.Countdown, 4000),
            "両方準備OKでカウントダウン開始");
        Check(core.Log.Snapshot().Any(e => e.EventKind == "ゲーム開始" && e.Pair == "PAIR-001"),
            "ログにゲーム開始が記録された");
    }

    // ---------- 4. ラウンド1の役割と描画 ----------

    private static void Test4_Round1RolesAndDrawing(ServerCore core, TestClient pc01, TestClient pc02)
    {
        Console.WriteLine("[4] ラウンド1 役割決定と描画中継");

        Check(pc01.WaitForState(() => pc01.GameState == GameState.Drawing, 12000) &&
              pc02.WaitForState(() => pc02.GameState == GameState.Drawing, 12000),
            "カウントダウン後にラウンド1（Drawing）開始");

        var session = core.Sessions.GetByPair("PAIR-001")!;
        Check(session.Round == 1, "ラウンド1が開始");
        Check(session.RoleA != session.RoleB &&
              (session.RoleA == Role.Drawer || session.RoleA == Role.Guesser),
            $"役割が決定（{session.PcA}:{session.RoleA} / {session.PcB}:{session.RoleB}）");

        var drawerPc = session.RoleA == Role.Drawer ? session.PcA : session.PcB;
        var guesserPc = session.RoleA == Role.Guesser ? session.PcA : session.PcB;
        var drawer = drawerPc == "PC01" ? pc01 : pc02;
        var guesser = guesserPc == "PC01" ? pc01 : pc02;

        Check(drawer.Question != null, $"描く側にお題が表示（{drawer.Question}）");
        Check(guesser.Question == null, "当てる側にお題は表示されない");

        // 当てる側が描画データを送っても無視される（操作権限検証）
        guesser.SendStroke(9999, "#FF0000", 3,
            new List<StrokePoint> { new(10, 10), new(50, 50) }, false);
        Thread.Sleep(800);
        Check(!drawer.HasMessage(m => m.Type == MessageType.StrokeRelay && m.Stroke?.StrokeId == 9999),
            "当てる側の描画データは中継されない");
        Check(core.Log.Snapshot().Any(e => e.EventKind == "描画拒否" && e.Pc == guesserPc),
            "ログに操作権限違反（描画拒否）が記録された");

        // 描く側が描画 → サーバー → 当てる側 へリアルタイム中継
        long strokeId = 1001;
        drawer.SendStroke(strokeId, "#0000FF", 3,
            new List<StrokePoint> { new(100, 100), new(200, 150), new(300, 120) }, false);
        var relayed = guesser.WaitFor(
            m => m.Type == MessageType.StrokeRelay && m.Stroke?.StrokeId == strokeId,
            3000, out _);
        Check(relayed != null && relayed.Stroke!.Points.Count == 3 && relayed.Stroke.ColorHex == "#0000FF",
            "描画データがサーバー経由で当てる側へ中継された（座標・色一致）");

        drawer.SendStroke(strokeId, "#0000FF", 3,
            new List<StrokePoint> { new(400, 200) }, true);
        Check(guesser.WaitFor(
            m => m.Type == MessageType.StrokeRelay && m.Stroke?.StrokeId == strokeId,
            3000, out _) != null, "ストローク終了が中継された");

        Check(pc01.WaitFor(m => m.Type == MessageType.TimeSync, 3000, out _) != null,
            "サーバー基準のTimeSyncが受信される");
    }

    // ---------- 5. 回答判定 ----------

    private static void Test5_AnswerJudge(ServerCore core, TestClient pc01, TestClient pc02)
    {
        Console.WriteLine("[5] 回答の正誤判定（サーバー側）");

        var session = core.Sessions.GetByPair("PAIR-001")!;
        var guesser = session.RoleA == Role.Guesser ? pc01 : pc02;
        var drawer = session.RoleA == Role.Drawer ? pc01 : pc02;

        guesser.SendAnswer("ぜんぜん違う答え");
        var wrong = guesser.WaitFor(m => m.Type == MessageType.AnswerResult && !m.IsCorrect, 3000, out _);
        Check(wrong != null, "不正解の回答には不正解結果が返る");
        Check(!drawer.HasMessage(m => m.Type == MessageType.AnswerResult && !m.IsCorrect),
            "不正解は回答者本人にのみ通知される");

        var correctText = AnswerNormalizer.Normalize(session.Question);
        var alternate = session.AcceptableAnswers
            .Select(AnswerNormalizer.Normalize)
            .FirstOrDefault(a => a.Length > 0 && a != correctText);

        if (alternate != null)
        {
            // ゲーム設定の「正解表記」に登録された別表記（漢字/カタカナ等）でも正解扱いになる
            guesser.SendAnswer(alternate);
            var alt = guesser.WaitFor(m => m.Type == MessageType.AnswerResult && m.IsCorrect, 3000, out _);
            Check(alt != null, $"正解表記の別表記（{alternate}）でも正解結果が返る");
        }
        else
        {
            guesser.SendAnswer(correctText);
            var ok = guesser.WaitFor(m => m.Type == MessageType.AnswerResult && m.IsCorrect, 3000, out _);
            Check(ok != null, $"正解の回答（{correctText}）には正解結果が返る");
        }

        Check(drawer.WaitFor(m => m.Type == MessageType.AnswerResult && m.IsCorrect, 3000, out _) != null,
            "正解時は両クライアントへ通知される");

        Check(pc01.WaitForState(() => pc01.GameState == GameState.RoundResult, 3000) &&
              pc02.WaitForState(() => pc02.GameState == GameState.RoundResult, 3000),
            "正解でラウンド結果状態へ遷移");
    }

    // ---------- 6. 役割交代 ----------

    private static void Test6_RoleSwap(ServerCore core, TestClient pc01, TestClient pc02)
    {
        Console.WriteLine("[6] 2ラウンド目で役割が入れ替わる");

        var session = core.Sessions.GetByPair("PAIR-001")!;
        var roleA_round1 = session.RoleA;

        Check(pc01.WaitForState(() => pc01.Round == 2 && pc01.GameState == GameState.Drawing, 15000) &&
              pc02.WaitForState(() => pc02.Round == 2 && pc02.GameState == GameState.Drawing, 15000),
            "ラウンド2が開始");
        Check(session.RoleA == (roleA_round1 == Role.Drawer ? Role.Guesser : Role.Drawer),
            "前ラウンドと逆の役割になった");
    }

    // ---------- 7. 再接続同期 ----------

    private static void Test7_ReconnectResync(ServerCore core, ref TestClient? pc01, ref TestClient? pc02)
    {
        Console.WriteLine("[7] 再接続時の状態同期（切断→再接続で描画状態も復元）");

        var session = core.Sessions.GetByPair("PAIR-001")!;
        var drawerPc = session.RoleA == Role.Drawer ? session.PcA : session.PcB;
        var guesserPc = session.RoleA == Role.Guesser ? session.PcA : session.PcB;
        var drawer = drawerPc == "PC01" ? pc01 : pc02;
        var guesser = guesserPc == "PC01" ? pc01 : pc02;

        // ラウンド2の描画データを送る
        long strokeId = 2001;
        drawer!.SendStroke(strokeId, "#00AA00", 3,
            new List<StrokePoint> { new(50, 50), new(120, 80) }, false);
        Check(guesser!.WaitFor(m => m.Type == MessageType.StrokeRelay && m.Stroke?.StrokeId == strokeId, 3000, out _) != null,
            "ラウンド2の描画が中継された");

        // 当てる側を切断
        guesser.Dispose();
        if (guesserPc == "PC01") pc01 = null;
        else pc02 = null;
        Thread.Sleep(1200);

        // 再接続（同じPC番号）→ サーバーが現在の状態とストロークを送信する
        var reconnected = new TestClient("127.0.0.1", Port, guesserPc);
        if (guesserPc == "PC01") pc01 = reconnected;
        else pc02 = reconnected;

        Check(reconnected.WaitForState(() => reconnected.GameState == GameState.Drawing && reconnected.PairId == "PAIR-001", 5000),
            "再接続後に現在のゲーム状態が同期された（Drawing / PAIR-001）");
        Check(reconnected.WaitFor(m => m.Type == MessageType.StrokeSnapshot, 5000, out _) != null,
            "再接続後にストロークスナップショットを受信");
        Check(reconnected.HasMessage(m => m.Type == MessageType.StrokeSnapshot &&
              m.Strokes != null && m.Strokes.Any(s => s.StrokeId == strokeId)),
            "スナップショットに切断前のストロークが含まれる");

        // 再接続後も回答可能（タイマーはサーバー側で停止していた）
        pc01!.ClearMessages();
        pc02!.ClearMessages();
        var correctText = AnswerNormalizer.Normalize(session.Question);
        reconnected.SendAnswer(correctText);
        Check(pc01.WaitFor(m => m.Type == MessageType.AnswerResult && m.IsCorrect, 3000, out _) != null ||
              pc02.WaitFor(m => m.Type == MessageType.AnswerResult && m.IsCorrect, 3000, out _) != null,
            "再接続後の回答が正しく判定された");
    }

    // ---------- 8. 回答制限とタイムアウト ----------

    private static void Test8_AnswerLimitAndTimeout(ServerCore core, TestClient pc01, TestClient pc02)
    {
        Console.WriteLine("[8] ゲーム終了と準備状態への復帰");

        // 正解で RoundResult → GameFinished（2ラウンド構成）→ 準備状態へ戻る
        Check(pc01.WaitForState(() => pc01.GameState == GameState.WaitingForReady && pc01.PairId == "PAIR-001", 30000) &&
              pc02.WaitForState(() => pc02.GameState == GameState.WaitingForReady && pc02.PairId == "PAIR-001", 30000),
            "ゲーム終了後、ペア維持のまま準備状態へ戻る");
        Check(pc01.HasMessage(m => m.Type == MessageType.GameFinished),
            "GameFinishedメッセージを受信");
        Check(core.Log.Snapshot().Any(e => e.EventKind == "ゲーム終了" && e.Pair == "PAIR-001"),
            "ログにゲーム終了が記録された");
    }

    // ---------- 9. セッション分離 ----------

    private static void Test9_SessionIsolation(ServerCore core, ref TestClient? pc03, ref TestClient? pc04,
        TestClient pc01, TestClient pc02)
    {
        Console.WriteLine("[9] 別ペアへのデータ漏洩防止");

        pc03 = new TestClient("127.0.0.1", Port, "PC03");
        pc04 = new TestClient("127.0.0.1", Port, "PC04");
        var c3 = pc03!;
        var c4 = pc04!;
        Check(c3.WaitForState(() => c3.GameState == GameState.WaitingForPair, 5000) &&
              c4.WaitForState(() => c4.GameState == GameState.WaitingForPair, 5000),
            "PC03/PC04 が接続");

        var a = core.Clients.Get("PC03")!;
        var b = core.Clients.Get("PC04")!;
        Check(core.Pairs.CreatePair(a, b, out var pair, out _) && pair != null, "PAIR-002 作成");
        core.Sessions.CreateSession(pair!);

        Check(c3.WaitForState(() => c3.PairId == "PAIR-002" && c3.GameState == GameState.WaitingForReady, 5000),
            "PC03 が PAIR-002 へペアリング");

        // 準備OK前（WaitingForReady）にPC03から描画を送る → 拒否され、誰にも中継されない
        pc03.SendStroke(3001, "#FF0000", 3, new List<StrokePoint> { new(1, 1), new(9, 9) }, false);
        Thread.Sleep(1000);
        Check(!pc04.HasMessage(m => m.Type == MessageType.StrokeRelay && m.Stroke?.StrokeId == 3001),
            "準備OK前の描画は中継されない（PAIR-002 内でも拒否）");
        Check(!pc01.HasMessage(m => m.Type == MessageType.StrokeRelay && m.Stroke?.StrokeId == 3001) &&
              !pc02.HasMessage(m => m.Type == MessageType.StrokeRelay && m.Stroke?.StrokeId == 3001),
            "PAIR-002 の描画が PAIR-001 へ漏れない");
        Check(core.Log.Snapshot().Any(e => e.EventKind == "描画拒否" && e.Pc == "PC03"),
            "ログに描画拒否が記録された");

        // 準備OK前の回答も拒否される
        pc03.SendAnswer("りんご");
        Thread.Sleep(1000);
        Check(pc03.HasMessage(m => m.Type == MessageType.Error),
            "準備OK前の回答はエラー通知される");
        Check(!pc01.HasMessage(m => m.Type == MessageType.AnswerResult && m.Answer == "りんご") &&
              !pc04.HasMessage(m => m.Type == MessageType.AnswerResult && m.Answer == "りんご"),
            "別ペアの回答は一切通知されない");

        // PAIR-002 の回答を PAIR-001 のセッションへ送ろうとしても拒否される（セッションID検証）
        var s1 = core.Sessions.GetByPair("PAIR-001")!;
        pc03.Send(new NetMessage
        {
            Type = MessageType.AnswerSubmit,
            Answer = "不正アクセス",
            SessionId = s1.SessionId,
        });
        Thread.Sleep(800);
        Check(core.Log.Snapshot().Any(e => e.EventKind == "セッション不正" && e.Pc == "PC03"),
            "別セッションへの回答アクセスはログに記録される");
    }

    // ---------- 10. 緊急停止・復元・リセット ----------

    private static void Test10_EmergencyStopRestoreReset(ServerCore core,
        TestClient pc01, TestClient pc02, TestClient pc03, TestClient pc04)
    {
        Console.WriteLine("[10] 緊急停止・復元・リセット");

        // PAIR-001 を再開させる
        pc01.SendReady();
        pc02.SendReady();
        Check(pc01.WaitForState(() => pc01.GameState == GameState.Drawing, 12000),
            "PAIR-001 が再びラウンド中");

        // 緊急停止
        core.Emergency.EmergencyStop();
        Check(core.Emergency.IsStopped, "サーバーが緊急停止状態になる");
        Check(pc01.WaitForState(() => pc01.EmergencyStopped, 3000) &&
              pc02.WaitForState(() => pc02.EmergencyStopped, 3000),
            "両クライアントへ緊急停止が通知された");
        Check(core.Sessions.GetByPair("PAIR-001")!.State == GameState.EmergencyStopped,
            "セッションが緊急停止状態になる");
        Check(pc03.EmergencyStopped || pc03.GameState == GameState.EmergencyStopped,
            "別ペアのクライアントにも緊急停止が通知された");
        Check(core.Log.Snapshot().Any(e => e.EventKind == "緊急停止"),
            "ログに緊急停止が記録された");

        // システムを復元
        core.Emergency.Restore();
        Check(!core.Emergency.IsStopped, "緊急停止が解除された");
        Check(pc01.WaitForState(() => !pc01.EmergencyStopped && pc01.GameState == GameState.WaitingForReady, 3000) &&
              pc02.WaitForState(() => !pc02.EmergencyStopped && pc02.GameState == GameState.WaitingForReady, 3000),
            "復元後は準備OK待ちへ戻る");
        Check(pc01.PairId == "PAIR-001" && pc02.PairId == "PAIR-001",
            "復元後もペアリングは維持される");
        Check(core.Clients.All().Count == 4, "復元後も接続デバイスは維持される");

        // システムをリセット
        core.Emergency.Reset();
        Check(pc01.GotSystemReset && pc02.GotSystemReset && pc03.GotSystemReset && pc04.GotSystemReset,
            "全クライアントへシステムリセット（再起動要求）が通知された");
        Check(core.Pairs.All().Count == 0, "ペアリング情報がリセットされた");
        Check(core.Sessions.All().Count == 0, "ゲームセッションがリセットされた");
        Check(core.Log.Snapshot().Any(e => e.EventKind == "システムリセット"),
            "ログにシステムリセットが記録された");
    }
}