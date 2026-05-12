using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ── Config persistence ─────────────────────────────────────────────────────
var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "NotionWidget", "config.json");

(string Token, string DatabaseId, string WorkspaceName) LoadConfig()
{
    try
    {
        if (!File.Exists(configPath)) return ("", "", "");
        var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        return (
            doc.RootElement.GetProperty("token").GetString()      ?? "",
            doc.RootElement.GetProperty("databaseId").GetString() ?? "",
            doc.RootElement.TryGetProperty("workspaceName", out var w) ? w.GetString() ?? "" : "");
    }
    catch { return ("", "", ""); }
}

void SaveConfig(string token, string databaseId, string workspaceName = "")
{
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath,
            JsonSerializer.Serialize(new { token, databaseId, workspaceName }));
    }
    catch { }
}

void ClearConfig()
{
    try { if (File.Exists(configPath)) File.Delete(configPath); }
    catch { }
}

// Static appsettings override saved config (dev convenience)
var saved = LoadConfig();
string authToken  = builder.Configuration["Notion:Token"]      is { Length: > 0 } t ? t : saved.Token;
string databaseId = builder.Configuration["Notion:DatabaseId"] is { Length: > 0 } d ? d : saved.DatabaseId;
string workspaceName = saved.WorkspaceName;

string oauthClientId = builder.Configuration["Notion:OAuth:ClientId"] ?? "";
string oauthClientSecret = builder.Configuration["Notion:OAuth:ClientSecret"] ?? "";
string oauthRedirectUri = builder.Configuration["Notion:OAuth:RedirectUri"]
    ?? "http://localhost:5183/auth/notion/callback";
string oauthAuthorizationUrl = builder.Configuration["Notion:OAuth:AuthorizationUrl"] ?? "";
var pendingOAuthStates = new HashSet<string>();

// ── Notion HTTP client ─────────────────────────────────────────────────────
var http = new HttpClient { BaseAddress = new Uri("https://api.notion.com/v1/") };
http.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

async Task<HttpResponseMessage> NotionSend(HttpMethod method, string path, HttpContent? body = null)
{
    var req = new HttpRequestMessage(method, path) { Content = body };
    if (!string.IsNullOrEmpty(authToken))
        req.Headers.Add("Authorization", $"Bearer {authToken}");
    return await http.SendAsync(req);
}

Task<HttpResponseMessage> NotionGet(string path)              => NotionSend(HttpMethod.Get,   path);
Task<HttpResponseMessage> NotionPost(string path, HttpContent? body = null)
                                                               => NotionSend(HttpMethod.Post,  path, body);
Task<HttpResponseMessage> NotionPatch(string path, HttpContent body) => NotionSend(HttpMethod.Patch, path, body);

var statusOrder   = new[] { "시작 전", "진행 중", "완료" };
var jsonOpts      = new JsonSerializerOptions();
var cachedOptions = new List<(string Id, string Name, string Color)>();

var app = builder.Build();

// ── Auth: check status ─────────────────────────────────────────────────────
app.MapGet("/auth/status", () =>
    Results.Ok(new
    {
        configured = !string.IsNullOrEmpty(authToken) && !string.IsNullOrEmpty(databaseId),
        workspaceName,
        databaseId
    }));

// ── Auth: start Notion OAuth ───────────────────────────────────────────────
app.MapPost("/auth/notion/start", () =>
{
    if (string.IsNullOrWhiteSpace(oauthClientId) ||
        string.IsNullOrWhiteSpace(oauthClientSecret))
    {
        return Results.BadRequest(new
        {
            ok = false,
            error = "Notion OAuth 설정이 필요합니다. Notion:OAuth:ClientId와 ClientSecret을 설정해주세요."
        });
    }

    var state = Guid.NewGuid().ToString("N");
    pendingOAuthStates.Add(state);

    var authUrl = string.IsNullOrWhiteSpace(oauthAuthorizationUrl)
        ? "https://api.notion.com/v1/oauth/authorize"
        : oauthAuthorizationUrl;

    var separator = authUrl.Contains('?') ? "&" : "?";
    var url = authUrl + separator +
        "client_id=" + Uri.EscapeDataString(oauthClientId) +
        "&response_type=code" +
        "&owner=user" +
        "&redirect_uri=" + Uri.EscapeDataString(oauthRedirectUri) +
        "&state=" + Uri.EscapeDataString(state);

    return Results.Ok(new { ok = true, data = new { authUrl = url } });
});

