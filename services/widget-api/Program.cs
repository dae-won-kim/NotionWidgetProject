using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var configuredNotionToken = builder.Configuration["Notion:Token"];
var configuredDatabaseId = builder.Configuration["Notion:DatabaseId"];
var oauthClientId = builder.Configuration["Notion:OAuth:ClientId"];
var oauthClientSecret = builder.Configuration["Notion:OAuth:ClientSecret"];
var oauthRedirectUri = builder.Configuration["Notion:OAuth:RedirectUri"];
var configuredAuthorizationUrl = builder.Configuration["Notion:OAuth:AuthorizationUrl"];

var authStorePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "NotionWidget",
    "notion-auth.json");
var dayStatusStorePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "NotionWidget",
    "day-status-overrides.json");

var http = new HttpClient { BaseAddress = new Uri("https://api.notion.com/v1/") };
var statusOrder = new[] { "시작 전", "진행 중", "완료" };
var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var cachedOptions = new List<(string Id, string Name, string Color)>();
var oauthState = "";
const string DailyDatabaseTitle = "Daily";
const string DailyDateProperty = "DATE";
const string DailyDayProperty = "DAY";
const string DailyTaskRelationProperty = "Task DataBase";
const string DailyTitleProperty = "Note";

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { app = "widget-api", ok = true }));
app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapGet("/auth/notion/status", () =>
{
    var token = GetAccessToken();
    var databaseId = GetDatabaseId();

    return Results.Ok(new
    {
        ok = true,
        data = new
        {
            authenticated = !string.IsNullOrWhiteSpace(token),
            hasDatabaseId = !string.IsNullOrWhiteSpace(databaseId),
            authUrl = "/auth/notion/start"
        }
    });
});

app.MapGet("/auth/notion/start", () =>
{
    if (string.IsNullOrWhiteSpace(oauthClientId) || string.IsNullOrWhiteSpace(oauthRedirectUri))
    {
        return Results.Json(new
        {
            ok = false,
            error = new
            {
                code = "OAUTH_NOT_CONFIGURED",
                message = "Notion OAuth ClientId and RedirectUri are required."
            }
        }, statusCode: StatusCodes.Status500InternalServerError);
    }

    oauthState = Guid.NewGuid().ToString("N");
    var authorizationUrl = BuildAuthorizationUrl(oauthState);
    return Results.Redirect(authorizationUrl);
});

app.MapGet("/auth/notion/callback", async (HttpContext context) =>
{
    var error = context.Request.Query["error"].ToString();
    if (!string.IsNullOrWhiteSpace(error))
    {
        return Html($"Notion authorization was cancelled or denied: {error}");
    }

    var code = context.Request.Query["code"].ToString();
    if (string.IsNullOrWhiteSpace(code))
    {
        return Html("Notion authorization failed: missing code.");
    }

    var state = context.Request.Query["state"].ToString();
    if (!string.IsNullOrWhiteSpace(oauthState) &&
        !string.Equals(state, oauthState, StringComparison.Ordinal))
    {
        return Html("Notion authorization failed: invalid state.");
    }

    if (string.IsNullOrWhiteSpace(oauthClientId) ||
        string.IsNullOrWhiteSpace(oauthClientSecret) ||
        string.IsNullOrWhiteSpace(oauthRedirectUri))
    {
        return Html("Notion OAuth is not configured. Set ClientId, ClientSecret, and RedirectUri.");
    }

    var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "oauth/token");
    var basicCredential = Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"{oauthClientId}:{oauthClientSecret}"));
    tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicCredential);
    tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    tokenRequest.Content = JsonContent.Create(new
    {
        grant_type = "authorization_code",
        code,
        redirect_uri = oauthRedirectUri
    });

    var tokenResponse = await http.SendAsync(tokenRequest);
    var tokenText = await tokenResponse.Content.ReadAsStringAsync();
    if (!tokenResponse.IsSuccessStatusCode)
    {
        return Html($"Notion token exchange failed ({(int)tokenResponse.StatusCode}): {tokenText}");
    }

    using var tokenDoc = JsonDocument.Parse(tokenText);
    var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
    var refreshToken = tokenDoc.RootElement.TryGetProperty("refresh_token", out var refreshTokenElement)
        ? refreshTokenElement.GetString()
        : null;

    if (string.IsNullOrWhiteSpace(accessToken))
    {
        return Html("Notion token exchange failed: access_token was missing.");
    }

    var authStore = LoadAuthStore() ?? new NotionAuthStore();
    authStore.AccessToken = accessToken;
    authStore.RefreshToken = refreshToken;
    authStore.DatabaseId = configuredDatabaseId;
    authStore.UpdatedAt = DateTimeOffset.UtcNow;

    SaveAuthStore(authStore);

    var foundDatabaseId = await FindChecklistDatabaseIdAsync(accessToken);
    if (!string.IsNullOrWhiteSpace(foundDatabaseId))
    {
        authStore.DatabaseId = foundDatabaseId;
        authStore.UpdatedAt = DateTimeOffset.UtcNow;
        SaveAuthStore(authStore);

        return Html("Notion authorization completed. Checklist database was found automatically. You can return to the widget.");
    }

    return Html("Notion authorization completed, but no shared database with Task and Status properties was found. Share the checklist database during authorization, then open /auth/notion/start again.");
});

