using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace DeepSeekHarness.Services;

/// <summary>
/// 跟随 Harness 的 <c>ui-theme.preference</c>(light / dark / system):
/// 监听 settings.yaml 与系统主题变化,向界面提供当前明暗状态。
/// </summary>
public sealed class ThemeWatcher : IDisposable
{
    private readonly DshSettingsFile _settings;
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;

    /// <summary>主题(或系统主题)变化时触发;已在 UI 线程。</summary>
    public event Action? Changed;

    public ThemeWatcher(DshSettingsFile settings)
    {
        _settings = settings;
    }

    public string Preference => _settings.GetString("ui-theme", "preference") ?? "system";

    /// <summary>当前应使用的明暗模式。</summary>
    public bool IsDark => Preference switch
    {
        "dark" => true,
        "light" => false,
        _ => IsSystemDark(),
    };

    public void Start()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settings.SettingsPath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                _watcher = new FileSystemWatcher(directory, "settings.y*ml")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnSettingsTouched;
                _watcher.Created += OnSettingsTouched;
                _watcher.Renamed += OnSettingsTouched;
            }
        }
        catch
        {
            // 监听失败不影响主题读取。
        }

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnSettingsTouched(object sender, FileSystemEventArgs e)
    {
        _debounce?.Dispose();
        _debounce = new System.Threading.Timer(_ => Raise(), null, 300, System.Threading.Timeout.Infinite);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            Raise();
        }
    }

    private void Raise()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Changed?.Invoke();
        else dispatcher.BeginInvoke(() => Changed?.Invoke());
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return true;
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