// ── Auth: Notion OAuth callback ────────────────────────────────────────────
app.MapGet("/auth/notion/callback", async ([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error) =>
{
    if (!string.IsNullOrWhiteSpace(error))
        return Results.Text("Notion 연결이 취소되었거나 실패했습니다: " + error, "text/plain; charset=utf-8");

    if (string.IsNullOrWhiteSpace(code) ||
        string.IsNullOrWhiteSpace(state) ||
        !pendingOAuthStates.Remove(state))
        return Results.BadRequest("올바르지 않은 OAuth 요청입니다.");

    var tokenResult = await ExchangeOAuthCodeAsync(code);
    if (!tokenResult.Ok)
        return Results.Text(tokenResult.Error, "text/plain; charset=utf-8", statusCode: 400);

    var dbResult = await FindOrCreateWidgetDatabaseAsync(tokenResult.AccessToken);
    if (!dbResult.Ok)
        return Results.Text(dbResult.Error, "text/plain; charset=utf-8", statusCode: 400);

    authToken = tokenResult.AccessToken;
    databaseId = dbResult.DatabaseId.Replace("-", "");
    workspaceName = tokenResult.WorkspaceName;
    SaveConfig(authToken, databaseId, workspaceName);

    return Results.Text(
        "Notion 연결이 완료되었습니다. 이 창을 닫고 Notion Widget으로 돌아가세요.",
        "text/plain; charset=utf-8");
});

// ── Auth: configure (legacy/dev: token + databaseId) ──────────────────────
app.MapPost("/auth/configure", async ([FromBody] ConfigureBody body) =>
{
    if (string.IsNullOrWhiteSpace(body.Token) || string.IsNullOrWhiteSpace(body.DatabaseId))
        return Results.BadRequest(new { ok = false, error = "토큰과 데이터베이스 ID를 모두 입력해주세요." });

    var testReq = new HttpRequestMessage(HttpMethod.Get,
        "databases/" + body.DatabaseId.Replace("-", "").Trim());
    testReq.Headers.Add("Authorization", $"Bearer {body.Token.Trim()}");
    var testRes = await http.SendAsync(testReq);

    if (!testRes.IsSuccessStatusCode)
    {
        var errBody = await testRes.Content.ReadAsStringAsync();
        string hint = testRes.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? "토큰이 올바르지 않습니다."
            : testRes.StatusCode == System.Net.HttpStatusCode.NotFound
                ? "데이터베이스를 찾을 수 없습니다. ID를 확인하거나 데이터베이스에 통합을 연결했는지 확인하세요."
                : "Notion 연결에 실패했습니다. (" + (int)testRes.StatusCode + ")";
        return Results.BadRequest(new { ok = false, error = hint });
    }

    authToken  = body.Token.Trim();
    databaseId = body.DatabaseId.Replace("-", "").Trim();
    workspaceName = "";
    SaveConfig(authToken, databaseId, workspaceName);

    return Results.Ok(new { ok = true });
});

// ── Auth: logout (clear saved credentials) ────────────────────────────────
app.MapPost("/auth/logout", () =>
{
    authToken  = "";
    databaseId = "";
    workspaceName = "";
    ClearConfig();
    return Results.Ok(new { ok = true });
});

// ── Health ─────────────────────────────────────────────────────────────────
app.MapGet("/",       () => Results.Ok(new { app = "widget-api", ok = true }));
app.MapGet("/health", () => Results.Ok(new { ok = true }));

// ── Query items ────────────────────────────────────────────────────────────
app.MapPost("/v1/widgets/{widgetId}/items/query", async (string widgetId) =>
{
    try
    {
        if (string.IsNullOrEmpty(databaseId))
            return Results.BadRequest(new { ok = false, error = "데이터베이스가 설정되지 않았습니다. Notion 연결을 완료해주세요." });

        var dbRes = await NotionGet($"databases/{databaseId}");
        if (!dbRes.IsSuccessStatusCode)
            return Results.BadRequest(new { ok = false, error = $"데이터베이스 조회 실패: {(int)dbRes.StatusCode}" });

        var dbDoc = JsonDocument.Parse(await dbRes.Content.ReadAsStringAsync());
        
        // Status 옵션 파싱
        if (!dbDoc.RootElement.TryGetProperty("properties", out var props) ||
            !props.TryGetProperty("Status", out var statusProp) ||
            !statusProp.TryGetProperty("status", out var statusObj) ||
            !statusObj.TryGetProperty("options", out var options))
        {
            return Results.BadRequest(new { ok = false, error = "데이터베이스에 'Status' 속성이 없습니다. 데이터베이스 구조를 확인해주세요." });
        }

        cachedOptions = options.EnumerateArray()
            .Select(o => (
                Id:    o.GetProperty("id").GetString()!,
                Name:  o.GetProperty("name").GetString()!,
                Color: MapNotionColor(o.GetProperty("color").GetString())))
            .ToList();

        var qRes = await NotionPost($"databases/{databaseId}/query");
        if (!qRes.IsSuccessStatusCode)
            return Results.BadRequest(new { ok = false, error = $"항목 조회 실패: {(int)qRes.StatusCode}" });

        var qDoc = JsonDocument.Parse(await qRes.Content.ReadAsStringAsync());

        var items = qDoc.RootElement.GetProperty("results").EnumerateArray()
            .Select(page =>
            {
                var itemProps = page.GetProperty("properties");
                var (statusId, statusName) = ParseStatus(itemProps);
                return new
                {
                    id             = page.GetProperty("id").GetString()!,
                    title          = ParseTitle(itemProps),
                    isChecked      = statusName == "완료",
                    statusId,
                    status         = statusName,
                    days           = ParseDays(itemProps),
                    note           = ParseNote(itemProps),
                    lastEditedTime = page.GetProperty("last_edited_time").GetString()!
                };
            })
            .ToList();

        var statusOptionsDto = cachedOptions.Select(o => new { id = o.Id, name = o.Name, color = o.Color });
        return Results.Ok(new { ok = true, data = new { items, statusOptions = statusOptionsDto } });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = $"조회 오류: {ex.Message}" });
    }
});