// ── Query items ────────────────────────────────────────────────────────────
app.MapPost("/v1/widgets/{widgetId}/items/query", async (string widgetId, [FromBody] QueryItemsBody? body) =>
{
    var token = GetAccessToken();
    if (string.IsNullOrWhiteSpace(token))
    {
        return AuthRequired();
    }

    var databaseId = await GetOrFindDatabaseIdAsync(token);
    if (string.IsNullOrWhiteSpace(databaseId))
    {
        return Results.Json(new
        {
            ok = false,
            error = new
            {
                code = "DATABASE_NOT_FOUND",
                message = "No Notion database with Task and Status properties was found. Authorize Notion and select the checklist database."
            }
        }, statusCode: StatusCodes.Status404NotFound);
    }

    var dbRes = await SendNotionAsync(HttpMethod.Get, $"databases/{databaseId}", token);
    if (!dbRes.IsSuccessStatusCode)
    {
        if (!string.IsNullOrWhiteSpace(configuredDatabaseId))
        {
            return await NotionFailureAsync(dbRes, "DATABASE_FETCH_FAILED");
        }

        var alternateDatabaseId = await FindChecklistDatabaseIdAsync(token);

        if (string.IsNullOrWhiteSpace(alternateDatabaseId))
        {
            return await NotionFailureAsync(dbRes, "DATABASE_FETCH_FAILED");
        }

        SaveSelectedDatabaseId(token, alternateDatabaseId);
        databaseId = alternateDatabaseId;

        dbRes = await SendNotionAsync(HttpMethod.Get, $"databases/{databaseId}", token);
        if (!dbRes.IsSuccessStatusCode)
        {
            return await NotionFailureAsync(dbRes, "DATABASE_FETCH_FAILED");
        }
    }

    using var dbDoc = JsonDocument.Parse(await dbRes.Content.ReadAsStringAsync());
    if (!TryGetStatusOptions(dbDoc.RootElement, out cachedOptions))
    {
        return SchemaMismatch("The selected database must have a Status property of type status.");
    }

    var queryRes = await SendNotionAsync(HttpMethod.Post, $"databases/{databaseId}/query", token, new { });
    if (!queryRes.IsSuccessStatusCode)
    {
        return await NotionFailureAsync(queryRes, "DATABASE_QUERY_FAILED");
    }

    using var queryDoc = JsonDocument.Parse(await queryRes.Content.ReadAsStringAsync());
    if (!queryDoc.RootElement.TryGetProperty("results", out var results))
    {
        return SchemaMismatch("The Notion query response did not contain results.");
    }

    var resultPages = results.EnumerateArray().Select(page => page.Clone()).ToArray();
    if (resultPages.Length == 0 && string.IsNullOrWhiteSpace(configuredDatabaseId))
    {
        var alternateDatabaseId = await FindChecklistDatabaseIdAsync(
            token,
            currentDatabaseId: databaseId,
            requireItems: true);

        if (!string.IsNullOrWhiteSpace(alternateDatabaseId) &&
            !string.Equals(alternateDatabaseId, databaseId, StringComparison.OrdinalIgnoreCase))
        {
            SaveSelectedDatabaseId(token, alternateDatabaseId);
            databaseId = alternateDatabaseId;

            dbRes = await SendNotionAsync(HttpMethod.Get, $"databases/{databaseId}", token);
            if (!dbRes.IsSuccessStatusCode)
            {
                return await NotionFailureAsync(dbRes, "DATABASE_FETCH_FAILED");
            }

            using var alternateDbDoc = JsonDocument.Parse(await dbRes.Content.ReadAsStringAsync());
            if (!TryGetStatusOptions(alternateDbDoc.RootElement, out cachedOptions))
            {
                return SchemaMismatch("The selected database must have a Status property of type status.");
            }

            queryRes = await SendNotionAsync(HttpMethod.Post, $"databases/{databaseId}/query", token, new { });
            if (!queryRes.IsSuccessStatusCode)
            {
                return await NotionFailureAsync(queryRes, "DATABASE_QUERY_FAILED");
            }

            using var alternateQueryDoc = JsonDocument.Parse(await queryRes.Content.ReadAsStringAsync());
            if (!alternateQueryDoc.RootElement.TryGetProperty("results", out var alternateResults))
            {
                return SchemaMismatch("The Notion query response did not contain results.");
            }

            resultPages = alternateResults.EnumerateArray().Select(page => page.Clone()).ToArray();
        }
    }

    var selectedDay = body?.Day?.Trim() ?? "";
    var dailyDatabaseId = string.IsNullOrWhiteSpace(selectedDay)
        ? null
        : await FindDailyDatabaseIdAsync(token);

    if (!string.IsNullOrWhiteSpace(selectedDay) &&
        !string.IsNullOrWhiteSpace(dailyDatabaseId))
    {
        var dailyDbRes = await SendNotionAsync(HttpMethod.Get, $"databases/{dailyDatabaseId}", token);
        if (!dailyDbRes.IsSuccessStatusCode)
        {
            return await NotionFailureAsync(dailyDbRes, "DAILY_DATABASE_FETCH_FAILED");
        }

        using var dailyDbDoc = JsonDocument.Parse(await dailyDbRes.Content.ReadAsStringAsync());
        if (!TryGetStatusOptions(dailyDbDoc.RootElement, out cachedOptions))
        {
            return SchemaMismatch("The Daily database must have a Status property of type status.");
        }

        var targetDate = GetDateForDayInCurrentWeek(selectedDay);
        await EnsureDailyRowsAsync(token, dailyDatabaseId, resultPages, selectedDay, targetDate);

        var dailyRowsRes = await QueryDailyRowsAsync(token, dailyDatabaseId, selectedDay, targetDate);
        if (!dailyRowsRes.IsSuccessStatusCode)
        {
            return await NotionFailureAsync(dailyRowsRes, "DAILY_QUERY_FAILED");
        }

        using var dailyRowsDoc = JsonDocument.Parse(await dailyRowsRes.Content.ReadAsStringAsync());
        if (!dailyRowsDoc.RootElement.TryGetProperty("results", out var dailyResults))
        {
            return SchemaMismatch("The Daily query response did not contain results.");
        }

        var sourcePagesById = resultPages.ToDictionary(
            page => page.GetProperty("id").GetString()!,
            page => page);
        var dailyItems = dailyResults.EnumerateArray()
            .Select(row => BuildDailyItemDto(row, sourcePagesById, selectedDay))
            .Where(item => item is not null)
            .ToList();

        var dailyStatusOptionsDto = cachedOptions.Select(option => new
        {
            id = option.Id,
            name = option.Name,
            color = option.Color
        });

        return Results.Ok(new { ok = true, data = new { items = dailyItems, statusOptions = dailyStatusOptionsDto } });
    }

    var dayStatusOverrides = LoadDayStatusOverrides();
    var items = resultPages
        .SelectMany(page => BuildItemDtos(page, dayStatusOverrides, selectedDay))
        .ToList();

    var statusOptionsDto = cachedOptions.Select(option => new
    {
        id = option.Id,
        name = option.Name,
        color = option.Color
    });

    return Results.Ok(new { ok = true, data = new { items, statusOptions = statusOptionsDto } });
});

