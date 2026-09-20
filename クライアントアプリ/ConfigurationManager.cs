using System.Text.Json;

namespace DrawingGame.Client;

/// <summary>クライアント設定</summary>
public class ClientConfig
{
    public string ServerIp { get; set; } = "";
    public string PcNumber { get; set; } = "";
}

/// <summary>
/// クライアント設定の読み書き（初回起動画面・リセット時に使用）。
/// 設定ファイルは実行フォルダーに保存される。
/// </summary>
public class ConfigurationManager
{
    private readonly string _filePath;

    public ConfigurationManager()
    {
        _filePath = Path.Combine(AppContext.BaseDirectory, "client_config.json");
    }

    public ClientConfig Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var cfg = JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(_filePath));
                if (cfg != null) return cfg;
            }
        }
        catch { }
        return new ClientConfig();
    }

    public void Save(ClientConfig config)
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void Delete()
    {
        try { if (File.Exists(_filePath)) File.Delete(_filePath); } catch { }
    }
}
