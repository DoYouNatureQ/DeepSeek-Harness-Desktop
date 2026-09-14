using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeepSeekHarness.Services;

public sealed record PluginInfo(
    string Name,
    string Version,
    string? Description,
    bool Enabled,
    bool BuiltIn);

public sealed record PluginCommandResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

/// <summary>
/// 基于 profile 的插件管理:读取 <c>profiles/web/package.json</c> 的依赖与 bundle 列表,
/// 安装/移除通过官方 CLI `dsh plugin --profile web ...`(转发给 pnpm)执行。
/// </summary>
public sealed class PluginManager
{
    private static readonly string[] BuiltInBundles =
    {
        "@deepseek-ai/dsh-base",
        "@deepseek-ai/dsh-web-app",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ProfileDir { get; }
    public string? InstallationDir { get; }

    public PluginManager(string profileDir, string? installationDir = null)
    {
        ProfileDir = profileDir;
        InstallationDir = installationDir;
    }

    public string ManifestPath => Path.Combine(ProfileDir, "package.json");

    // ===================== 读取 =====================

    public IReadOnlyList<PluginInfo> ListInstalled()
    {
        var result = new List<PluginInfo>();
        var manifest = ReadManifest();
        if (manifest is null) return result;

        var dependencies = (manifest["dependencies"] as JsonObject) ?? new JsonObject();
        var bundles = GetBundles(manifest).ToHashSet(StringComparer.Ordinal);

        foreach (var (name, _) in dependencies)
        {
            var installed = ReadInstalledPackage(name);
            var builtIn = BuiltInBundles.Contains(name, StringComparer.Ordinal);
            result.Add(new PluginInfo(
                name,
                installed?.Version ?? "unknown",
                installed?.Description,
                bundles.Contains(name),
                builtIn));
        }

        foreach (var bundle in bundles)
        {
            if (result.Any(p => string.Equals(p.Name, bundle, StringComparison.Ordinal))) continue;
            var installed = ReadInstalledPackage(bundle);
            result.Add(new PluginInfo(
                bundle,
                installed?.Version ?? "内置",
                installed?.Description,
                true,
                BuiltInBundles.Contains(bundle, StringComparer.Ordinal)));
        }

        return result.OrderByDescending(p => p.BuiltIn).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<string> GetBundles()
    {
        var manifest = ReadManifest();
        return manifest is null ? Array.Empty<string>() : GetBundles(manifest);
    }

    // ===================== 启用 / 禁用 =====================

    public void SetEnabled(string name, bool enabled)
    {
        var manifest = ReadManifest() ?? throw new InvalidOperationException("profile 清单不存在。");
        var bundles = GetBundles(manifest).ToList();
        if (enabled)
        {
            if (!bundles.Contains(name, StringComparer.Ordinal)) bundles.Add(name);
        }
        else
        {
            bundles.RemoveAll(b => string.Equals(b, name, StringComparison.Ordinal));
        }
        SetBundles(manifest, bundles);
        WriteManifest(manifest);
    }

    public void DisableAll()
    {
        var manifest = ReadManifest() ?? throw new InvalidOperationException("profile 清单不存在。");
        SetBundles(manifest, BuiltInBundles);
        WriteManifest(manifest);
    }

    // ===================== 安装 / 移除 =====================

    public async Task<PluginCommandResult> InstallAsync(string spec, string nodePath, string dshBinPath, CancellationToken ct = default)
        => await RunCliAsync(nodePath, dshBinPath, new[] { "add", spec }, ct).ConfigureAwait(false);

    public async Task<PluginCommandResult> RemoveAsync(string name, string nodePath, string dshBinPath, CancellationToken ct = default)
        => await RunCliAsync(nodePath, dshBinPath, new[] { "remove", name }, ct).ConfigureAwait(false);

    public async Task<PluginCommandResult> UpdateAsync(string name, string nodePath, string dshBinPath, CancellationToken ct = default)
        => await RunCliAsync(nodePath, dshBinPath, new[] { "update", name }, ct).ConfigureAwait(false);

    private async Task<PluginCommandResult> RunCliAsync(
        string nodePath,
        string dshBinPath,
        IReadOnlyList<string> pnpmArgs,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = nodePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        psi.ArgumentList.Add(dshBinPath);
        psi.ArgumentList.Add("plugin");
        psi.ArgumentList.Add("--profile");
        psi.ArgumentList.Add("web");
        foreach (var arg in pnpmArgs) psi.ArgumentList.Add(arg);

        var pathParts = new List<string> { Path.GetDirectoryName(nodePath)! };
        var npmBin = DshPaths.FindNpmGlobalBin();
        if (npmBin is not null) pathParts.Add(npmBin);
        psi.Environment["PATH"] = string.Join(Path.PathSeparator, pathParts) + Path.PathSeparator +
                                  (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new PluginCommandResult(process.ExitCode, output.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
            return new PluginCommandResult(-1, output + "\n操作已取消或超时。");
        }
        catch (Exception ex)
        {
            return new PluginCommandResult(-1, output + "\n" + ex.Message);
        }
    }

    // ===================== 内部 =====================

    private JsonNode? ReadManifest()
    {
        try
        {
            if (!File.Exists(ManifestPath)) return null;
            return JsonNode.Parse(File.ReadAllText(ManifestPath));
        }
        catch
        {
            return null;
        }
    }

    private void WriteManifest(JsonNode manifest)
    {
        Directory.CreateDirectory(ProfileDir);
        var json = manifest.ToJsonString(JsonOptions) + Environment.NewLine;
        var tmp = ManifestPath + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, ManifestPath, overwrite: true);
    }

    private static List<string> GetBundles(JsonNode manifest)
    {
        var list = manifest["dsh"]?["profile"]?["bundles"]?.AsArray();
        return list is null
            ? new List<string>()
            : list.Select(n => n?.GetValue<string>() ?? string.Empty).Where(s => s.Length > 0).ToList();
    }

    private static void SetBundles(JsonNode manifest, IReadOnlyList<string> bundles)
    {
        if (manifest["dsh"] is not JsonObject dsh)
        {
            dsh = new JsonObject();
            manifest["dsh"] = dsh;
        }
        if (dsh["profile"] is not JsonObject profile)
        {
            profile = new JsonObject();
            dsh["profile"] = profile;
        }
        var array = new JsonArray();
        foreach (var bundle in bundles) array.Add(bundle);
        profile["bundles"] = array;
    }

    private (string Version, string? Description)? ReadInstalledPackage(string name)
    {
        var roots = new List<string> { ProfileDir };
        if (InstallationDir is not null) roots.Add(InstallationDir);
        foreach (var root in roots)
        {
            try
            {
                var parts = name.Split('/', 2);
                var dir = parts.Length == 2
                    ? Path.Combine(root, "node_modules", parts[0], parts[1])
                    : Path.Combine(root, "node_modules", name);
                var manifestPath = Path.Combine(dir, "package.json");
                if (!File.Exists(manifestPath)) continue;
                var node = JsonNode.Parse(File.ReadAllText(manifestPath));
                var version = node?["version"]?.GetValue<string>() ?? "unknown";
                var description = node?["description"]?.GetValue<string>();
                return (version, description);
            }
            catch
            {
                // 尝试下一个根目录。
            }
        }
        return null;
    }
}