// ── Status: cycle to next ──────────────────────────────────────────────────
app.MapPost("/v1/widgets/{widgetId}/items/{itemId}/status/next",
    async (string widgetId, string itemId) =>
{
    var token = GetAccessToken();
    if (string.IsNullOrWhiteSpace(token))
    {
        return AuthRequired();
    }

    var itemRef = ParseItemId(itemId);
    var pageRes = await SendNotionAsync(HttpMethod.Get, $"pages/{itemRef.PageId}", token);
    if (!pageRes.IsSuccessStatusCode)
    {
        return await NotionFailureAsync(pageRes, "PAGE_FETCH_FAILED");
    }

    var (_, current) = ParseStatus(
        JsonDocument.Parse(await pageRes.Content.ReadAsStringAsync())
                    .RootElement.GetProperty("properties"));
    if (!string.IsNullOrWhiteSpace(itemRef.Day) &&
        TryGetDayStatusOverride(itemRef.PageId, itemRef.Day, out var dayStatus))
    {
        current = dayStatus.Status ?? current;
    }

    var idx = Array.IndexOf(statusOrder, current);
    var next = statusOrder[idx < 0 ? 0 : (idx + 1) % statusOrder.Length];

    if (!string.IsNullOrWhiteSpace(itemRef.Day))
    {
        var option = cachedOptions.FirstOrDefault(statusOption => statusOption.Name == next);
        return SaveDayStatusAndReturn(itemRef.PageId, itemRef.Day, option.Id, next);
    }

    return await PatchStatusByName(itemRef.PageId, next, token);
});

