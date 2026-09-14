using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using DeepSeekHarness.Models;

namespace DeepSeekHarness.Services;

public enum DshServerState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Failed,
}

public sealed record DshLogLine(string Text, bool IsError, DateTime Time);

/// <summary>管理 `dsh web` 子进程的完整生命周期。</summary>
public sealed class DshServer : IDisposable
{
    private static readonly Regex UrlPattern = new(@"dsh web:\s*(http://[^\s]+)", RegexOptions.Compiled);

    private Process? _process;
    private readonly object _gate = new();

    public DshServerState State { get; private set; } = DshServerState.Stopped;

    /// <summary>带 token 的启动 URL(仅在本次进程生命周期内有效)。</summary>
    public string? LaunchUrl { get; private set; }

    /// <summary>不带 token 的基础地址,例如 http://127.0.0.1:3080。</summary>
    public string? BaseUrl { get; private set; }

    public int ActualPort { get; private set; }

    /// <summary>最近一次失败原因。</summary>
    public string? LastError { get; private set; }

    public event Action<DshLogLine>? LogLine;
    public event Action? StateChanged;
    public event Action<string>? Ready;

    public bool IsRunning => State is DshServerState.Running or DshServerState.Starting;

    public async Task<bool> StartAsync(AppConfig config, DshPaths paths, CancellationToken ct = default)
    {
        if (IsRunning) return true;

        lock (_gate)
        {
            LastError = null;
            LaunchUrl = null;
            BaseUrl = null;
            SetState(DshServerState.Starting);
        }

        var port = config.Port;
        var args = $"\"{paths.DshBinPath}\" web --no-open --port {port}";

        var psi = new ProcessStartInfo
        {
            FileName = paths.NodePath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        psi.Environment["DSH_HOME"] = paths.DshHome;
        if (config.TelemetryDisabled)
        {
            psi.Environment["DSH_TELEMETRY_DISABLED"] = "1";
        }
        var extraPath = new List<string> { Path.GetDirectoryName(paths.NodePath)! };
        var npmBin = DshPaths.FindNpmGlobalBin();
        if (npmBin is not null) extraPath.Add(npmBin);
        psi.Environment["PATH"] = string.Join(Path.PathSeparator, extraPath) + Path.PathSeparator + (psi.Environment.TryGetValue("PATH", out var oldPath) ? oldPath : Environment.GetEnvironmentVariable("PATH") ?? string.Empty);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnLine(e.Data, isError: false);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data, isError: true);
        process.Exited += (_, _) => OnExited();

        lock (_gate)
        {
            _process = process;
        }

        try
        {
            if (!process.Start())
            {
                Fail("无法启动 dsh 进程。");
                return false;
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Fail($"启动 dsh 失败: {ex.Message}");
            return false;
        }

        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (State == DshServerState.Running) return true;
            if (State is DshServerState.Failed or DshServerState.Stopped) return false;
            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        Fail("启动超时:3 分钟内未收到服务就绪信号。");
        Stop();
        return false;
    }

    public void Stop()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }
        if (process is null)
        {
            SetState(DshServerState.Stopped);
            return;
        }

        SetState(DshServerState.Stopping);
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch
        {
            // 进程可能已退出。
        }
        finally
        {
            process.Dispose();
        }

        LaunchUrl = null;
        BaseUrl = null;
        ActualPort = 0;
        SetState(DshServerState.Stopped);
    }

    public async Task RestartAsync(AppConfig config, DshPaths paths, CancellationToken ct = default)
    {
        Stop();
        await Task.Delay(400, ct).ConfigureAwait(false);
        await StartAsync(config, paths, ct).ConfigureAwait(false);
    }

    private void OnLine(string? line, bool isError)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        LogLine?.Invoke(new DshLogLine(line, isError, DateTime.Now));

        var match = UrlPattern.Match(line);
        if (match.Success)
        {
            var url = match.Groups[1].Value;
            LaunchUrl = url;
            try
            {
                var uri = new Uri(url);
                BaseUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
                ActualPort = uri.Port;
            }
            catch
            {
                BaseUrl = null;
            }
            SetState(DshServerState.Running);
            Ready?.Invoke(url);
        }

        if (line.Contains("EADDRINUSE", StringComparison.OrdinalIgnoreCase))
        {
            LastError = $"端口 {ActualPort} 已被占用,请在设置中更换端口后重试。";
        }
    }

    private void OnExited()
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }
        var exitCode = 0;
        try
        {
            exitCode = process?.ExitCode ?? 0;
        }
        catch
        {
            // 进程句柄可能已失效。
        }
        process?.Dispose();

        if (State == DshServerState.Stopping || State == DshServerState.Stopped)
        {
            SetState(DshServerState.Stopped);
            return;
        }

        LastError ??= $"dsh 进程意外退出(退出码 {exitCode})。";
        LogLine?.Invoke(new DshLogLine(LastError, true, DateTime.Now));
        SetState(DshServerState.Failed);
    }

    private void Fail(string message)
    {
        LastError = message;
        LogLine?.Invoke(new DshLogLine(message, true, DateTime.Now));
        SetState(DshServerState.Failed);
    }

    private void SetState(DshServerState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        Stop();
    }
}
