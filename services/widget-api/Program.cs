using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var notionToken  = builder.Configuration["Notion:Token"]!;
var databaseId   = builder.Configuration["Notion:DatabaseId"]!;

// Shared HttpClient — configured once at startup
var http = new HttpClient { BaseAddress = new Uri("https://api.notion.com/v1/") };
http.DefaultRequestHeaders.Add("Authorization", $"Bearer {notionToken}");
http.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

// Status cycle order
var statusOrder = new[] { "To-do", "In progress", "Done" };

// JSON options that preserve property casing (no camelCase renaming)
var jsonOpts = new JsonSerializerOptions();

// In-memory cache for status options (loaded on first query, rarely changes)
var cachedOptions = new List<(string Id, string Name, string Color)>();

var app = builder.Build();

app.MapGet("/",       () => Results.Ok(new { app = "widget-api", ok = true }));
app.MapGet("/health", () => Results.Ok(new { ok = true }));

// ── Query items ────────────────────────────────────────────────────────────
app.MapPost("/v1/widgets/{widgetId}/items/query", async (string widgetId) =>
{
    // 1. DB schema → status options
    var dbRes  = await http.GetAsync($"databases/{databaseId}");
    var dbDoc  = JsonDocument.Parse(await dbRes.Content.ReadAsStringAsync());

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
                Color: ToWidgetColor(o.GetProperty("color").GetString())
            ))
    ];

    // 2. Query pages
    var qRes  = await http.PostAsync($"databases/{databaseId}/query", null);
    var qDoc  = JsonDocument.Parse(await qRes.Content.ReadAsStringAsync());

    var items = qDoc.RootElement
        .GetProperty("results")
        .EnumerateArray()
        .Select(page =>
        {
            var props = page.GetProperty("properties");

            var titleArr  = props.GetProperty("Name").GetProperty("title").EnumerateArray().ToArray();
            var title     = titleArr.Length > 0
                ? titleArr[0].GetProperty("text").GetProperty("content").GetString() ?? ""
                : "(Untitled)";

            string statusId = "", statusName = "To-do";
            if (props.TryGetProperty("Status", out var sp) &&
                sp.TryGetProperty("status", out var so) &&
                so.ValueKind != JsonValueKind.Null)
            {
                statusId   = so.GetProperty("id").GetString()!;
                statusName = so.GetProperty("name").GetString()!;
            }

            return new
            {
                id             = page.GetProperty("id").GetString()!,
                title,
                isChecked      = statusName == "Done",
                statusId,
                status         = statusName,
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
        return Results.NotFound(new { ok = false, error = new { code = "NOT_FOUND", message = "item not found" } });

    var pageDoc = JsonDocument.Parse(await pageRes.Content.ReadAsStringAsync());
    var props   = pageDoc.RootElement.GetProperty("properties");

    string current = "To-do";
    if (props.TryGetProperty("Status", out var sp) &&
        sp.TryGetProperty("status", out var so) &&
        so.ValueKind != JsonValueKind.Null)
        current = so.GetProperty("name").GetString()!;

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
        return Results.BadRequest(new { ok = false, error = new { code = "BAD_STATUS", message = "invalid statusId" } });

    return await PatchStatusByName(itemId, optName);
});

app.Run();

// ── Helpers ────────────────────────────────────────────────────────────────
async Task<IResult> PatchStatusByName(string pageId, string statusName)
{
    var payload  = JsonSerializer.Serialize(
                       new { properties = new { Status = new { status = new { name = statusName } } } },
                       jsonOpts);
    var content  = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
    var patchRes = await http.PatchAsync($"pages/{pageId}", content);
    if (!patchRes.IsSuccessStatusCode)
        return Results.NotFound(new { ok = false, error = new { code = "NOT_FOUND", message = "page not found" } });

    var patchDoc = JsonDocument.Parse(await patchRes.Content.ReadAsStringAsync());
    var props    = patchDoc.RootElement.GetProperty("properties");

    string updatedId = "", updatedName = statusName;
    if (props.TryGetProperty("Status", out var sp) &&
        sp.TryGetProperty("status", out var so) &&
        so.ValueKind != JsonValueKind.Null)
    {
        updatedId   = so.GetProperty("id").GetString()!;
        updatedName = so.GetProperty("name").GetString()!;
    }

    return Results.Ok(new
    {
        ok   = true,
        data = new
        {
            id             = pageId,
            statusId       = updatedId,
            status         = updatedName,
            lastEditedTime = patchDoc.RootElement.GetProperty("last_edited_time").GetString()!
        }
    });
}

static string ToWidgetColor(string? notionColor) => notionColor switch
{
    "blue"   => "blue",
    "green"  => "green",
    "yellow" or "orange" => "yellow",
    "red"    or "pink"   => "red",
    _                    => "gray"
};

sealed class StatusSetBody { public string StatusId { get; set; } = ""; }
