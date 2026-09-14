using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DeepSeekHarness.Services;

namespace DeepSeekHarness;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    public AppHost Host { get; private set; } = null!;

    /// <summary>诊断开关 --exec:页面首次就绪后执行的脚本。</summary>
    public static string? StartupScript { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        WriteLog("OnStartup begin");
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "DeepSeekHarnessDesktop.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            WriteLog("another instance is running; exiting");
            MessageBox.Show("DeepSeek Harness Desktop 已在运行。", "DeepSeek Harness Desktop",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) ShowFatal(ex);
        };

        Host = new AppHost();
        WriteLog($"host created; ready={Host.IsReady}; pathError={Host.PathError ?? "none"}");
        base.OnStartup(e);

        var execIndex = Array.IndexOf(e.Args, "--exec");
        if (execIndex >= 0 && execIndex + 1 < e.Args.Length)
        {
            var value = e.Args[execIndex + 1];
            if (value.StartsWith('@'))
            {
                try
                {
                    StartupScript = System.IO.File.ReadAllText(value[1..]);
                }
                catch (Exception ex)
                {
                    WriteLog($"读取 --exec 脚本失败: {ex.Message}");
                }
            }
            else
            {
                StartupScript = value;
            }
        }

        var selfTestIndex = Array.IndexOf(e.Args, "--selftest");
        if (selfTestIndex >= 0)
        {
            var output = selfTestIndex + 1 < e.Args.Length
                ? e.Args[selfTestIndex + 1]
                : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-selftest.txt");
            var withServer = Array.IndexOf(e.Args, "--with-server") >= 0;
            var writeTest = Array.IndexOf(e.Args, "--write-test") >= 0;
            _ = RunSelfTestAsync(output, withServer, writeTest);
            return;
        }

        var window = new MainWindow(Host);
        WriteLog("main window constructed");
        MainWindow = window;
        window.Show();
        WriteLog("main window shown");
    }

    private async Task RunSelfTestAsync(string output, bool withServer, bool writeTest)
    {
        try
        {
            var code = await SelfTest.RunAsync(Host, output, withServer, writeTest);
            Shutdown(code);
        }
        catch (Exception ex)
        {
            try { System.IO.File.WriteAllText(output, "自检崩溃: " + ex); } catch { /* 忽略 */ }
            Shutdown(2);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Host?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatal(e.Exception);
    }

    private void ShowFatal(Exception ex)
    {
        WriteLog("FATAL: " + ex);
        Host?.LogApp($"未处理异常: {ex.Message}", true);
        MessageBox.Show($"发生未处理的异常:\n\n{ex.Message}", "DeepSeek Harness Desktop",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>启动与崩溃日志,便于诊断无法启动的问题。</summary>
    internal static void WriteLog(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DeepSeekHarnessDesktop");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "app.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响应用。
        }
    }
}
