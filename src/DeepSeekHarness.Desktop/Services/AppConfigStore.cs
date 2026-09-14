using System.IO;
using System.Text.Json;
using DeepSeekHarness.Models;

namespace DeepSeekHarness.Services;

/// <summary>读写应用级配置 JSON。</summary>
public sealed class AppConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string DirectoryPath { get; }

    public string FilePath => Path.Combine(DirectoryPath, "appsettings.json");

    public AppConfigStore()
    {
        DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DeepSeekHarnessDesktop");
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            }
        }
        catch
        {
            // 配置损坏时回退默认值,不阻塞启动。
        }
        return new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(DirectoryPath);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
