using System.Text.Json;
using DrawingGame.Shared;

namespace DrawingGame.Server;

/// <summary>
/// お題データの管理。
/// questions.json（実行フォルダー）から読み込み、後から追加・変更できる。
/// ファイルが無ければ既定のお題一覧を作成する。
/// </summary>
public class QuestionManager
{
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly LogManager _log;
    private List<QuestionEntry> _questions = new();

    public string FilePath => _filePath;

    public QuestionManager(LogManager log, string dataDir)
    {
        _log = log;
        _filePath = Path.Combine(dataDir, "questions.json");
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var file = JsonSerializer.Deserialize<QuestionsFile>(json, NetUtil.JsonOpts);
                    _questions = file?.Questions?.Where(q => !string.IsNullOrWhiteSpace(q.Text)).ToList() ?? new List<QuestionEntry>();
                }
                else
                {
                    _questions = CreateDefaultQuestions();
                    SaveLocked();
                }

                // 正解リストの補完（Answersが空ならTextを使う）
                foreach (var q in _questions)
                {
                    if (q.Answers.Count == 0) q.Answers.Add(q.Text);
                }

                _log.Info("お題データ", "", "", $"お題データを読み込みました（{_questions.Count}件 / questions.json）");
            }
            catch (Exception ex)
            {
                _questions = new List<QuestionEntry>();
                _log.Error("お題データ", "", "", $"お題データの読み込みに失敗しました: {ex.Message}");
            }
        }
    }

    public void Reload()
    {
        lock (_lock) _questions.Clear();
        Load();
    }

    /// <summary>ランダムにお題を1件選ぶ（直前のものは除外）</summary>
    public QuestionEntry? PickRandom(string? excludeText)
    {
        lock (_lock)
        {
            var pool = _questions
                .Where(q => !string.Equals(q.Text, excludeText, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (pool.Count == 0) pool = _questions;
            if (pool.Count == 0) return null;
            return pool[Random.Shared.Next(pool.Count)];
        }
    }

    public int Count { get { lock (_lock) return _questions.Count; } }

    /// <summary>お題一覧のスナップショットを返す</summary>
    public List<QuestionEntry> GetAll()
    {
        lock (_lock)
        {
            return _questions
                .Select(q => new QuestionEntry { Text = q.Text, Answers = new List<string>(q.Answers) })
                .ToList();
        }
    }

    /// <summary>お題を追加して保存する</summary>
    public bool Add(string text, IEnumerable<string> answers, out string? error)
    {
        var trimmedText = text?.Trim() ?? "";
        var answerList = (answers ?? Array.Empty<string>())
            .Select(a => a?.Trim() ?? "")
            .Where(a => a.Length > 0)
            .Distinct()
            .ToList();

        lock (_lock)
        {
            if (trimmedText.Length == 0)
            {
                error = "お題を入力してください。";
                return false;
            }
            if (_questions.Any(q => string.Equals(q.Text, trimmedText, StringComparison.OrdinalIgnoreCase)))
            {
                error = "同じお題が既に存在します。";
                return false;
            }
            if (answerList.Count == 0) answerList.Add(trimmedText);

            _questions.Add(new QuestionEntry { Text = trimmedText, Answers = answerList });
            SaveLocked();
            _log.Info("お題データ", "", "", $"お題を追加しました: {trimmedText}（正解表記 {answerList.Count}種）");
            error = null;
            return true;
        }
    }

    /// <summary>お題を削除して保存する</summary>
    public bool Remove(string text, out string? error)
    {
        lock (_lock)
        {
            var target = _questions.FirstOrDefault(q => string.Equals(q.Text, text?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                error = "お題が見つかりません。";
                return false;
            }

            _questions.Remove(target);
            SaveLocked();
            _log.Info("お題データ", "", "", $"お題を削除しました: {target.Text}");
            error = null;
            return true;
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(
                new QuestionsFile { Questions = _questions }, NetUtil.JsonOpts));
        }
        catch (Exception ex)
        {
            _log.Error("お題データ", "", "", $"お題データの保存に失敗しました: {ex.Message}");
        }
    }

    private static List<QuestionEntry> CreateDefaultQuestions()
    {
        static QuestionEntry Q(string text, params string[] answers) => new() { Text = text, Answers = answers.ToList() };

        return new List<QuestionEntry>
        {
            Q("りんご", "りんご", "リンゴ", "林檎"),
            Q("バナナ", "ばなな", "バナナ"),
            Q("いぬ", "いぬ", "イヌ", "犬"),
            Q("ねこ", "ねこ", "ネコ", "猫"),
            Q("うさぎ", "うさぎ", "ウサギ", "兎"),
            Q("とり", "とり", "トリ", "鳥"),
            Q("さかな", "さかな", "サカナ", "魚"),
            Q("くま", "くま", "クマ", "熊"),
            Q("はな", "はな", "ハナ", "花"),
            Q("き", "き", "キ", "木"),
            Q("やま", "やま", "ヤマ", "山"),
            Q("うみ", "うみ", "ウミ", "海"),
            Q("たいよう", "たいよう", "タイヨウ", "太陽"),
            Q("つき", "つき", "ツキ", "月"),
            Q("ほし", "ほし", "ホシ", "星"),
            Q("そら", "そら", "ソラ", "空"),
            Q("あめ", "あめ", "アメ", "雨"),
            Q("かさ", "かさ", "カサ", "傘"),
            Q("くつ", "くつ", "クツ", "靴"),
            Q("ぼうし", "ぼうし", "ボウシ", "帽子"),
            Q("とけい", "とけい", "トケイ", "時計"),
            Q("でんしゃ", "でんしゃ", "デンシャ", "電車"),
            Q("じてんしゃ", "じてんしゃ", "ジテンシャ", "自転車"),
            Q("くるま", "くるま", "クルマ", "車"),
            Q("ひこうき", "ひこうき", "ヒコウキ", "飛行機"),
            Q("ふね", "ふね", "フネ", "船"),
            Q("いえ", "いえ", "イエ", "家"),
            Q("まど", "まど", "マド", "窓"),
            Q("ほん", "ほん", "ホン", "本"),
            Q("えんぴつ", "えんぴつ", "エンピツ", "鉛筆"),
            Q("かばん", "かばん", "カバン", "鞄"),
            Q("いちご", "いちご", "イチゴ", "苺"),
            Q("すいか", "すいか", "スイカ", "西瓜"),
            Q("おにぎり", "おにぎり", "オニギリ", "おむすび", "オムスビ"),
            Q("だんごむし", "だんごむし", "ダンゴムシ"),
            Q("ちょうちょ", "ちょうちょ", "チョウチョ", "ちょうちょう", "蝶々"),
            Q("ぺんぎん", "ぺんぎん", "ペンギン"),
            Q("きりん", "きりん", "キリン"),
            Q("ぞう", "ぞう", "ゾウ", "象"),
            Q("ふうせん", "ふうせん", "フウセン", "風船"),
            Q("アイスクリーム", "あいすくりーむ", "アイスクリーム"),
            Q("ハンバーガー", "はんばーがー", "ハンバーガー"),
            Q("てれび", "てれび", "テレビ"),
            Q("でんわ", "でんわ", "デンワ", "電話"),
            Q("はさみ", "はさみ", "ハサミ", "鋏"),
        };
    }
}
