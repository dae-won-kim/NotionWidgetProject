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

    // ── Public API ────────────────────────────────────────────────────

    public Task<QueryItemsResponseDto> QueryItemsAsync(string widgetId)
        => PostAndUnwrap<QueryItemsResponseDto>(
               $"v1/widgets/{widgetId}/items/query");

    public Task<StatusUpdateResponseDto> StatusNextAsync(string widgetId, string itemId)
        => PostAndUnwrap<StatusUpdateResponseDto>(
               $"v1/widgets/{widgetId}/items/{itemId}/status/next");

    public Task<StatusUpdateResponseDto> StatusSetAsync(string widgetId, string itemId, string statusId)
        => SendAndUnwrap<StatusUpdateResponseDto>(
               new HttpRequestMessage(new HttpMethod("PATCH"),
                   $"v1/widgets/{widgetId}/items/{itemId}/status")
               { Content = JsonContent.Create(new { statusId }) });

    // ── Private helpers ───────────────────────────────────────────────

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

    // ── Response envelope ─────────────────────────────────────────────

    private sealed class ApiEnvelope<T>
    {
        public bool Ok   { get; set; }
        public T?   Data { get; set; }
    }
}
