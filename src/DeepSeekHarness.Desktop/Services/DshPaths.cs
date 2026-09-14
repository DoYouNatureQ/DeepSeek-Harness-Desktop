using System.IO;
using DeepSeekHarness.Models;

namespace DeepSeekHarness.Services;

/// <summary>解析 dsh 运行时、Node.js、DSH_HOME 与 profile 路径。</summary>
public sealed class DshPaths
{
    public required string RuntimeDir { get; init; }
    public required string DshBinPath { get; init; }
    public required string NodePath { get; init; }
    public required string DshHome { get; init; }
    public required string ProfileDir { get; init; }
    public required string WebProfileDir { get; init; }

    public string DshBinDir => Path.GetDirectoryName(DshBinPath)!;

    /// <summary>按配置与默认位置解析全部路径;无法找到运行时或 Node 时抛出 <see cref="FileNotFoundException"/>。</summary>
    public static DshPaths Resolve(AppConfig config)
    {
        var runtimeDir = ResolveRuntimeDir(config);
        var bin = Path.Combine(runtimeDir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        if (!File.Exists(bin))
        {
            throw new FileNotFoundException("未找到 DeepSeek Harness 运行时入口 lib/bin.js", bin);
        }

        var node = ResolveNode(config);
        var home = ResolveDshHome(config);
        var profiles = Path.Combine(home, "profiles");

        return new DshPaths
        {
            RuntimeDir = runtimeDir,
            DshBinPath = bin,
            NodePath = node,
            DshHome = home,
            ProfileDir = profiles,
            WebProfileDir = Path.Combine(profiles, "web"),
        };
    }

    public static string ResolveDshHome(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.DshHome))
        {
            return ExpandPath(config.DshHome.Trim());
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
    }

    /// <summary>从应用目录向上查找 runtime\node_modules\@deepseek-ai\dsh。</summary>
    public static string ResolveRuntimeDir(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.RuntimeDir))
        {
            var dir = ExpandPath(config.RuntimeDir.Trim());
            if (HasDshPackage(dir)) return dir;
            throw new DirectoryNotFoundException($"配置的运行时目录中未找到 @deepseek-ai/dsh: {dir}");
        }

        var candidates = new List<string>();
        var dir2 = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir2 is not null; i++, dir2 = dir2.Parent)
        {
            candidates.Add(Path.Combine(dir2.FullName, "runtime"));
            candidates.Add(dir2.FullName);
        }

        foreach (var candidate in candidates)
        {
            if (HasDshPackage(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException(
            "未找到 DeepSeek Harness 运行时。请确认 runtime\\node_modules\\@deepseek-ai\\dsh 存在,或在设置中指定运行时目录。");
    }

    public static string ResolveNode(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.NodePath) && File.Exists(config.NodePath))
        {
            return config.NodePath;
        }

        var onPath = FindOnPath("node.exe");
        if (onPath is not null) return onPath;

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node64.exe"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(
            "未找到 Node.js。请安装 Node.js (>= 22.19) 或在设置中指定 node.exe 路径。");
    }

    /// <summary>在 PATH 中查找可执行文件,失败返回 null。</summary>
    public static string? FindOnPath(string exeName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var raw in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var dir = raw.Trim().Trim('"');
                if (dir.Length == 0) continue;
                var full = Path.Combine(dir, exeName);
                if (File.Exists(full)) return full;
            }
            catch
            {
                // 忽略无效 PATH 片段。
            }
        }
        return null;
    }

    /// <summary>npm 全局 bin 目录(存放 pnpm.cmd),用于插件安装。</summary>
    public static string? FindNpmGlobalBin()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "npm");
        return Directory.Exists(dir) ? dir : null;
    }

    private static bool HasDshPackage(string dir)
    {
        try
        {
            return File.Exists(Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"));
        }
        catch
        {
            return false;
        }
    }

    public static string ExpandPath(string path)
    {
        if (path.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path.TrimStart('~', '/', '\\'));
        }
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}
