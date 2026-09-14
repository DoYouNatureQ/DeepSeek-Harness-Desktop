using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using DeepSeekHarness.Models;

namespace DeepSeekHarness.Services;

/// <summary>应用级服务容器:配置、路径、服务进程、API 客户端与插件管理器。</summary>
public sealed class AppHost : IDisposable
{
    public const int MaxLogLines = 3000;

    public AppConfigStore ConfigStore { get; }
    public AppConfig Config { get; private set; }
    public DshPaths? Paths { get; private set; }
    public string? PathError { get; private set; }
    public DshServer Server { get; } = new();
    public DshApiClient Api { get; } = new();
    public PluginManager? Plugins { get; private set; }
    public DshSettingsFile? Settings { get; private set; }
    public ObservableCollection<DshLogLine> Logs { get; } = new();

    /// <summary>全局通知(message, isError)。</summary>
    public event Action<string, bool>? Toast;
    public event Action? StatusChanged;

    public AppHost()
    {
        ConfigStore = new AppConfigStore();
        Config = ConfigStore.Load();
        Server.LogLine += line => RunOnUi(() =>
        {
            Logs.Add(line);
            while (Logs.Count > MaxLogLines) Logs.RemoveAt(0);
        });
        Server.StateChanged += () => RunOnUi(() => StatusChanged?.Invoke());
        ReloadPaths();
    }

    public bool IsReady => Paths is not null;

    public void ReloadPaths()
    {
        try
        {
            Paths = DshPaths.Resolve(Config);
            Plugins = new PluginManager(Paths.WebProfileDir, Paths.RuntimeDir);
            Settings = new DshSettingsFile(Paths.DshHome);
            PathError = null;
        }
        catch (Exception ex)
        {
            Paths = null;
            Plugins = null;
            Settings = null;
            PathError = ex.Message;
        }
        RaiseStatusChanged();
    }

    public void SaveConfig()
    {
        ConfigStore.Save(Config);
    }

    public void Notify(string message, bool isError = false)
    {
        RunOnUi(() => Toast?.Invoke(message, isError));
    }

    private void RaiseStatusChanged()
    {
        RunOnUi(() => StatusChanged?.Invoke());
    }

    public void LogApp(string message, bool isError = false)
    {
        RunOnUi(() =>
        {
            Logs.Add(new DshLogLine(message, isError, DateTime.Now));
            while (Logs.Count > MaxLogLines) Logs.RemoveAt(0);
        });
    }

