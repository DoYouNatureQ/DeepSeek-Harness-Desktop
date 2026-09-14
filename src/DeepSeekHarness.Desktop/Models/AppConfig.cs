using System.Text.Json.Serialization;

namespace DeepSeekHarness.Models;

/// <summary>应用级配置,保存在 %APPDATA%\DeepSeekHarnessDesktop\appsettings.json。</summary>
public sealed class AppConfig
{
    /// <summary>DSH_HOME 覆盖;为空时使用 ~/.dsh。</summary>
    [JsonPropertyName("dshHome")]
    public string? DshHome { get; set; }

    /// <summary>Web 服务端口;0 表示由系统分配。</summary>
    [JsonPropertyName("port")]
    public int Port { get; set; } = 3080;

    /// <summary>node.exe 路径;为空时自动探测。</summary>
    [JsonPropertyName("nodePath")]
    public string? NodePath { get; set; }

    /// <summary>dsh 运行时目录(包含 node_modules\@deepseek-ai\dsh);为空时自动探测。</summary>
    [JsonPropertyName("runtimeDir")]
    public string? RuntimeDir { get; set; }

    /// <summary>启动应用时自动启动服务。</summary>
    [JsonPropertyName("autoStartServer")]
    public bool AutoStartServer { get; set; } = true;

    /// <summary>禁用遥测上报。</summary>
    [JsonPropertyName("telemetryDisabled")]
    public bool TelemetryDisabled { get; set; } = true;

    /// <summary>关闭窗口时最小化到托盘而不是退出。</summary>
    [JsonPropertyName("closeToTray")]
    public bool CloseToTray { get; set; }
}