// ── Status: set specific ───────────────────────────────────────────────────
app.MapMethods("/v1/widgets/{widgetId}/items/{itemId}/status", new[] { "PATCH" },
    async ([FromRoute] string widgetId, [FromRoute] string itemId, [FromBody] StatusSetBody body) =>
{
    var (_, optName, _) = cachedOptions.FirstOrDefault(option => option.Id == body.StatusId);
    if (string.IsNullOrEmpty(optName))
    {
        return Results.BadRequest(new { ok = false, error = new { code = "BAD_STATUS" } });
    }

    var token = GetAccessToken();
    if (string.IsNullOrWhiteSpace(token))
    {
        return AuthRequired();
    }

    var itemRef = ParseItemId(itemId);
    if (!string.IsNullOrWhiteSpace(itemRef.Day))
    {
        return SaveDayStatusAndReturn(itemRef.PageId, itemRef.Day, body.StatusId, optName);
    }

    return await PatchStatusByName(itemRef.PageId, optName, token);
});

app.Run();

// ── Auth helpers ───────────────────────────────────────────────────────────
string? GetAccessToken()
{
    if (!string.IsNullOrWhiteSpace(configuredNotionToken))
    {
        return configuredNotionToken;
    }

    return LoadAuthStore()?.AccessToken;
}

string? GetDatabaseId()
{
    if (!string.IsNullOrWhiteSpace(configuredDatabaseId))
    {
        return configuredDatabaseId;
    }

    return LoadAuthStore()?.DatabaseId;
}

async Task<string?> GetOrFindDatabaseIdAsync(string token)
{
    var existingDatabaseId = GetDatabaseId();
    if (!string.IsNullOrWhiteSpace(existingDatabaseId))
    {
        return existingDatabaseId;
    }

    var foundDatabaseId = await FindChecklistDatabaseIdAsync(token);
    if (string.IsNullOrWhiteSpace(foundDatabaseId))
    {
        return null;
    }

    SaveSelectedDatabaseId(token, foundDatabaseId);

    return foundDatabaseId;
}

void SaveSelectedDatabaseId(string token, string databaseId)
{
    var authStore = LoadAuthStore() ?? new NotionAuthStore();
    authStore.AccessToken = token;
    authStore.DatabaseId = databaseId;
    authStore.UpdatedAt = DateTimeOffset.UtcNow;
    SaveAuthStore(authStore);
}

string BuildAuthorizationUrl(string state)
{
    var authorizationUrl = string.IsNullOrWhiteSpace(configuredAuthorizationUrl)
        ? "https://api.notion.com/v1/oauth/authorize"
        : configuredAuthorizationUrl;

    var separator = authorizationUrl.Contains('?') ? '&' : '?';
    var requiredQuery = string.Join("&", new[]
    {
        $"owner=user",
        $"client_id={Uri.EscapeDataString(oauthClientId!)}",
        $"redirect_uri={Uri.EscapeDataString(oauthRedirectUri!)}",
        $"response_type=code",
        $"state={Uri.EscapeDataString(state)}"
    });

    return authorizationUrl.Contains("client_id=", StringComparison.OrdinalIgnoreCase)
        ? $"{authorizationUrl}{separator}state={Uri.EscapeDataString(state)}"
        : $"{authorizationUrl}{separator}{requiredQuery}";
}

NotionAuthStore? LoadAuthStore()
{
    if (!File.Exists(authStorePath))
    {
        return null;
    }

    try
    {
        return JsonSerializer.Deserialize<NotionAuthStore>(
            File.ReadAllText(authStorePath),
            jsonOpts);
    }
    catch
    {
        return null;
    }
}

void SaveAuthStore(NotionAuthStore authStore)
{
    Directory.CreateDirectory(Path.GetDirectoryName(authStorePath)!);
    File.WriteAllText(authStorePath, JsonSerializer.Serialize(authStore, jsonOpts));
}

Dictionary<string, DayStatusOverride> LoadDayStatusOverrides()
{
    if (!File.Exists(dayStatusStorePath))
    {
        return new Dictionary<string, DayStatusOverride>();
    }

    try
    {
        return JsonSerializer.Deserialize<Dictionary<string, DayStatusOverride>>(
                   File.ReadAllText(dayStatusStorePath),
                   jsonOpts)
               ?? new Dictionary<string, DayStatusOverride>();
    }
    catch
    {
        return new Dictionary<string, DayStatusOverride>();
    }
}

