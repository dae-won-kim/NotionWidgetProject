using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var notionToken = builder.Configuration["Notion:Token"]!;
var databaseId  = builder.Configuration["Notion:DatabaseId"]!;

var http = new HttpClient { BaseAddress = new Uri("https://api.notion.com/v1/") };
http.DefaultRequestHeaders.Add("Authorization",   $"Bearer {notionToken}");
http.DefaultRequestHeaders.Add("Notion-Version",  "2022-06-28");

var statusOrder  = new[] { "시작 전", "진행 중", "완료" };
var jsonOpts     = new JsonSerializerOptions();
var cachedOptions = new List<(string Id, string Name, string Color)>();

var app = builder.Build();

app.MapGet("/",       () => Results.Ok(new { app = "widget-api", ok = true }));
app.MapGet("/health", () => Results.Ok(new { ok = true }));

// ── Query items ────────────────────────────────────────────────────────────
app.MapPost("/v1/widgets/{widgetId}/items/query", async (string widgetId) =>
{
    var dbDoc = JsonDocument.Parse(
        await (await http.GetAsync($"databases/{databaseId}")).Content.ReadAsStringAsync());

    cachedOptions =
    [
        .. dbDoc.RootElement
            .GetProperty("properties")
            .GetProperty("Status")
            .GetProperty("status")
            .GetProperty("options")
            .EnumerateArray()
            .Select(o => (
                Id:    o.GetProperty("id").GetString()!,
                Name:  o.GetProperty("name").GetString()!,
                Color: MapNotionColor(o.GetProperty("color").GetString())))
    ];

    var qDoc = JsonDocument.Parse(
        await (await http.PostAsync($"databases/{databaseId}/query", null)).Content.ReadAsStringAsync());

    var items = qDoc.RootElement.GetProperty("results").EnumerateArray()
        .Select(page =>
        {
            var props = page.GetProperty("properties");
            var (statusId, statusName) = ParseStatus(props);

            return new
            {
                id             = page.GetProperty("id").GetString()!,
                title          = ParseTitle(props),
                isChecked      = statusName == "완료",
                statusId,
                status         = statusName,
                days           = ParseDays(props),
                note           = ParseNote(props),
                lastEditedTime = page.GetProperty("last_edited_time").GetString()!
            };
        })
        .ToList();

    var statusOptionsDto = cachedOptions.Select(o => new { id = o.Id, name = o.Name, color = o.Color });
    return Results.Ok(new { ok = true, data = new { items, statusOptions = statusOptionsDto } });
});

// ── Status: cycle to next ──────────────────────────────────────────────────
app.MapPost("/v1/widgets/{widgetId}/items/{itemId}/status/next",
    async (string widgetId, string itemId) =>
{
    var pageRes = await http.GetAsync($"pages/{itemId}");
    if (!pageRes.IsSuccessStatusCode)
        return Results.NotFound(new { ok = false, error = new { code = "NOT_FOUND" } });

    var (_, current) = ParseStatus(
        JsonDocument.Parse(await pageRes.Content.ReadAsStringAsync())
                    .RootElement.GetProperty("properties"));

    var idx  = Array.IndexOf(statusOrder, current);
    var next = statusOrder[idx < 0 ? 0 : (idx + 1) % statusOrder.Length];

    return await PatchStatusByName(itemId, next);
});

// ── Status: set specific ───────────────────────────────────────────────────
app.MapMethods("/v1/widgets/{widgetId}/items/{itemId}/status", new[] { "PATCH" },
    async ([FromRoute] string widgetId, [FromRoute] string itemId, [FromBody] StatusSetBody body) =>
{
    var (_, optName, _) = cachedOptions.FirstOrDefault(o => o.Id == body.StatusId);
    if (string.IsNullOrEmpty(optName))
        return Results.BadRequest(new { ok = false, error = new { code = "BAD_STATUS" } });

    return await PatchStatusByName(itemId, optName);
});

app.Run();

// ── PATCH helper ──────────────────────────────────────────────────────────
async Task<IResult> PatchStatusByName(string pageId, string statusName)
{
    var payload  = JsonSerializer.Serialize(
        new { properties = new { Status = new { status = new { name = statusName } } } },
        jsonOpts);
    var patchRes = await http.PatchAsync($"pages/{pageId}",
        new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

    if (!patchRes.IsSuccessStatusCode)
        return Results.NotFound(new { ok = false, error = new { code = "PATCH_FAILED" } });

    var doc   = JsonDocument.Parse(await patchRes.Content.ReadAsStringAsync());
    var props = doc.RootElement.GetProperty("properties");
    var (updatedId, updatedName) = ParseStatus(props, statusName);

    return Results.Ok(new
    {
        ok   = true,
        data = new
        {
            id             = pageId,
            statusId       = updatedId,
            status         = updatedName,
            lastEditedTime = doc.RootElement.GetProperty("last_edited_time").GetString()!
        }
    });
}

// ── Notion property parsers ───────────────────────────────────────────────
static string ParseTitle(JsonElement props, string propName = "Task")
{
    if (!props.TryGetProperty(propName, out var prop)) return "(Untitled)";
    var arr = prop.GetProperty("title").EnumerateArray().ToArray();
    return arr.Length > 0
        ? arr[0].GetProperty("text").GetProperty("content").GetString() ?? ""
        : "(Untitled)";
}

static (string Id, string Name) ParseStatus(JsonElement props, string defaultName = "시작 전")
{
    if (props.TryGetProperty("Status", out var sp) &&
        sp.TryGetProperty("status", out var so) &&
        so.ValueKind != JsonValueKind.Null)
        return (so.GetProperty("id").GetString()!, so.GetProperty("name").GetString()!);
    return ("", defaultName);
}

static List<string> ParseDays(JsonElement props)
    => props.TryGetProperty("Day", out var dp) && dp.TryGetProperty("multi_select", out var ms)
        ? ms.EnumerateArray().Select(d => d.GetProperty("name").GetString()!).ToList()
        : new List<string>();

static string ParseNote(JsonElement props)
{
    if (!props.TryGetProperty("Note", out var np) || !np.TryGetProperty("rich_text", out var rt))
        return "";
    var arr = rt.EnumerateArray().ToArray();
    return arr.Length > 0
        ? arr[0].GetProperty("text").GetProperty("content").GetString() ?? ""
        : "";
}

// ── Color mapping ─────────────────────────────────────────────────────────
static string MapNotionColor(string? color) => color switch
{
    "blue"            => "blue",
    "green"           => "green",
    "yellow"
    or "orange"       => "yellow",
    "red" or "pink"   => "red",
    _                 => "gray"
};

sealed class StatusSetBody { public string StatusId { get; set; } = ""; }
