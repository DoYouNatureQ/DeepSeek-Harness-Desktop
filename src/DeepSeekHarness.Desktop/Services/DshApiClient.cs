using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeepSeekHarness.Services;

public sealed class DshApiException : Exception
{
    public string Code { get; }

    public DshApiException(string code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>
/// dsh Web 服务的 RPC 客户端。协议:`POST /api/&lt;namespace&gt;/&lt;method&gt;`,
/// 请求体为 client-request 信封,鉴权使用 token 换取的浏览器会话 Cookie。
/// </summary>
public sealed class DshApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private HttpClient? _http;
    private CookieContainer? _cookies;

    public string? BaseUrl { get; private set; }
    public bool IsAuthenticated { get; private set; }

    /// <summary>使用启动 URL 中的一次性 token 完成鉴权并保存会话 Cookie。</summary>
    public async Task<bool> AuthenticateAsync(string baseUrl, string tokenUrl, CancellationToken ct = default)
    {
        Reset();
        _cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true,
            AllowAutoRedirect = true,
            UseProxy = false,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        BaseUrl = baseUrl.TrimEnd('/');

        try
        {
            using var response = await _http.GetAsync(tokenUrl, ct).ConfigureAwait(false);
            IsAuthenticated = (int)response.StatusCode < 400;
        }
        catch
        {
            IsAuthenticated = false;
        }
        return IsAuthenticated;
    }

    /// <summary>调用一个 RPC 方法,返回 result.value;失败时抛出 <see cref="DshApiException"/>。</summary>
    public async Task<JsonNode?> CallAsync(string method, object? args = null, CancellationToken ct = default)
    {
        var http = _http ?? throw new InvalidOperationException("API 客户端尚未初始化。");
        var baseUrl = BaseUrl ?? throw new InvalidOperationException("API 地址未知。");

        var envelope = new Dictionary<string, object?>
        {
            ["type"] = "client-request",
            ["rpcId"] = Guid.NewGuid().ToString(),
            ["method"] = method,
            ["payload"] = new Dictionary<string, object?>
            {
                ["args"] = args ?? new Dictionary<string, object?>(),
            },
        };

        using var content = new StringContent(JsonSerializer.Serialize(envelope, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync($"{baseUrl}/api/{method}", content, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(text))
        {
            throw new DshApiException($"http/{(int)response.StatusCode}", $"请求 {method} 失败: HTTP {(int)response.StatusCode}");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            throw new DshApiException("http/invalid-json", $"请求 {method} 返回了非 JSON 响应: {Truncate(text)}");
        }

        var result = root?["result"];
        if (result is null)
        {
            throw new DshApiException("http/invalid-envelope", $"请求 {method} 返回了无效信封: {Truncate(text)}");
        }
        if (result["ok"]?.GetValue<bool>() == true)
        {
            return result["value"];
        }

        var error = result["error"];
        var code = error?["code"]?.GetValue<string>() ?? "unknown";
        var message = error?["message"]?.GetValue<string>() ?? "未知错误";
        throw new DshApiException(code, message);
    }

    public void Reset()
    {
        _http?.Dispose();
        _http = null;
        _cookies = null;
        IsAuthenticated = false;
        BaseUrl = null;
    }

    public void Dispose()
    {
        Reset();
    }

    private static string Truncate(string text) => text.Length <= 400 ? text : text[..400] + "…";
}