void SaveDayStatusOverrides(Dictionary<string, DayStatusOverride> overrides)
{
    Directory.CreateDirectory(Path.GetDirectoryName(dayStatusStorePath)!);
    File.WriteAllText(dayStatusStorePath, JsonSerializer.Serialize(overrides, jsonOpts));
}

bool TryGetDayStatusOverride(string pageId, string day, out DayStatusOverride status)
    => LoadDayStatusOverrides().TryGetValue(DayStatusKey(pageId, day), out status!);

IResult SaveDayStatusAndReturn(string pageId, string day, string statusId, string statusName)
{
    var updatedAt = DateTimeOffset.UtcNow;
    var overrides = LoadDayStatusOverrides();
    overrides[DayStatusKey(pageId, day)] = new DayStatusOverride
    {
        StatusId = statusId,
        Status = statusName,
        UpdatedAt = updatedAt
    };
    SaveDayStatusOverrides(overrides);

    return Results.Ok(new
    {
        ok = true,
        data = new
        {
            id = BuildItemId(pageId, day),
            statusId,
            status = statusName,
            lastEditedTime = updatedAt.ToString("O")
        }
    });
}

static string DayStatusKey(string pageId, string day) => $"{pageId}|{day}";

static string BuildItemId(string pageId, string day) => $"{pageId}::{DayCode(day)}";

static (string PageId, string? Day) ParseItemId(string itemId)
{
    var parts = itemId.Split("::", 2, StringSplitOptions.None);
    return parts.Length == 2
        ? (parts[0], DayFromCode(parts[1]))
        : (itemId, null);
}

static string DayCode(string day) => day switch
{
    "월요일" => "mon",
    "화요일" => "tue",
    "수요일" => "wed",
    "목요일" => "thu",
    "금요일" => "fri",
    "토요일" => "sat",
    "일요일" => "sun",
    _ => day
};

static string DayFromCode(string code) => code switch
{
    "mon" => "월요일",
    "tue" => "화요일",
    "wed" => "수요일",
    "thu" => "목요일",
    "fri" => "금요일",
    "sat" => "토요일",
    "sun" => "일요일",
    _ => code
};

IResult AuthRequired()
    => Results.Json(new
    {
        ok = false,
        error = new
        {
            code = "NOTION_AUTH_REQUIRED",
            message = "Open /auth/notion/start to connect Notion.",
            authUrl = "/auth/notion/start"
        }
    }, statusCode: StatusCodes.Status401Unauthorized);

IResult Html(string body)
    => Results.Content(
        $"""
        <!doctype html>
        <html lang="ko">
        <head><meta charset="utf-8"><title>Notion Widget</title></head>
        <body><main style="font-family: system-ui; max-width: 720px; margin: 48px auto; line-height: 1.6;">{System.Net.WebUtility.HtmlEncode(body)}</main></body>
        </html>
        """,
        "text/html",
        Encoding.UTF8);

// ── Notion helpers ─────────────────────────────────────────────────────────
async Task<HttpResponseMessage> SendNotionAsync(
    HttpMethod method,
    string path,
    string token,
    object? body = null)
{
    var request = new HttpRequestMessage(method, path);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    request.Headers.Add("Notion-Version", "2022-06-28");
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    if (body is not null)
    {
        request.Content = JsonContent.Create(body);
    }

    return await http.SendAsync(request);
}

async Task<IResult> NotionFailureAsync(HttpResponseMessage response, string fallbackCode)
{
    var responseText = await response.Content.ReadAsStringAsync();
    var code = fallbackCode;
    var message = responseText;

    try
    {
        using var errorDoc = JsonDocument.Parse(responseText);
        if (errorDoc.RootElement.TryGetProperty("code", out var codeElement))
        {
            code = codeElement.GetString() ?? fallbackCode;
        }

        if (errorDoc.RootElement.TryGetProperty("message", out var messageElement))
        {
            message = messageElement.GetString() ?? responseText;
        }
    }
    catch
    {
        // Keep the original response text when Notion returns a non-JSON body.
    }

    return Results.Json(new
    {
        ok = false,
        error = new
        {
            code,
            message,
            notionStatusCode = (int)response.StatusCode
        }
    }, statusCode: (int)response.StatusCode);
}

IResult SchemaMismatch(string message)
    => Results.Json(new
    {
        ok = false,
        error = new
        {
            code = "NOTION_SCHEMA_MISMATCH",
            message
        }
    }, statusCode: StatusCodes.Status422UnprocessableEntity);