    /// <summary>线程安全地获取日志快照(供页面桥接读取)。</summary>
    public IReadOnlyList<DshLogLine> SnapshotLogs()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return Logs.ToList();
        return dispatcher.CheckAccess() ? Logs.ToList() : dispatcher.Invoke(() => Logs.ToList());
    }

    /// <summary>清空日志缓冲。</summary>
    public void ClearLogs() => RunOnUi(() => Logs.Clear());

    // ===================== 服务生命周期 =====================

    public async Task<bool> StartServerAsync(CancellationToken ct = default)
    {
        if (Paths is null)
        {
            Notify(PathError ?? "运行时未就绪。", true);
            return false;
        }
        if (Server.State == DshServerState.Running && Api.IsAuthenticated) return true;

        LogApp($"正在启动 DeepSeek Harness 服务(端口 {Config.Port})…");
        var ok = await Server.StartAsync(Config, Paths, ct).ConfigureAwait(false);
        if (!ok)
        {
            Notify(Server.LastError ?? "服务启动失败。", true);
            return false;
        }

        if (Server.BaseUrl is not null && Server.LaunchUrl is not null)
        {
            var authed = await Api.AuthenticateAsync(Server.BaseUrl, Server.LaunchUrl, ct).ConfigureAwait(false);
            if (!authed)
            {
                LogApp("已连接服务,但浏览器会话鉴权失败;部分管理功能将使用本地文件模式。", true);
            }
        }
        LogApp($"服务已就绪: {Server.BaseUrl}");
        Notify("DeepSeek Harness 服务已启动");
        RaiseStatusChanged();
        return true;
    }

    public void StopServer()
    {
        if (Server.State == DshServerState.Stopped) return;
        LogApp("正在停止服务…");
        Server.Stop();
        Api.Reset();
        Notify("服务已停止");
        RaiseStatusChanged();
    }

    public async Task RestartServerAsync(CancellationToken ct = default)
    {
        LogApp("正在重启服务…");
        Server.Stop();
        Api.Reset();
        await Task.Delay(500, ct).ConfigureAwait(false);
        await StartServerAsync(ct).ConfigureAwait(false);
    }

    // ===================== 主题 =====================

    /// <summary>首次运行时把 Harness Web UI 的默认主题设为深色,与原生外壳保持一致;用户后续在界面中的修改不会被覆盖。</summary>
    public async Task EnsureDefaultThemeAsync(string preference = "dark", CancellationToken ct = default)
    {
        if (Settings is null) return;
        if (Settings.GetString("ui-theme", "preference") is not null) return;

        if (Server.State == DshServerState.Running && Api.IsAuthenticated)
        {
            try
            {
                await Api.CallAsync("settings/update", new Dictionary<string, object?>
                {
                    ["ns"] = "ui-theme",
                    ["patch"] = new Dictionary<string, object?> { ["preference"] = preference },
                }, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                LogApp($"设置默认主题失败,回退到本地文件: {ex.Message}", true);
            }
        }
        Settings.SetValue(preference, "ui-theme", "preference");
    }

    // ===================== 凭据 =====================

    /// <summary>写入 API Key。优先走 RPC(热重载、冲突保护),服务未运行时直接写文件。</summary>
    public async Task<(bool Ok, string Message)> SetApiKeyAsync(string value, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (false, "API Key 不能为空。");
        }
        value = value.Trim();

        if (Server.State == DshServerState.Running && Api.IsAuthenticated)
        {
            try
            {
                await Api.CallAsync("credentials/set", new Dictionary<string, object?>
                {
                    ["ref"] = ModelCatalog.ApiKeyRef,
                    ["value"] = value,
                }, ct).ConfigureAwait(false);
                return (true, "API Key 已保存并即时生效。");
            }
            catch (Exception ex)
            {
                LogApp($"通过 RPC 保存 API Key 失败,回退到本地文件: {ex.Message}", true);
            }
        }

        if (Settings is null) return (false, "数据目录未就绪。");
        Settings.SetCredential(ModelCatalog.ApiKeyRef, value);
        return (true, "API Key 已写入本地凭据文件,服务启动后生效。");
    }

    public async Task<bool> HasApiKeyAsync(CancellationToken ct = default)
    {
        if (Server.State == DshServerState.Running && Api.IsAuthenticated)
        {
            try
            {
                var value = await Api.CallAsync("credentials/describe", new Dictionary<string, object?>
                {
                    ["refs"] = new[] { ModelCatalog.ApiKeyRef },
                }, ct).ConfigureAwait(false);
                var configured = value?[ModelCatalog.ApiKeyRef]?["configured"]?.GetValue<bool>();
                if (configured is not null) return configured.Value;
            }
            catch
            {
                // 回退文件检查。
            }
        }
        return Settings?.HasCredential(ModelCatalog.ApiKeyRef) ?? false;
    }

    // ===================== 模型配置 =====================

    public string GetDefaultModel()
    {
        if (Settings is null) return ModelCatalog.DefaultModelId;
        return Settings.GetString("agent-default-model", "model") ?? ModelCatalog.DefaultModelId;
    }

    public async Task<(bool Ok, string Message)> SetDefaultModelAsync(string modelId, CancellationToken ct = default)
    {
        if (Server.State == DshServerState.Running && Api.IsAuthenticated)
        {
            try
            {
                await Api.CallAsync("settings/update", new Dictionary<string, object?>
                {
                    ["ns"] = "agent-default-model",
                    ["patch"] = new Dictionary<string, object?>
                    {
                        ["provider"] = ModelCatalog.Provider,
                        ["model"] = modelId,
                    },
                }, ct).ConfigureAwait(false);
                return (true, $"默认模型已切换为 {modelId}。");
            }
            catch (Exception ex)
            {
                LogApp($"通过 RPC 保存默认模型失败,回退到本地文件: {ex.Message}", true);
            }
        }

        if (Settings is null) return (false, "数据目录未就绪。");
        Settings.SetValue(modelId, "agent-default-model", "model");
        Settings.SetValue(ModelCatalog.Provider, "agent-default-model", "provider");
        return (true, $"默认模型已写入配置: {modelId}。");
    }

    public string? GetBaseUrl()
    {
        return Settings?.GetString("llm-deepseek", "baseURL");
    }

    public async Task<(bool Ok, string Message)> SetBaseUrlAsync(string? url, CancellationToken ct = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(url) ? null : url.Trim().TrimEnd('/');

        if (Server.State == DshServerState.Running && Api.IsAuthenticated)
        {
            try
            {
                var ops = new List<object>();
                ops.Add(trimmed is null
                    ? new Dictionary<string, object?> { ["op"] = "unset", ["path"] = new[] { "baseURL" } }
                    : new Dictionary<string, object?> { ["op"] = "set", ["path"] = new[] { "baseURL" }, ["value"] = trimmed });
                await Api.CallAsync("settings/mutate", new Dictionary<string, object?>
                {
                    ["ns"] = "llm-deepseek",
                    ["ops"] = ops,
                }, ct).ConfigureAwait(false);
                return (true, trimmed is null ? "已恢复默认 API 地址。" : $"API 地址已更新: {trimmed}");
            }
            catch (Exception ex)
            {
                LogApp($"通过 RPC 保存 API 地址失败,回退到本地文件: {ex.Message}", true);
            }
        }

        if (Settings is null) return (false, "数据目录未就绪。");
        if (trimmed is null)
        {
            var settings = Settings.ReadSettings();
            if (settings.TryGetValue("llm-deepseek", out var section) && section is Dictionary<string, object?> map)
            {
                map.Remove("baseURL");
                if (map.Count == 0) settings.Remove("llm-deepseek");
                Settings.WriteSettings(settings);
            }
        }
        else
        {
            Settings.SetValue(trimmed, "llm-deepseek", "baseURL");
        }
        return (true, trimmed is null ? "已恢复默认 API 地址。" : $"API 地址已写入配置: {trimmed}");
    }

    // ===================== 插件 =====================

    public async Task<JsonNode?> GetLivePluginInventoryAsync(CancellationToken ct = default)
    {
        if (Server.State != DshServerState.Running || !Api.IsAuthenticated) return null;
        try
        {
            return await Api.CallAsync("pluginInventory/list", null, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        Server.Dispose();
        Api.Dispose();
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
