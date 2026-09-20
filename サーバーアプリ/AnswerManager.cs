using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// 回答の受付・正解判定を担当する。
/// 判定は必ずサーバー側で行い、クライアントを信用しない。
/// </summary>
public class AnswerManager
{
    private readonly ServerCore _core;

    public AnswerManager(ServerCore core)
    {
        _core = core;
    }

    public void Process(GameSession session, string pc, string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            _core.Log.Warn("回答拒否", pc, session.PairId, "空の回答は受け付けません");
            SendError(pc, "回答を入力してください");
            return;
        }
        if (answer.Length > 50)
        {
            _core.Log.Warn("回答拒否", pc, session.PairId, "回答が長すぎます（50文字以内）");
            SendError(pc, "回答は50文字以内で入力してください");
            return;
        }

        if (!session.CanAcceptAnswer(pc, out var error))
        {
            _core.Log.Warn("回答拒否", pc, session.PairId, error ?? "回答を拒否しました");
            SendError(pc, error ?? "回答できません");
            return;
        }

        var normalized = AnswerNormalizer.Normalize(answer);
        if (normalized.Length == 0)
        {
            _core.Log.Warn("回答拒否", pc, session.PairId, "正規化後の回答が空です");
            SendError(pc, "回答を入力してください");
            return;
        }

        var acceptable = new List<string>();
        session.WithLock(() =>
        {
            if (session.AcceptableAnswers.Count > 0)
                acceptable.AddRange(session.AcceptableAnswers.Select(AnswerNormalizer.Normalize));
            else if (session.Question != null)
                acceptable.Add(AnswerNormalizer.Normalize(session.Question));
        });

        bool isCorrect = acceptable.Contains(normalized);

        _core.Log.Info("回答受信", pc, session.PairId, $"{pc} から回答を受信しました（\"{answer}\"）");
        session.RecordAnswer(pc, isCorrect, _core.Sessions);
        _core.Sessions.NotifyAnswerResult(session, pc, answer, isCorrect);
    }

    private void SendError(string pc, string message)
    {
        _core.Sessions.SendToPc(pc, new NetMessage
        {
            Type = MessageType.Error,
            Message = message,
        });
    }
}
