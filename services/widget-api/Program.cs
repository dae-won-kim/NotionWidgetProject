using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// ---- 더미 상태 옵션 (DB 옵션 순서 = 순환 순서) ----
var statusOptions = new List<(string Id, string Name, string Color)>
{
    ("opt_a", "To-do", "blue"),
    ("opt_b", "In progress", "yellow"),
    ("opt_c", "Done", "green"),
};

// ---- 더미 아이템 저장소 (메모리) ----
var items = new Dictionary<string, DummyItem>
{
    ["page1"] = new DummyItem { Id="page1", Title="Complete the Galle project", StatusId="opt_a", IsChecked=false },
    ["page2"] = new DummyItem { Id="page2", Title="Go to the gym on Tuesday", StatusId="opt_c", IsChecked=true },
    ["page3"] = new DummyItem { Id="page3", Title="Test ㅁㄴㅊㅁㄴA", StatusId="opt_a", IsChecked=false },
    ["page4"] = new DummyItem { Id="page4", Title="Test 123 b", StatusId="opt_c", IsChecked=true },
    ["page5"] = new DummyItem { Id="page5", Title="Party TIME", StatusId="opt_c", IsChecked=false },
};

app.MapGet("/", () => Results.Ok(new { app = "widget-api", ok = true }));
app.MapGet("/health", () => Results.Ok(new { ok = true }));

// (1) 조회: 위젯이 표시할 리스트
app.MapPost("/v1/widgets/{widgetId}/items/query", (string widgetId) =>
{
    var data = new
    {
        items = items.Values.Select(x => new
        {
            id = x.Id,
            title = x.Title,
            isChecked = x.IsChecked,
            statusId = x.StatusId,
            status = statusOptions.First(o => o.Id == x.StatusId).Name,
            lastEditedTime = x.LastEditedTime.ToString("o")
        }),
        statusOptions = statusOptions.Select(o => new { id = o.Id, name = o.Name, color = o.Color })
    };

    return Results.Ok(new { ok = true, data });
});

// (2) 좌클릭: 다음 status로 순환
app.MapPost("/v1/widgets/{widgetId}/items/{itemId}/status/next",
    (string widgetId, string itemId) =>
{
    if (!items.TryGetValue(itemId, out var item))
        return Results.NotFound(new { ok = false, error = new { code="NOT_FOUND", message="item not found" } });

    var idx = statusOptions.FindIndex(o => o.Id == item.StatusId);
    if (idx < 0) idx = 0;
    var next = statusOptions[(idx + 1) % statusOptions.Count];

    item.StatusId = next.Id;
    item.LastEditedTime = DateTimeOffset.Now;

    var data = new
    {
        id = item.Id,
        statusId = item.StatusId,
        status = next.Name,
        lastEditedTime = item.LastEditedTime.ToString("o")
    };

    return Results.Ok(new { ok = true, data });
});

// (3) 우클릭: statusId로 지정
app.MapMethods("/v1/widgets/{widgetId}/items/{itemId}/status", new[] { "PATCH" },
    ([FromRoute] string widgetId, [FromRoute] string itemId, [FromBody] StatusSetBody body) =>
{
    if (!items.TryGetValue(itemId, out var item))
        return Results.NotFound(new { ok = false, error = new { code="NOT_FOUND", message="item not found" } });

    if (!statusOptions.Any(o => o.Id == body.StatusId))
        return Results.BadRequest(new { ok = false, error = new { code="BAD_STATUS", message="invalid statusId" } });

    item.StatusId = body.StatusId;
    item.LastEditedTime = DateTimeOffset.Now;

    var name = statusOptions.First(o => o.Id == item.StatusId).Name;

    var data = new
    {
        id = item.Id,
        statusId = item.StatusId,
        status = name,
        lastEditedTime = item.LastEditedTime.ToString("o")
    };

    return Results.Ok(new { ok = true, data });
});

app.Run();

// ---- 내부 모델 ----
sealed class DummyItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsChecked { get; set; }
    public string StatusId { get; set; } = "opt_a";
    public DateTimeOffset LastEditedTime { get; set; } = DateTimeOffset.Now;
}

sealed class StatusSetBody
{
    public string StatusId { get; set; } = "";
}

static class ListExt
{
    public static int FindIndex<T>(this List<T> list, Func<T, bool> pred)
    {
        for (int i = 0; i < list.Count; i++)
            if (pred(list[i])) return i;
        return -1;
    }
}
