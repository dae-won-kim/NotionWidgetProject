using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using WidgetDesktop.Models;

namespace WidgetDesktop.Services;

public sealed class WidgetApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public WidgetApiClient(string baseUrl)
        => _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };

    // ── Auth ──────────────────────────────────────────────────────────

    public async Task<bool> GetAuthStatusAsync()
    {
        try
        {
            var res = await _http.GetFromJsonAsync<AuthStatusDto>("auth/status", _json);
            return res?.Configured ?? false;
        }
        catch { return false; }
    }

    public async Task<(bool Ok, string? AuthUrl, string? Error)> StartNotionOAuthAsync()
    {
        var res = await _http.PostAsync("auth/notion/start", content: null);

        if (res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadFromJsonAsync<ApiEnvelope<AuthStartDto>>(_json);
            return (true, body?.Data?.AuthUrl, null);
        }

        try
        {
            var body = await res.Content.ReadFromJsonAsync<ErrorDto>(_json);
            return (false, null, body?.Error ?? "Notion 로그인 시작에 실패했습니다.");
        }
        catch
        {
            return (false, null, "서버 오류가 발생했습니다.");
        }
    }

    // Returns (success, errorMessage)
    public async Task<(bool Ok, string? Error)> ConfigureAsync(string token, string databaseId)
    {
        var res = await _http.PostAsJsonAsync("auth/configure", new { token, databaseId });

        if (res.IsSuccessStatusCode)
            return (true, null);

        try
        {
            var body = await res.Content.ReadFromJsonAsync<ErrorDto>(_json);
            return (false, body?.Error ?? "알 수 없는 오류가 발생했습니다.");
        }
        catch
        {
            return (false, "서버 오류가 발생했습니다.");
        }
    }

    public async Task LogoutAsync()
    {
        try { await _http.PostAsync("auth/logout", content: null); }
        catch { }
    }

    // ── Widget items ──────────────────────────────────────────────────

    public Task<QueryItemsResponseDto> QueryItemsAsync(string widgetId)
        => PostAndUnwrap<QueryItemsResponseDto>($"v1/widgets/{widgetId}/items/query");

    public Task<StatusUpdateResponseDto> StatusNextAsync(string widgetId, string itemId)
        => PostAndUnwrap<StatusUpdateResponseDto>($"v1/widgets/{widgetId}/items/{itemId}/status/next");

    public Task<StatusUpdateResponseDto> StatusSetAsync(string widgetId, string itemId, string statusId)
        => SendAndUnwrap<StatusUpdateResponseDto>(
               new HttpRequestMessage(new HttpMethod("PATCH"),
                   $"v1/widgets/{widgetId}/items/{itemId}/status")
               { Content = JsonContent.Create(new { statusId }) });

    // ── Helpers ───────────────────────────────────────────────────────

    private async Task<T> PostAndUnwrap<T>(string url)
        => await Unwrap<T>(await _http.PostAsync(url, content: null));

    private async Task<T> SendAndUnwrap<T>(HttpRequestMessage req)
        => await Unwrap<T>(await _http.SendAsync(req));

    private async Task<T> Unwrap<T>(HttpResponseMessage res)
    {
        res.EnsureSuccessStatusCode();
        var env = await res.Content.ReadFromJsonAsync<ApiEnvelope<T>>(_json)
                  ?? throw new InvalidOperationException("Empty response");
        if (!env.Ok || env.Data is null)
            throw new InvalidOperationException("API returned not ok");
        return env.Data;
    }

    // ── Response types ────────────────────────────────────────────────

    private sealed class ApiEnvelope<T> { public bool Ok { get; set; } public T? Data { get; set; } }
    private sealed class AuthStatusDto  { public bool Configured { get; set; } }
    private sealed class AuthStartDto   { public string AuthUrl { get; set; } = ""; }
    private sealed class ErrorDto       { public string? Error { get; set; } }
}
