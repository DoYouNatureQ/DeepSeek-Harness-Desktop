using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DeepSeekHarness.Services;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekHarness;

/// <summary>
/// 主窗口:整窗即 DeepSeek Harness 官方页面。
/// 加载/错误状态由页面自身渲染并跟随主题;插件管理、服务控制与日志通过
/// 设置页内的"桌面工具"分区提供,页面与原生之间走 WebView2 消息桥。
/// </summary>
public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly AppHost _host;
    private readonly DesktopBridge _bridge;
    private readonly ThemeWatcher _theme;
    private bool _webReady;
    private bool _navigatedForSession;
    private bool _closing;
    private bool _startupScriptPending;
    private string _loadingSignature = string.Empty;

    public MainWindow(AppHost host)
    {
        _host = host;
        _bridge = new DesktopBridge(host);
        _theme = host.Settings is null ? null! : new ThemeWatcher(host.Settings);
        InitializeComponent();
        _host.StatusChanged += OnStatusChanged;
        Loaded += OnLoaded;
        Closing += OnClosing;
        SourceInitialized += OnSourceInitialized;
    }

    // ===================== 生命周期 =====================

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_host.Settings is not null)
        {
            _theme.Changed += ApplyTheme;
            _theme.Start();
        }
        ApplyTitleBarTheme();

        var ready = await InitializeWebViewAsync();

        if (!ready)
        {
            return;
        }

        if (!_host.IsReady)
        {
            ShowLoadingPage("运行时未就绪", _host.PathError ?? "请检查运行时目录与 Node.js 安装。", LoadingKind.Failed);
            return;
        }

        ShowLoadingPage("正在启动 DeepSeek Harness…", "正在准备本地运行时,首次启动可能需要数十秒。", LoadingKind.Starting);

        try
        {
            var (ok, message) = await DesktopPluginInstaller.EnsureInstalledAsync(_host);
            _host.LogApp(message, !ok);
        }
        catch (Exception ex)
        {
            _host.LogApp($"桌面工具插件准备失败: {ex.Message}", true);
        }

        await _host.EnsureDefaultThemeAsync();
        ApplyTitleBarTheme();

        if (_host.Config.AutoStartServer)
        {
            await _host.StartServerAsync();
        }
        UpdateStatus();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_closing) return;
        if (_host.Config.CloseToTray)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }
        _closing = true;
        _theme?.Dispose();
        _host.StopServer();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyTitleBarTheme();
    }

    // ===================== 主题 =====================

    private void ApplyTheme()
    {
        ApplyTitleBarTheme();
        if (_webReady && _host.Server.State != DshServerState.Running)
        {
            // 加载页跟随主题重新渲染。
            _loadingSignature = string.Empty;
            UpdateStatus();
        }
    }

    private void ApplyTitleBarTheme()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            var dark = _host.Settings is null || _theme is null || _theme.IsDark;
            var value = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, 20, ref value, sizeof(int));
            _ = DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
        }
        catch
        {
            // 老版本系统忽略。
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // ===================== WebView2 =====================

    private async Task<bool> InitializeWebViewAsync()
    {
        if (_webReady) return true;
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekHarnessDesktop", "WebView2");
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(environment);
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.NavigationStarting += OnNavigationStarting;
            Web.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            Web.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            Web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webReady = true;
            return true;
        }
        catch (Exception ex)
        {
            _host.LogApp($"WebView2 初始化失败: {ex.Message}", true);
            Web.Visibility = Visibility.Collapsed;
            FallbackState.Visibility = Visibility.Visible;
            FallbackText.Text = ex.Message;
            return false;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (App.StartupScript is not { } script || _startupScriptPending) return;
        if (!e.IsSuccess) return;
        if (!Web.CoreWebView2.Source.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)) return;
        _startupScriptPending = true;
        try
        {
            // 等待页面交互就绪后再执行诊断脚本。
            await Task.Delay(2500);
            var parameters = JsonSerializer.Serialize(new
            {
                expression = script,
                awaitPromise = true,
                returnByValue = true,
            });
            var result = await Web.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters);
            _host.LogApp($"诊断脚本执行结果: {result}");
            App.WriteLog("exec: " + result);
        }
        catch (Exception ex)
        {
            _host.LogApp($"诊断脚本执行失败: {ex.Message}", true);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            root = document.RootElement.Clone();
        }
        catch
        {
            return;
        }

        if (root.ValueKind != JsonValueKind.Object) return;
        if (!root.TryGetProperty("dsh", out var tag) || tag.GetString() != "desktop") return;

        var req = root.TryGetProperty("req", out var reqElement) && reqElement.TryGetInt32(out var requestId) ? requestId : 0;
        var action = root.TryGetProperty("action", out var actionElement) ? actionElement.GetString() ?? string.Empty : string.Empty;

        var result = await _bridge.HandleAsync(action, root);

        var reply = new Dictionary<string, object?>
        {
            ["dsh"] = "desktop",
            ["req"] = req,
            ["ok"] = result.Ok,
        };
        if (result.Ok) reply["data"] = result.Data;
        else reply["error"] = result.Error;

        var json = JsonSerializer.Serialize(reply, JsonOptions);
        Dispatcher.Invoke(() =>
        {
            try
            {
                Web.CoreWebView2?.PostWebMessageAsJson(json);
            }
            catch
            {
                // 页面可能已导航离开。
            }
        });
    }

    // ===================== 状态与加载页 =====================

    private enum LoadingKind
    {
        Starting,
        Failed,
        Stopped,
    }

    private void OnStatusChanged() => Dispatcher.BeginInvoke(UpdateStatus);

    private void UpdateStatus()
    {
        if (!_webReady) return;

        if (_host.Server.State == DshServerState.Running)
        {
            _loadingSignature = string.Empty;
            if (!_navigatedForSession && Web.CoreWebView2 is not null && _host.Server.LaunchUrl is { } url)
            {
                _navigatedForSession = true;
                Web.CoreWebView2.Navigate(url);
            }
            return;
        }

        _navigatedForSession = false;
        switch (_host.Server.State)
        {
            case DshServerState.Starting:
                ShowLoadingPage("正在启动 DeepSeek Harness…", "正在准备本地运行时,首次启动可能需要数十秒。", LoadingKind.Starting);
                break;
            case DshServerState.Failed:
                ShowLoadingPage("服务启动失败", _host.Server.LastError ?? "请查看应用日志了解详情。", LoadingKind.Failed);
                break;
            case DshServerState.Stopping:
                ShowLoadingPage("正在停止服务…", "服务进程正在退出。", LoadingKind.Starting);
                break;
            default:
                ShowLoadingPage("服务未启动", "点击下方按钮启动本地 DeepSeek Harness 服务。", LoadingKind.Stopped);
                break;
        }
    }

    private void ShowLoadingPage(string title, string description, LoadingKind kind)
    {
        if (!_webReady || Web.CoreWebView2 is null) return;
        var dark = _theme is null || _theme.IsDark;
        var signature = $"{title}|{description}|{kind}|{dark}";
        if (signature == _loadingSignature) return;
        _loadingSignature = signature;
        Web.CoreWebView2.NavigateToString(BuildLoadingPage(dark, title, description, kind));
    }

    /// <summary>加载/错误页:由页面渲染,颜色跟随主题设置。</summary>
    private static string BuildLoadingPage(bool dark, string title, string description, LoadingKind kind)
    {
        var spinner = kind == LoadingKind.Starting
            ? "<div class=\"spinner\"></div>"
            : "<div class=\"logo\">" + LoadWhaleSvg() + "</div>";

        var actions = kind == LoadingKind.Starting
            ? string.Empty
            : "<div class=\"actions\"><button class=\"primary\" onclick=\"act('service.start')\">" +
              (kind == LoadingKind.Failed ? "重试启动" : "启动服务") +
              "</button><button onclick=\"act('app.openLog')\">打开应用日志</button></div>";

        return "<!doctype html><html data-theme=\"" + (dark ? "dark" : "light") + "\"><head><meta charset=\"utf-8\">" +
               "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>DeepSeek Harness</title><style>" +
               ":root{--bg:#151517;--card:#2c2c2e;--border:rgba(255,255,255,.12);--text:#f9fafb;--muted:#81858c;--brand:#4176e6;}" +
               "html[data-theme=light]{--bg:#f9fafb;--card:#ffffff;--border:rgba(0,0,0,.1);--text:#0f0f0f;--muted:#7f8287;--brand:#4176e6;}" +
               "html,body{height:100%;margin:0;}body{display:flex;align-items:center;justify-content:center;background:var(--bg);color:var(--text);" +
               "font-family:'Segoe UI','Microsoft YaHei UI','PingFang SC',sans-serif;transition:background .2s ease,color .2s ease;}" +
               ".wrap{display:flex;flex-direction:column;align-items:center;max-width:460px;padding:0 32px;text-align:center;}" +
               ".logo{width:56px;height:56px;border-radius:16px;background:var(--brand);display:flex;align-items:center;justify-content:center;margin-bottom:20px;}" +
               ".spinner{width:22px;height:22px;border:2px solid var(--border);border-top-color:var(--brand);border-radius:50%;animation:spin 1s linear infinite;margin-bottom:20px;}" +
               "@keyframes spin{to{transform:rotate(360deg)}}" +
               "h1{font-size:16px;font-weight:600;margin:0 0 10px;}p{font-size:12.5px;color:var(--muted);line-height:1.8;margin:0;word-break:break-all;}" +
               ".actions{display:flex;gap:10px;margin-top:22px;}" +
               "button{padding:9px 20px;border-radius:8px;border:1px solid var(--border);background:transparent;color:var(--text);font-size:13px;cursor:pointer;font-family:inherit;transition:opacity .15s ease;}" +
               "button:hover{opacity:.85;}button.primary{background:var(--text);color:var(--bg);border-color:transparent;font-weight:600;}" +
               "</style></head><body><div class=\"wrap\">" + spinner +
               "<h1>" + Html(title) + "</h1><p>" + Html(description) + "</p>" + actions +
               "</div><script>function act(a){try{window.chrome.webview.postMessage({dsh:'desktop',req:Date.now()%1000000,action:a});}catch(e){}}</script>" +
               "</body></html>";
    }

    private static string Html(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    /// <summary>加载页上的 DeepSeek 鲸鱼标志(白色,置于蓝色圆角方块内)。</summary>
    private static string LoadWhaleSvg()
    {
        try
        {
            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("DeepSeekHarness.Desktop.Assets.deepseek.svg");
            if (stream is null) return string.Empty;
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            var svg = reader.ReadToEnd();
            svg = System.Text.RegularExpressions.Regex.Replace(svg, "<style>.*?</style>", string.Empty,
                System.Text.RegularExpressions.RegexOptions.Singleline);
            svg = svg.Replace("fill=\"#4D6BFE\"", "fill=\"#fff\"");
            svg = svg.Replace("width=\"50.000000\" height=\"50.000000\"", "width=\"26\" height=\"26\"");
            svg = svg.Replace("width=\"50\" height=\"50\"", "width=\"26\" height=\"26\"");
            return svg;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void OpenExternal(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 忽略无法打开的链接。
        }
    }
}
