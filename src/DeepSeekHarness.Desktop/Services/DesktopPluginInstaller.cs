using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace DeepSeekHarness.Services;

/// <summary>
/// 把随应用内嵌的"桌面工具"设置页插件释放到 %APPDATA%,并确保它已安装进 web profile。
/// 插件以 link 方式安装,内容更新后重启服务即可生效,无需重新安装。
/// </summary>
public static class DesktopPluginInstaller
{
    public const string PackageName = "dsh-desktop-tools";

    private static readonly (string Resource, string FileName)[] Files =
    {
        ("DeepSeekHarness.Desktop.desktop-plugin.package.json", "package.json"),
        ("DeepSeekHarness.Desktop.desktop-plugin.index.js", "index.js"),
        ("DeepSeekHarness.Desktop.desktop-plugin.client.js", "client.js"),
        ("DeepSeekHarness.Desktop.desktop-plugin.cordis.patch.yml", "cordis.patch.yml"),
    };

    /// <summary>插件在磁盘上的稳定位置(link 目标)。</summary>
    public static string PluginDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeepSeekHarnessDesktop", "desktop-plugin");

    /// <summary>释放(或更新)内嵌插件文件。</summary>
    public static string Materialize()
    {
        Directory.CreateDirectory(PluginDirectory);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var (resource, fileName) in Files)
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"缺少内嵌资源 {resource}");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var content = reader.ReadToEnd();
            var target = Path.Combine(PluginDirectory, fileName);
            if (File.Exists(target) && File.ReadAllText(target) == content) continue;
            File.WriteAllText(target, content, new UTF8Encoding(false));
        }
        return PluginDirectory;
    }

    /// <summary>确保 web profile 已安装并启用桌面工具插件。</summary>
    public static async Task<(bool Ok, string Message)> EnsureInstalledAsync(AppHost host, CancellationToken ct = default)
    {
        if (host.Paths is null || host.Plugins is null)
        {
            return (false, "运行时未就绪");
        }

        var directory = Materialize();
        var expectedSpec = "link:" + directory.Replace('\\', '/');

        var manifestPath = Path.Combine(host.Paths.WebProfileDir, "package.json");
        string? currentSpec = null;
        var bundleEnabled = false;
        try
        {
            if (File.Exists(manifestPath))
            {
                var manifest = JsonNode.Parse(File.ReadAllText(manifestPath));
                currentSpec = manifest?["dependencies"]?[PackageName]?.GetValue<string>();
                var bundles = manifest?["dsh"]?["profile"]?["bundles"]?.AsArray();
                bundleEnabled = bundles is not null && bundles.Any(n =>
                    string.Equals(n?.GetValue<string>(), PackageName, StringComparison.Ordinal));
            }
        }
        catch
        {
            // 清单不可读时走安装流程。
        }

        if (string.Equals(currentSpec, expectedSpec, StringComparison.OrdinalIgnoreCase) && bundleEnabled)
        {
            return (true, "桌面工具插件已就绪");
        }

        var result = await host.Plugins.InstallAsync(directory, host.Paths.NodePath, host.Paths.DshBinPath, ct)
            .ConfigureAwait(false);
        return result.Success
            ? (true, "桌面工具插件已安装,重启服务后生效")
            : (false, "桌面工具插件安装失败: " + Summarize(result.Output));
    }

    private static string Summarize(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "未知错误";
        var lines = output.Trim().Split('\n');
        var tail = lines.Length <= 4 ? output.Trim() : string.Join("\n", lines[^4..]).Trim();
        return tail.Length <= 500 ? tail : tail[^500..];
    }
}