// ── Status: cycle to next ──────────────────────────────────────────────────
app.MapPost("/v1/widgets/{widgetId}/items/{itemId}/status/next",
    async (string widgetId, string itemId) =>
{
    var pageRes = await NotionGet($"pages/{itemId}");
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
        new { properties = new { Status = new { status = new { name = statusName } } } }, jsonOpts);
    var patchRes = await NotionPatch($"pages/{pageId}",
        new StringContent(payload, Encoding.UTF8, "application/json"));

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

// ── Parsers ───────────────────────────────────────────────────────────────
static string ParseTitle(JsonElement props, string propName = "Task")
{
    if (!props.TryGetProperty(propName, out var prop)) return "(Untitled)";
    var arr = prop.GetProperty("title").EnumerateArray().ToArray();
    return arr.Length > 0 ? arr[0].GetProperty("text").GetProperty("content").GetString() ?? "" : "(Untitled)";
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
{
    if (TryGetMultiSelectNames(props, "Day", out var days)) return days;
    if (TryGetMultiSelectNames(props, "Days", out days)) return days;
    return new List<string>();
}

static string ParseNote(JsonElement props)
{
    if (!props.TryGetProperty("Note", out var np) || !np.TryGetProperty("rich_text", out var rt)) return "";
    var arr = rt.EnumerateArray().ToArray();
    return arr.Length > 0 ? arr[0].GetProperty("text").GetProperty("content").GetString() ?? "" : "";
}

static string MapNotionColor(string? color) => color switch
{
    "blue"          => "blue",
    "green"         => "green",
    "yellow"
    or "orange"     => "yellow",
    "red" or "pink" => "red",
    _               => "gray"
};

static bool TryGetMultiSelectNames(JsonElement props, string propName, out List<string> names)
{
    names = new List<string>();
    if (!props.TryGetProperty(propName, out var prop) ||
        !prop.TryGetProperty("multi_select", out var multiSelect))
        return false;

    names = multiSelect.EnumerateArray()
        .Select(d => d.GetProperty("name").GetString()!)
        .ToList();
    return true;
}

async Task<(bool Ok, string AccessToken, string WorkspaceName, string Error)> ExchangeOAuthCodeAsync(string code)
{
    var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{oauthClientId}:{oauthClientSecret}"));
    using var req = new HttpRequestMessage(HttpMethod.Post, "oauth/token");
    req.Headers.Add("Authorization", $"Basic {basic}");
    req.Content = JsonContent.Create(new
    {
        grant_type = "authorization_code",
        code,
        redirect_uri = oauthRedirectUri
    });

    var res = await http.SendAsync(req);
    var text = await res.Content.ReadAsStringAsync();
    if (!res.IsSuccessStatusCode)
        return (false, "", "", "Notion OAuth 토큰 교환에 실패했습니다. (" + (int)res.StatusCode + ") " + text);

    var doc = JsonDocument.Parse(text).RootElement;
    var accessToken = doc.GetProperty("access_token").GetString() ?? "";
    var wsName = doc.TryGetProperty("workspace_name", out var w) ? w.GetString() ?? "" : "";
    return string.IsNullOrWhiteSpace(accessToken)
        ? (false, "", "", "Notion OAuth 응답에 access_token이 없습니다.")
        : (true, accessToken, wsName, "");
}

async Task<(bool Ok, string DatabaseId, string Error)> FindOrCreateWidgetDatabaseAsync(string accessToken)
{
    var existing = await FindAccessibleWidgetDatabaseAsync(accessToken);
    if (existing.Ok && !string.IsNullOrWhiteSpace(existing.DatabaseId))
        return (true, existing.DatabaseId, "");

    return await CreateWidgetDatabaseAsync(accessToken);
}

async Task<(bool Ok, string DatabaseId, string Error)> FindAccessibleWidgetDatabaseAsync(string accessToken)
{
    using var req = new HttpRequestMessage(HttpMethod.Post, "search");
    req.Headers.Add("Authorization", $"Bearer {accessToken}");
    req.Content = JsonContent.Create(new
    {
        filter = new { property = "object", value = "database" },
        page_size = 100
    });

    var res = await http.SendAsync(req);
    var text = await res.Content.ReadAsStringAsync();
    if (!res.IsSuccessStatusCode)
        return (false, "", "Notion에서 기존 데이터베이스를 찾는 데 실패했습니다. (" + (int)res.StatusCode + ") " + text);

    var results = JsonDocument.Parse(text).RootElement.GetProperty("results").EnumerateArray();
    foreach (var db in results)
    {
        if (IsWidgetDatabase(db))
            return (true, db.GetProperty("id").GetString() ?? "", "");
    }

    return (false, "", "");
}

static bool IsWidgetDatabase(JsonElement database)
{
    if (!database.TryGetProperty("properties", out var props)) return false;
    if (!HasPropertyType(props, "Task", "title")) return false;
    if (!HasPropertyType(props, "Status", "status")) return false;

    var hasDay = HasPropertyType(props, "Day", "multi_select") ||
                 HasPropertyType(props, "Days", "multi_select");
    if (!hasDay) return false;

    return !props.TryGetProperty("Note", out _) ||
           HasPropertyType(props, "Note", "rich_text");
}

static bool HasPropertyType(JsonElement props, string name, string type)
    => props.TryGetProperty(name, out var prop) &&
       prop.TryGetProperty("type", out var typeProp) &&
       typeProp.GetString() == type;

async Task<(bool Ok, string DatabaseId, string Error)> CreateWidgetDatabaseAsync(string accessToken)
{
    var parentResult = await FindAccessibleParentPageAsync(accessToken);
    if (!parentResult.Ok)
        return (false, "", parentResult.Error);

    using var req = new HttpRequestMessage(HttpMethod.Post, "databases");
    req.Headers.Add("Authorization", $"Bearer {accessToken}");
    req.Content = JsonContent.Create(new
    {
        parent = new { type = "page_id", page_id = parentResult.PageId },
        title = new[] { new { type = "text", text = new { content = "Notion Widget Tasks" } } },
        properties = new Dictionary<string, object>
        {
            ["Task"] = new { title = new { } },
            ["Status"] = new
            {
                status = new
                {
                    options = new[]
                    {
                        new { name = "시작 전", color = "gray" },
                        new { name = "진행 중", color = "blue" },
                        new { name = "완료", color = "green" }
                    }
                }
            },
            ["Day"] = new
            {
                multi_select = new
                {
                    options = new[]
                    {
                        new { name = "월요일", color = "red" },
                        new { name = "화요일", color = "orange" },
                        new { name = "수요일", color = "yellow" },
                        new { name = "목요일", color = "green" },
                        new { name = "금요일", color = "blue" },
                        new { name = "토요일", color = "purple" },
                        new { name = "일요일", color = "pink" }
                    }
                }
            },
            ["Note"] = new { rich_text = new { } }
        }
    });

    var res = await http.SendAsync(req);
    var text = await res.Content.ReadAsStringAsync();
    if (!res.IsSuccessStatusCode)
        return (false, "", "위젯용 Notion 데이터베이스 생성에 실패했습니다. (" + (int)res.StatusCode + ") " + text);

    var doc = JsonDocument.Parse(text).RootElement;
    return (true, doc.GetProperty("id").GetString() ?? "", "");
}

async Task<(bool Ok, string PageId, string Error)> FindAccessibleParentPageAsync(string accessToken)
{
    using var req = new HttpRequestMessage(HttpMethod.Post, "search");
    req.Headers.Add("Authorization", $"Bearer {accessToken}");
    req.Content = JsonContent.Create(new
    {
        filter = new { property = "object", value = "page" },
        page_size = 1
    });

    var res = await http.SendAsync(req);
    var text = await res.Content.ReadAsStringAsync();
    if (!res.IsSuccessStatusCode)
        return (false, "", "Notion에서 접근 가능한 페이지를 찾는 데 실패했습니다. (" + (int)res.StatusCode + ") " + text);

    var results = JsonDocument.Parse(text).RootElement.GetProperty("results").EnumerateArray().ToArray();
    if (results.Length == 0)
        return (false, "", "Notion 승인 화면에서 데이터베이스를 만들 부모 페이지를 하나 이상 선택해주세요.");

    return (true, results[0].GetProperty("id").GetString() ?? "", "");
}

sealed class ConfigureBody { public string Token { get; set; } = ""; public string DatabaseId { get; set; } = ""; }
sealed class StatusSetBody  { public string StatusId { get; set; } = ""; }
