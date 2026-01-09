using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using WidgetDesktop.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WidgetDesktop.Services;

public sealed class WidgetApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public WidgetApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
    }

    // 백엔드 응답: { ok: true, data: { items: [...], statusOptions: [...] } }
    private sealed class ApiEnvelope<T>
    {
        public bool Ok { get; set; }
        public T? Data { get; set; }
    }

    public async Task<QueryItemsResponseDto> QueryItemsAsync(string widgetId)
    {
        var res = await _http.PostAsync($"v1/widgets/{widgetId}/items/query", content: null);
        res.EnsureSuccessStatusCode();

        var env = await res.Content.ReadFromJsonAsync<ApiEnvelope<QueryItemsResponseDto>>(_json)
                  ?? throw new InvalidOperationException("Empty response");

        if (!env.Ok || env.Data is null)
            throw new InvalidOperationException("API returned not ok");

        return env.Data;
    }

    public async Task<StatusUpdateResponseDto> StatusNextAsync(string widgetId, string itemId)
{
    var res = await _http.PostAsync($"v1/widgets/{widgetId}/items/{itemId}/status/next", content: null);
    res.EnsureSuccessStatusCode();

    var env = await res.Content.ReadFromJsonAsync<ApiEnvelope<StatusUpdateResponseDto>>(_json)
              ?? throw new InvalidOperationException("Empty response");

    if (!env.Ok || env.Data is null)
        throw new InvalidOperationException("API returned not ok");

    return env.Data;
}

public async Task<StatusUpdateResponseDto> StatusSetAsync(string widgetId, string itemId, string statusId)
{
    var req = new { statusId };
    var httpReq = new HttpRequestMessage(new HttpMethod("PATCH"), $"v1/widgets/{widgetId}/items/{itemId}/status")
    {
        Content = JsonContent.Create(req)
    };

    var res = await _http.SendAsync(httpReq);
    res.EnsureSuccessStatusCode();

    var env = await res.Content.ReadFromJsonAsync<ApiEnvelope<StatusUpdateResponseDto>>(_json)
              ?? throw new InvalidOperationException("Empty response");

    if (!env.Ok || env.Data is null)
        throw new InvalidOperationException("API returned not ok");

    return env.Data;
}

}
