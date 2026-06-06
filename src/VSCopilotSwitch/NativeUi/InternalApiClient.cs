using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace VSCopilotSwitch;

internal sealed class InternalApiClient : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        TypeInfoResolver = MewUiJsonContext.Default,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public InternalApiClient(string serverUrl)
    {
        _http.BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/");
    }

    public Uri? BaseAddress => _http.BaseAddress;

    public async Task<T> GetJsonAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var response = await _http.GetAsync(path);
        await EnsureSuccessAsync(response, path);

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo)
            ?? throw new InvalidOperationException($"接口 {path} 返回空响应。");
    }

    public async Task<T> PostJsonAsync<TRequest, T>(string path, TRequest request, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var content = JsonContent.Create(request, options: _jsonOptions);
        using var response = await _http.PostAsync(path, content);
        await EnsureSuccessAsync(response, path);

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo)
            ?? throw new InvalidOperationException($"接口 {path} 返回空响应。");
    }

    public async Task<T> DeleteJsonAsync<T>(string path, JsonTypeInfo<T> jsonTypeInfo)
    {
        using var response = await _http.DeleteAsync(path);
        await EnsureSuccessAsync(response, path);

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo)
            ?? throw new InvalidOperationException($"接口 {path} 返回空响应。");
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var error = TryReadError(body);
        throw new InvalidOperationException($"{path} 返回 {(int)response.StatusCode}：{error}");
    }

    private static string TryReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "无错误正文";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("Error", out var error))
            {
                return error.GetString() ?? body;
            }

            if (document.RootElement.TryGetProperty("error", out var lowerError))
            {
                return lowerError.ValueKind == JsonValueKind.String
                    ? lowerError.GetString() ?? body
                    : lowerError.ToString();
            }
        }
        catch
        {
        }

        return body.Length <= 500 ? body : body[..500] + "...";
    }
}