async Task<string?> FindChecklistDatabaseIdAsync(
    string token,
    string? currentDatabaseId = null,
    bool requireItems = false)
{
    var searchRes = await SendNotionAsync(HttpMethod.Post, "search", token, new
    {
        filter = new
        {
            property = "object",
            value = "database"
        },
        page_size = 25
    });

    if (!searchRes.IsSuccessStatusCode)
    {
        return null;
    }

    using var searchDoc = JsonDocument.Parse(await searchRes.Content.ReadAsStringAsync());
    if (!searchDoc.RootElement.TryGetProperty("results", out var results))
    {
        return null;
    }

    string? fallbackDatabaseId = null;

    foreach (var result in results.EnumerateArray())
    {
        if (result.TryGetProperty("properties", out var properties) &&
            HasChecklistSchema(properties) &&
            result.TryGetProperty("id", out var id))
        {
            var databaseId = id.GetString();
            if (string.IsNullOrWhiteSpace(databaseId))
            {
                continue;
            }

            if (!string.Equals(databaseId, currentDatabaseId, StringComparison.OrdinalIgnoreCase))
            {
                var hasItems = await DatabaseHasAnyItemsAsync(token, databaseId);
                if (hasItems == true)
                {
                    return databaseId;
                }
            }

            fallbackDatabaseId ??= databaseId;
        }
    }

    return fallbackDatabaseId;
}

async Task<bool?> DatabaseHasAnyItemsAsync(string token, string databaseId)
{
    var queryRes = await SendNotionAsync(HttpMethod.Post, $"databases/{databaseId}/query", token, new
    {
        page_size = 1
    });

    if (!queryRes.IsSuccessStatusCode)
    {
        return null;
    }

    using var queryDoc = JsonDocument.Parse(await queryRes.Content.ReadAsStringAsync());
    return queryDoc.RootElement.TryGetProperty("results", out var results) &&
           results.EnumerateArray().Any();
}

static bool HasChecklistSchema(JsonElement properties)
{
    var hasTitle = false;
    var hasStatus = false;

    foreach (var property in properties.EnumerateObject())
    {
        if (!property.Value.TryGetProperty("type", out var type))
        {
            continue;
        }

        hasTitle |= type.GetString() == "title";
        hasStatus |= property.Name == "Status" && type.GetString() == "status";
    }

    return hasTitle && hasStatus;
}

static bool TryGetStatusOptions(
    JsonElement database,
    out List<(string Id, string Name, string Color)> statusOptions)
{
    statusOptions = new List<(string Id, string Name, string Color)>();

    if (!database.TryGetProperty("properties", out var properties) ||
        !properties.TryGetProperty("Status", out var statusProperty) ||
        !statusProperty.TryGetProperty("status", out var statusConfig) ||
        !statusConfig.TryGetProperty("options", out var options))
    {
        return false;
    }

    statusOptions = options.EnumerateArray()
        .Select(option => (
            Id: option.GetProperty("id").GetString()!,
            Name: option.GetProperty("name").GetString()!,
            Color: MapNotionColor(option.GetProperty("color").GetString())))
        .ToList();

    return true;
}

