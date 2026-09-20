using System.Text;

namespace DrawingGame.Shared;

/// <summary>お題1件</summary>
public class QuestionEntry
{
    /// <summary>お題の表示名（例: りんご）</summary>
    public string Text { get; set; } = "";

    /// <summary>正解として許容する表記のリスト（例: りんご / リンゴ / 林檎）</summary>
    public List<string> Answers { get; set; } = new();

    /// <summary>主正解（判定表示用）</summary>
    public string PrimaryAnswer => Answers.Count > 0 ? Answers[0] : Text;
}

/// <summary>お題データファイル（questions.json）のルート</summary>
public class QuestionsFile
{
    public List<QuestionEntry> Questions { get; set; } = new();
}

/// <summary>回答の正規化・判定ヘルパー</summary>
public static class AnswerNormalizer
{
    /// <summary>
    /// 回答を比較可能な形へ正規化する。
    /// ・前後の空白除去・内部空白除去
    /// ・カタカナ→ひらがな
    /// ・英字の小文字化
    /// </summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Trim());
        for (int i = 0; i < sb.Length; i++)
        {
            char c = sb[i];
            if (c >= 'ァ' && c <= 'ヶ') sb[i] = (char)(c - 0x60);
            else if (c >= 'A' && c <= 'Z') sb[i] = (char)(c + 32);
            else if (c == ' ') { sb.Remove(i, 1); i--; }
        }
        return sb.ToString();
    }
}
