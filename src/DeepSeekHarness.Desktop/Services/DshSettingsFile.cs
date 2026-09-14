using System.IO;
using System.Text;
using YamlDotNet.Serialization;

namespace DeepSeekHarness.Services;

/// <summary>
/// 直接读写 DSH_HOME 下的 settings.yaml / .credentials.yaml。
/// 服务运行时应优先使用 RPC API(热重载、冲突保护),此实现用于服务未启动时的兜底。
/// </summary>
public sealed class DshSettingsFile
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().Build();
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string DshHome { get; }
    public string SettingsPath { get; }
    public string CredentialsPath { get; }

    public DshSettingsFile(string dshHome)
    {
        DshHome = dshHome;
        SettingsPath = Path.Combine(dshHome, "settings.yaml");
        CredentialsPath = Path.Combine(dshHome, ".credentials.yaml");
    }

    public bool HasSettingsDocument => File.Exists(SettingsPath);

    // ===================== settings.yaml =====================

    public Dictionary<string, object?> ReadSettings()
    {
        if (!File.Exists(SettingsPath)) return new Dictionary<string, object?>();
        var text = File.ReadAllText(SettingsPath);
        if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object?>();
        try
        {
            var raw = Deserializer.Deserialize<Dictionary<string, object?>>(text)
                      ?? new Dictionary<string, object?>();
            return NormalizeMap(raw);
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    public void WriteSettings(Dictionary<string, object?> settings)
    {
        Directory.CreateDirectory(DshHome);
        var text = Serializer.Serialize(settings);
        WriteAtomic(SettingsPath, text);
    }

    /// <summary>读取命名空间下某个路径的值。</summary>
    public object? GetValue(params string[] path)
    {
        object? current = ReadSettings();
        foreach (var key in path)
        {
            if (current is IDictionary<string, object?> typed)
            {
                if (!typed.TryGetValue(key, out current)) return null;
            }
            else if (current is IDictionary<object, object> generic)
            {
                if (!generic.TryGetValue(key, out current)) return null;
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    public string? GetString(params string[] path)
    {
        return GetValue(path) as string;
    }

    /// <summary>设置命名空间下某个路径的值(自动创建中间映射)。</summary>
    public void SetValue(object? value, params string[] path)
    {
        if (path.Length == 0) throw new ArgumentException("路径不能为空", nameof(path));
        var settings = ReadSettings();
        var current = settings;
        for (var i = 0; i < path.Length - 1; i++)
        {
            if (current.TryGetValue(path[i], out var next))
            {
                if (next is Dictionary<string, object?> typed)
                {
                    current = typed;
                    continue;
                }
                if (next is IDictionary<object, object> generic)
                {
                    var converted = NormalizeMap(generic);
                    current[path[i]] = converted;
                    current = converted;
                    continue;
                }
            }
            var child = new Dictionary<string, object?>();
            current[path[i]] = child;
            current = child;
        }
        current[path[^1]] = value;
        WriteSettings(settings);
    }

    public void RemoveSection(string name)
    {
        var settings = ReadSettings();
        if (settings.Remove(name))
        {
            WriteSettings(settings);
        }
    }

    // ===================== .credentials.yaml =====================

    public bool HasCredential(string reference)
    {
        var credentials = ReadCredentials();
        return credentials.TryGetValue("refs", out var refs)
               && refs is Dictionary<string, object?> map
               && map.ContainsKey(reference);
    }

    /// <summary>读取凭据值(仅用于诊断与迁移;产品界面不应回显密钥)。</summary>
    public string? GetCredential(string reference)
    {
        var credentials = ReadCredentials();
        if (credentials.TryGetValue("refs", out var refs)
            && refs is Dictionary<string, object?> map
            && map.TryGetValue(reference, out var value))
        {
            return value as string;
        }
        return null;
    }

    public void SetCredential(string reference, string value)
    {
        var credentials = ReadCredentials();
        if (credentials.TryGetValue("refs", out var refs) && refs is Dictionary<string, object?> existing)
        {
            existing[reference] = value;
        }
        else
        {
            credentials["refs"] = new Dictionary<string, object?> { [reference] = value };
        }
        credentials["version"] = 1;
        WriteCredentials(credentials);
    }

    public void RemoveCredential(string reference)
    {
        var credentials = ReadCredentials();
        if (credentials.TryGetValue("refs", out var refs) && refs is Dictionary<string, object?> existing)
        {
            existing.Remove(reference);
            WriteCredentials(credentials);
        }
    }

    private Dictionary<string, object?> ReadCredentials()
    {
        if (!File.Exists(CredentialsPath)) return new Dictionary<string, object?>();
        var text = File.ReadAllText(CredentialsPath);
        if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object?>();
        try
        {
            var raw = Deserializer.Deserialize<Dictionary<string, object?>>(text)
                      ?? new Dictionary<string, object?>();
            return NormalizeMap(raw);
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private void WriteCredentials(Dictionary<string, object?> credentials)
    {
        Directory.CreateDirectory(DshHome);
        var text = Serializer.Serialize(credentials);
        WriteAtomic(CredentialsPath, text);
    }

    private static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".dshdesktop.tmp";
        File.WriteAllText(tmp, text, Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>把 YamlDotNet 产出的 Dictionary&lt;object, object&gt; 递归规整为 Dictionary&lt;string, object?&gt;。</summary>
    private static Dictionary<string, object?> NormalizeMap(IDictionary<object, object> source)
        => NormalizePairs(source.Select(kv => (kv.Key, (object?)kv.Value)));

    private static Dictionary<string, object?> NormalizeMap(IDictionary<string, object?> source)
        => NormalizePairs(source.Select(kv => ((object)kv.Key, kv.Value)));

    private static Dictionary<string, object?> NormalizePairs(IEnumerable<(object Key, object? Value)> pairs)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in pairs)
        {
            result[Convert.ToString(key, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty] = NormalizeValue(value);
        }
        return result;
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        IDictionary<object, object> generic => NormalizeMap(generic),
        IDictionary<string, object?> typed => typed.ToDictionary(kv => kv.Key, kv => NormalizeValue(kv.Value)),
        IList<object> list => list.Select(NormalizeValue).ToList(),
        _ => value,
    };
}
