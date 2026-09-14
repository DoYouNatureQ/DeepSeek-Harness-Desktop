using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace DeepSeekHarness.Services;

/// <summary>无界面自检:验证运行时、配置读写、插件清单与服务端到端链路。</summary>
public static class SelfTest
{
    public static async Task<int> RunAsync(AppHost host, string outputPath, bool withServer, bool writeTest = false)
    {
        var report = new StringBuilder();
        var failures = 0;

        void Check(string name, Func<string> probe)
        {
            try
            {
                var detail = probe();
                report.AppendLine($"[PASS] {name}: {detail}");
            }
            catch (Exception ex)
            {
                failures++;
                report.AppendLine($"[FAIL] {name}: {ex.Message}");
            }
        }

        async Task RunWriteTestsAsync(string label)
        {
            report.AppendLine(new string('-', 72));
            report.AppendLine($"写入链路测试[{label}](结束后自动清理)");
            var apiMode = host.Server.State == DshServerState.Running && host.Api.IsAuthenticated;
            report.AppendLine($"模式: {(apiMode ? "RPC(服务在线)" : "本地文件")}");

            var placeholder = "sk-dshdesktop-selftest-placeholder";
            var existing = host.Settings!.GetCredential(Models.ModelCatalog.ApiKeyRef);
            if (existing == placeholder)
            {
                host.Settings.RemoveCredential(Models.ModelCatalog.ApiKeyRef);
                report.AppendLine("[CLEAN] 发现上次遗留的测试密钥,已清理。");
                existing = null;
            }

            if (existing is not null)
            {
                report.AppendLine("[SKIP] API Key 写入: 已存在真实密钥,跳过以免覆盖。");
            }
            else
            {
                try
                {
                    var (ok, message) = await host.SetApiKeyAsync(placeholder);
                    Check("API Key 写入", () => ok ? message : throw new InvalidOperationException(message));
                    Check("API Key 校验", () => host.HasApiKeyAsync().GetAwaiter().GetResult()
                        ? "已配置"
                        : throw new InvalidOperationException("写入后仍未配置"));
                }
                finally
                {
                    try
                    {
                        if (apiMode)
                        {
                            await host.Api.CallAsync("credentials/unset", new Dictionary<string, object?>
                            {
                                ["ref"] = Models.ModelCatalog.ApiKeyRef,
                            });
                        }
                    }
                    catch
                    {
                        // 忽略,继续文件清理。
                    }
                    host.Settings!.RemoveCredential(Models.ModelCatalog.ApiKeyRef);
                    report.AppendLine($"[CLEAN] 测试密钥已清理(当前配置: {(host.Settings.HasCredential(Models.ModelCatalog.ApiKeyRef) ? "仍存在" : "无")})");
                }
            }

            var originalModel = host.GetDefaultModel();
            try
            {
                var (ok, message) = await host.SetDefaultModelAsync("deepseek-v4-pro");
                Check("默认模型写入", () => ok ? message : throw new InvalidOperationException(message));
                Check("默认模型校验", () =>
                {
                    var current = host.GetDefaultModel();
                    if (current != "deepseek-v4-pro") throw new InvalidOperationException($"实际为 {current}");
                    return current;
                });
            }
            finally
            {
                await host.SetDefaultModelAsync(originalModel);
                report.AppendLine($"[CLEAN] 默认模型已恢复: {host.GetDefaultModel()}");
            }

            try
            {
                var (ok, message) = await host.SetBaseUrlAsync("https://api.deepseek.com");
                Check("API 地址写入", () => ok ? message : throw new InvalidOperationException(message));
                Check("API 地址校验", () => host.GetBaseUrl() ?? throw new InvalidOperationException("未读到 baseURL"));
            }
            finally
            {
                await host.SetBaseUrlAsync(null);
                report.AppendLine($"[CLEAN] API 地址已恢复默认(当前: {host.GetBaseUrl() ?? "默认"})");
            }
        }

        report.AppendLine($"DeepSeek Harness Desktop 自检 · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(new string('-', 72));

        Check("运行时路径解析", () =>
        {
            if (host.Paths is null) throw new InvalidOperationException(host.PathError ?? "路径未解析");
            return host.Paths.RuntimeDir;
        });

        Check("Node.js", () =>
        {
            if (host.Paths is null) throw new InvalidOperationException("路径未解析");
            var psi = new ProcessStartInfo(host.Paths.NodePath, "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi)!;
            var version = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            if (process.ExitCode != 0) throw new InvalidOperationException($"node 退出码 {process.ExitCode}");
            return $"{host.Paths.NodePath} ({version})";
        });

        Check("DSH 数据目录", () =>
        {
            if (host.Paths is null) throw new InvalidOperationException("路径未解析");
            return host.Paths.DshHome;
        });

        Check("settings.yaml 读取", () => $"{host.Settings!.ReadSettings().Count} 个命名空间");

        Check("API Key 状态", () => host.Settings!.HasCredential(Models.ModelCatalog.ApiKeyRef) ? "已配置" : "未配置");

        Check("默认模型", () => host.GetDefaultModel());

        Check("插件清单", () =>
        {
            var plugins = host.Plugins!.ListInstalled();
            return $"{plugins.Count} 个包,启用 {plugins.Count(p => p.Enabled)} 个";
        });

        if (withServer)
        {
            var started = await host.StartServerAsync();
            Check("服务启动", () => started
                ? host.Server.BaseUrl ?? "?"
                : throw new InvalidOperationException(host.Server.LastError ?? "启动失败"));

            if (started)
            {
                Check("RPC settings/describe", () =>
                {
                    var value = host.Api.CallAsync("settings/describe").GetAwaiter().GetResult();
                    return $"{value?["namespaces"]?.AsArray().Count ?? 0} 个命名空间";
                });

                Check("RPC credentials/describe", () =>
                {
                    var value = host.Api.CallAsync("credentials/describe", new Dictionary<string, object?>
                    {
                        ["refs"] = new[] { Models.ModelCatalog.ApiKeyRef },
                    }).GetAwaiter().GetResult();
                    var configured = value?[Models.ModelCatalog.ApiKeyRef]?["configured"]?.GetValue<bool>() ?? false;
                    return configured ? "服务端确认已配置" : "服务端确认未配置";
                });

                Check("RPC pluginInventory/list", () =>
                {
                    var value = host.Api.CallAsync("pluginInventory/list").GetAwaiter().GetResult();
                    return $"{(value?["entries"] as JsonArray)?.Count ?? 0} 个 Loader 条目";
                });

                Check("RPC llm/listConfigurableProviders", () =>
                {
                    var value = host.Api.CallAsync("llm/listConfigurableProviders").GetAwaiter().GetResult();
                    return $"{value?.AsArray().Count ?? 0} 个提供方";
                });

                if (writeTest) await RunWriteTestsAsync("RPC");
            }

            host.StopServer();
        }

        if (writeTest) await RunWriteTestsAsync("本地文件");

        report.AppendLine(new string('-', 72));
        report.AppendLine(failures == 0 ? "结果: 全部通过" : $"结果: {failures} 项失败");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, report.ToString(), new UTF8Encoding(false));
        return failures == 0 ? 0 : 1;
    }
}
