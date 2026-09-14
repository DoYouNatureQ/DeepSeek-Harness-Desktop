using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DeepSeekHarness.Models;

namespace DeepSeekHarness.Services;

public sealed record BridgeResult(bool Ok, object? Data, string? Error);

/// <summary>
/// Harness 页面内"桌面工具"设置页与原生能力之间的桥。
/// 协议:页面 postMessage <c>{dsh:'desktop', req, action, ...}</c>,原生回
/// <c>{dsh:'desktop', req, ok, data|error}</c>。
/// </summary>
public sealed class DesktopBridge
{
    private readonly AppHost _host;

    public DesktopBridge(AppHost host)
    {
        _host = host;
    }

    public async Task<BridgeResult> HandleAsync(string action, JsonElement message)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var data = await DispatchAsync(action, message).ConfigureAwait(false);
            LogIfSlow(action, sw.Elapsed);
            return new BridgeResult(true, data, null);
        }
        catch (Exception ex)
        {
            LogIfSlow(action, sw.Elapsed);
            return new BridgeResult(false, null, ex.Message);
        }
    }

    /// <summary>记录页面桥接调用耗时,便于定位「某个操作要等一会」的具体环节。</summary>
    private void LogIfSlow(string action, TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < 300) return;
        _host.LogApp($"[耗时] {action}: {elapsed.TotalMilliseconds:N0}ms");
    }

    private async Task<object?> DispatchAsync(string action, JsonElement m)
    {
        switch (action)
        {
            case "app.info":
                return new
                {
                    version = "1.0.0",
                    dshVersion = "0.1.5-rc.2",
                    dshHome = _host.Paths?.DshHome ?? string.Empty,
                    profileDir = _host.Paths?.WebProfileDir ?? string.Empty,
                    runtimeDir = _host.Paths?.RuntimeDir ?? string.Empty,
                    port = _host.Config.Port,
                    state = _host.Server.State.ToString(),
                    url = _host.Server.BaseUrl,
                    autoStart = _host.Config.AutoStartServer,
                    telemetryDisabled = _host.Config.TelemetryDisabled,
                    closeToTray = _host.Config.CloseToTray,
                };

            case "app.openDataDir":
                OpenFolder(_host.Paths?.DshHome);
                return null;
            case "app.openProfile":
                OpenFolder(_host.Paths?.WebProfileDir);
                return null;
            case "app.openRuntime":
                OpenFolder(_host.Paths?.RuntimeDir);
                return null;
            case "app.openLog":
                OpenLog();
                return null;
            case "app.openBrowser":
                if (_host.Server.BaseUrl is { } url) OpenExternal(url);
                return null;

            case "plugins.list":
                return new
                {
                    items = (_host.Plugins?.ListInstalled() ?? Array.Empty<PluginInfo>()).Select(p => new
                    {
                        name = p.Name,
                        version = p.Version,
                        description = p.Description,
                        enabled = p.Enabled,
                        builtIn = p.BuiltIn,
                    }).ToList(),
                };

            case "plugins.install":
            {
                var spec = GetString(m, "spec");
                if (string.IsNullOrWhiteSpace(spec)) throw new InvalidOperationException("插件包名不能为空");
                var result = await RunCliAsync(() => _host.Plugins!.InstallAsync(spec, _host.Paths!.NodePath, _host.Paths!.DshBinPath)).ConfigureAwait(false);
                return new { ok = result.Success, output = result.Output, needsRestart = result.Success };
            }

            case "plugins.remove":
            {
                var name = GetString(m, "name");
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("插件名不能为空");
                var result = await RunCliAsync(() => _host.Plugins!.RemoveAsync(name, _host.Paths!.NodePath, _host.Paths!.DshBinPath)).ConfigureAwait(false);
                return new { ok = result.Success, output = result.Output, needsRestart = result.Success };
            }

            case "plugins.setEnabled":
            {
                var name = GetString(m, "name");
                var enabled = GetBool(m, "enabled");
                _host.Plugins!.SetEnabled(name, enabled);
                return new { ok = true, needsRestart = true };
            }

            case "plugins.reset":
                _host.Plugins!.DisableAll();
                return new { ok = true, needsRestart = true };

            case "service.start":
                await _host.StartServerAsync().ConfigureAwait(false);
                return new { ok = _host.Server.State == DshServerState.Running, state = _host.Server.State.ToString() };
            case "service.stop":
                _host.StopServer();
                return new { ok = true, state = _host.Server.State.ToString() };
            case "service.restart":
                await _host.RestartServerAsync().ConfigureAwait(false);
                return new { ok = _host.Server.State == DshServerState.Running, state = _host.Server.State.ToString() };
            case "service.setPort":
            {
                var port = GetInt(m, "port");
                if (port < 0 || port > 65535) throw new InvalidOperationException("端口必须在 0 - 65535 之间");
                _host.Config.Port = port;
                _host.SaveConfig();
                await _host.RestartServerAsync().ConfigureAwait(false);
                return new { ok = true, port };
            }
            case "service.setBehavior":
            {
                if (m.TryGetProperty("autoStart", out var autoStart)) _host.Config.AutoStartServer = autoStart.GetBoolean();
                if (m.TryGetProperty("telemetryDisabled", out var telemetry)) _host.Config.TelemetryDisabled = telemetry.GetBoolean();
                if (m.TryGetProperty("closeToTray", out var tray)) _host.Config.CloseToTray = tray.GetBoolean();
                _host.SaveConfig();
                return new { ok = true };
            }

            case "logs.tail":
            {
                var lines = Math.Clamp(GetInt(m, "lines", 400), 1, 2000);
                var snapshot = _host.SnapshotLogs();
                var take = snapshot.Count > lines ? snapshot.Skip(snapshot.Count - lines) : snapshot;
                return new
                {
                    lines = take.Select(l => new
                    {
                        t = l.Time.ToString("HH:mm:ss"),
                        text = l.Text,
                        error = l.IsError,
                    }).ToList(),
                };
            }
            case "logs.clear":
                _host.ClearLogs();
                return new { ok = true };

            default:
                throw new InvalidOperationException($"未知操作 {action}");
        }
    }

    private static async Task<PluginCommandResult> RunCliAsync(Func<Task<PluginCommandResult>> run)
    {
        return await run().ConfigureAwait(false);
    }

    private static string GetString(JsonElement m, string name)
        => m.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static bool GetBool(JsonElement m, string name)
        => m.TryGetProperty(name, out var value) && value.GetBoolean();

    private static int GetInt(JsonElement m, string name, int fallback = 0)
    {
        if (!m.TryGetProperty(name, out var value)) return fallback;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : fallback;
    }

    private void OpenLog()
    {
        try
        {
            var path = Path.Combine(_host.ConfigStore.DirectoryPath, "app.log");
            if (File.Exists(path)) OpenExternal(path);
            else OpenFolder(_host.ConfigStore.DirectoryPath);
        }
        catch
        {
            // 忽略。
        }
    }

    private static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // 忽略。
        }
    }

    private static void OpenFolder(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // 忽略。
        }
    }
}