async Task<string?> FindDailyDatabaseIdAsync(string token)
{
    var searchRes = await SendNotionAsync(HttpMethod.Post, "search", token, new
    {
        query = DailyDatabaseTitle,
        filter = new
        {
            property = "object",
            value = "database"
        },
        page_size = 25
    });

    if (!searchRes.IsSuccessStatusCode)
    {
        return null;
    }

    using var searchDoc = JsonDocument.Parse(await searchRes.Content.ReadAsStringAsync());
    if (!searchDoc.RootElement.TryGetProperty("results", out var results))
    {
        return null;
    }

    foreach (var database in results.EnumerateArray())
    {
        var title = ParseDatabaseTitle(database);
        if (!string.Equals(title, DailyDatabaseTitle, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (database.TryGetProperty("properties", out var properties) &&
            HasDailySchema(properties) &&
            database.TryGetProperty("id", out var id))
        {
            return id.GetString();
        }
    }

    return null;
}

async Task EnsureDailyRowsAsync(
    string token,
    string dailyDatabaseId,
    IEnumerable<JsonElement> sourcePages,
    string selectedDay,
    DateOnly targetDate)
{
    var existingRowsRes = await QueryDailyRowsAsync(token, dailyDatabaseId, selectedDay, targetDate);
    if (!existingRowsRes.IsSuccessStatusCode)
    {
        return;
    }

    var existingSourceIds = new HashSet<string>();
    using (var existingDoc = JsonDocument.Parse(await existingRowsRes.Content.ReadAsStringAsync()))
    {
        if (existingDoc.RootElement.TryGetProperty("results", out var results))
        {
            foreach (var row in results.EnumerateArray())
            {
                var sourceId = ParseDailyRelationPageId(row);
                if (!string.IsNullOrWhiteSpace(sourceId))
                {
                    existingSourceIds.Add(sourceId);
                }
            }
        }
    }

    foreach (var sourcePage in sourcePages)
    {
        var props = sourcePage.GetProperty("properties");
        var days = ParseDays(props);
        if (!days.Contains(selectedDay))
        {
            continue;
        }

        var sourcePageId = sourcePage.GetProperty("id").GetString()!;
        if (existingSourceIds.Contains(sourcePageId))
        {
            continue;
        }

        await CreateDailyRowAsync(token, dailyDatabaseId, sourcePage, selectedDay, targetDate);
    }
}

async Task<HttpResponseMessage> QueryDailyRowsAsync(
    string token,
    string dailyDatabaseId,
    string selectedDay,
    DateOnly targetDate)
    => await SendNotionAsync(HttpMethod.Post, $"databases/{dailyDatabaseId}/query", token, new
    {
        filter = new
        {
            and = new object[]
            {
                new
                {
                    property = DailyDateProperty,
                    date = new { equals = targetDate.ToString("yyyy-MM-dd") }
                },
                new
                {
                    property = DailyDayProperty,
                    multi_select = new { contains = selectedDay }
                }
            }
        },
        page_size = 100
    });

async Task CreateDailyRowAsync(
    string token,
    string dailyDatabaseId,
    JsonElement sourcePage,
    string selectedDay,
    DateOnly targetDate)
{
    var props = sourcePage.GetProperty("properties");
    var sourcePageId = sourcePage.GetProperty("id").GetString()!;
    var title = ParseTitle(props);

    await SendNotionAsync(HttpMethod.Post, "pages", token, new
    {
        parent = new { database_id = dailyDatabaseId },
        properties = new Dictionary<string, object>
        {
            [DailyDateProperty] = new
            {
                date = new { start = targetDate.ToString("yyyy-MM-dd") }
            },
            [DailyDayProperty] = new
            {
                multi_select = new[] { new { name = selectedDay } }
            },
            [DailyTaskRelationProperty] = new
            {
                relation = new[] { new { id = sourcePageId } }
            },
            ["Status"] = new
            {
                status = new { name = "시작 전" }
            },
            [DailyTitleProperty] = new
            {
                title = new[]
                {
                    new
                    {
                        text = new { content = title }
                    }
                }
            }
        }
    });
}

object? BuildDailyItemDto(
    JsonElement dailyRow,
    Dictionary<string, JsonElement> sourcePagesById,
    string selectedDay)
{
    var sourcePageId = ParseDailyRelationPageId(dailyRow);
    if (string.IsNullOrWhiteSpace(sourcePageId) ||
        !sourcePagesById.TryGetValue(sourcePageId, out var sourcePage))
    {
        return null;
    }

    var sourceProps = sourcePage.GetProperty("properties");
    var dailyProps = dailyRow.GetProperty("properties");
    var (statusId, statusName) = ParseStatus(dailyProps);

    return BuildItemDto(
        dailyRow,
        dailyRow.GetProperty("id").GetString()!,
        ParseTitle(sourceProps),
        statusId,
        statusName,
        new List<string> { selectedDay },
        ParseNote(sourceProps),
        dailyRow.GetProperty("last_edited_time").GetString()!);
}

static bool HasDailySchema(JsonElement properties)
    => HasPropertyType(properties, DailyDateProperty, "date") &&
       HasPropertyType(properties, DailyDayProperty, "multi_select") &&
       HasPropertyType(properties, DailyTaskRelationProperty, "relation") &&
       HasPropertyType(properties, "Status", "status") &&
       HasPropertyType(properties, DailyTitleProperty, "title");

static bool HasPropertyType(JsonElement properties, string propertyName, string type)
    => properties.TryGetProperty(propertyName, out var property) &&
       property.TryGetProperty("type", out var propertyType) &&
       propertyType.GetString() == type;

static string? ParseDailyRelationPageId(JsonElement dailyRow)
{
    if (!dailyRow.TryGetProperty("properties", out var properties) ||
        !properties.TryGetProperty(DailyTaskRelationProperty, out var relationProperty) ||
        !relationProperty.TryGetProperty("relation", out var relations))
    {
        return null;
    }

    var relation = relations.EnumerateArray().FirstOrDefault();
    return relation.ValueKind == JsonValueKind.Object &&
           relation.TryGetProperty("id", out var id)
        ? id.GetString()
        : null;
}

static string ParseDatabaseTitle(JsonElement database)
{
    if (!database.TryGetProperty("title", out var title) ||
        title.ValueKind != JsonValueKind.Array)
    {
        return "";
    }

    return string.Concat(title.EnumerateArray()
        .Select(part => part.TryGetProperty("plain_text", out var text)
            ? text.GetString()
            : ""));
}

static DateOnly GetDateForDayInCurrentWeek(string selectedDay)
{
    var today = DateOnly.FromDateTime(DateTime.Today);
    var todayIndex = ((int)DateTime.Today.DayOfWeek + 6) % 7;
    var selectedIndex = selectedDay switch
    {
        "월요일" => 0,
        "화요일" => 1,
        "수요일" => 2,
        "목요일" => 3,
        "금요일" => 4,
        "토요일" => 5,
        "일요일" => 6,
        _ => todayIndex
    };

    return today.AddDays(selectedIndex - todayIndex);
}

IEnumerable<object> BuildItemDtos(
    JsonElement page,
    Dictionary<string, DayStatusOverride> dayStatusOverrides,
    string selectedDay)
{
    var pageId = page.GetProperty("id").GetString()!;
    var props = page.GetProperty("properties");
    var (baseStatusId, baseStatusName) = ParseStatus(props);
    var days = ParseDays(props);

    if (string.IsNullOrWhiteSpace(selectedDay))
    {
        yield return BuildItemDto(
            page,
            pageId,
            ParseTitle(props),
            baseStatusId,
            baseStatusName,
            days,
            ParseNote(props),
            page.GetProperty("last_edited_time").GetString()!);

        yield break;
    }

    if (days.Count == 0 || !days.Contains(selectedDay))
    {
        yield break;
    }

    var itemDay = days.Count > 0 ? selectedDay : "";
    var itemDays = days.Count > 0 ? new List<string> { selectedDay } : days;
    var itemId = string.IsNullOrWhiteSpace(itemDay)
        ? pageId
        : BuildItemId(pageId, itemDay);
    var statusId = baseStatusId;
    var statusName = baseStatusName;
    var lastEditedTime = page.GetProperty("last_edited_time").GetString()!;

    if (!string.IsNullOrWhiteSpace(itemDay) &&
        dayStatusOverrides.TryGetValue(DayStatusKey(pageId, itemDay), out var overrideStatus))
    {
        statusId = overrideStatus.StatusId ?? statusId;
        statusName = overrideStatus.Status ?? statusName;
        lastEditedTime = overrideStatus.UpdatedAt.ToString("O");
    }

    yield return BuildItemDto(
        page,
        itemId,
        ParseTitle(props),
        statusId,
        statusName,
        itemDays,
        ParseNote(props),
        lastEditedTime);
}

static object BuildItemDto(
    JsonElement page,
    string id,
    string title,
    string statusId,
    string statusName,
    List<string> days,
    string note,
    string lastEditedTime)
    => new
    {
        id,
        title,
        isChecked = statusName == "완료",
        statusId,
        status = statusName,
        days,
        note,
        lastEditedTime
    };

// ── PATCH helper ──────────────────────────────────────────────────────────
async Task<IResult> PatchStatusByName(string pageId, string statusName, string token)
{
    var payload = new
    {
        properties = new Dictionary<string, object>
        {
            ["Status"] = new
            {
                status = new { name = statusName }
            }
        }
    };

    var patchRes = await SendNotionAsync(HttpMethod.Patch, $"pages/{pageId}", token, payload);
    if (!patchRes.IsSuccessStatusCode)
    {
        return await NotionFailureAsync(patchRes, "PATCH_FAILED");
    }

    using var doc = JsonDocument.Parse(await patchRes.Content.ReadAsStringAsync());
    var props = doc.RootElement.GetProperty("properties");
    var (updatedId, updatedName) = ParseStatus(props, statusName);

    return Results.Ok(new
    {
        ok = true,
        data = new
        {
            id = pageId,
            statusId = updatedId,
            status = updatedName,
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
        ? ms.EnumerateArray().Select(day => day.GetProperty("name").GetString()!).ToList()
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
    "blue" => "blue",
    "green" => "green",
    "yellow"
    or "orange" => "yellow",
    "red" or "pink" => "red",
    _ => "gray"
};

sealed class StatusSetBody { public string StatusId { get; set; } = ""; }

sealed class QueryItemsBody { public string Day { get; set; } = ""; }

sealed class NotionAuthStore
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? DatabaseId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

sealed class DayStatusOverride
{
    public string? StatusId { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
