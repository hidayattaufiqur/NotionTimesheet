using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using NotionTimesheet.Models;

namespace NotionTimesheet.Services;

internal sealed class NotionApiException : Exception
{
    public NotionApiException(string message, int statusCode, string? bodyExcerpt)
        : base(message)
    {
        StatusCode = statusCode;
        BodyExcerpt = bodyExcerpt;
    }

    public int StatusCode { get; }

    public string? BodyExcerpt { get; }
}

/// <summary>
/// Thin, dependency-free Notion REST client. All calls are single-shot by design:
/// exactly one POST on create and one PATCH on finish, so a retry loop can never
/// double-create a timesheet row. The only retry is the mandated 429 retry-after
/// (one attempt) and it happens before the write is accepted.
/// </summary>
internal sealed class NotionClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly object MonthCacheLock = new();
    private static DateOnly monthCacheDay;
    private static string? monthCacheId;
    private static bool monthCachePopulated;

    private readonly string token;
    private string? monthTitleProperty;

    public NotionClient(string token)
    {
        this.token = token;
    }

    public IReadOnlyList<OpenTask> QueryOpenTasks(CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["filter"] = new JsonObject
            {
                ["or"] = new JsonArray(
                    StatusFilter("In-Progress"),
                    StatusFilter("Do Today")),
            },
            ["sorts"] = new JsonArray(new JsonObject
            {
                ["property"] = "Start Time",
                ["direction"] = "ascending",
            }),
            ["page_size"] = 50,
        };

        var response = SendAsync(HttpMethod.Post, $"/data_sources/{NotionIds.TasksKanbanDataSourceId}/query", payload, cancellationToken)
            .GetAwaiter().GetResult();

        var tasks = new List<OpenTask>();
        if (response["results"] is JsonArray results)
        {
            foreach (var page in results)
            {
                if (page is not JsonObject obj)
                {
                    continue;
                }

                tasks.Add(new OpenTask(
                    PageId: obj["id"]?.GetValue<string>() ?? string.Empty,
                    Title: PageTitle(obj) ?? "(untitled)",
                    StartTime: DateStart(obj["properties"]?["Start Time"]),
                    Category: obj["properties"]?["Category"]?["select"]?["name"]?.GetValue<string>(),
                    Url: obj["url"]?.GetValue<string>()));
            }
        }

        return tasks;
    }

    public async Task<CreatedTask> CreateTaskAsync(
        StartTaskDraft draft,
        DateTimeOffset start,
        string? monthPageId,
        CancellationToken cancellationToken = default)
    {
        var properties = new JsonObject
        {
            ["Name"] = new JsonObject
            {
                ["title"] = new JsonArray(TextObject(draft.Title)),
            },
            ["Priority"] = new JsonObject { ["select"] = new JsonObject { ["name"] = draft.Priority } },
            ["Status"] = new JsonObject { ["status"] = new JsonObject { ["name"] = draft.Status } },
            ["Start Time"] = DateProperty(Clock.Stamp(start)),
            ["Timeline Date"] = DateProperty(Clock.Day(start)),
            ["Category"] = new JsonObject { ["select"] = new JsonObject { ["name"] = draft.Category } },
        };

        if (!string.IsNullOrWhiteSpace(draft.Description))
        {
            properties["Description"] = new JsonObject
            {
                ["rich_text"] = new JsonArray(TextObject(draft.Description)),
            };
        }

        if (!string.IsNullOrWhiteSpace(draft.ProjectPageId))
        {
            properties[NotionIds.ProjectRelationProperty] = new JsonObject
            {
                ["relation"] = new JsonArray(new JsonObject { ["id"] = draft.ProjectPageId }),
            };
        }

        if (!string.IsNullOrWhiteSpace(monthPageId))
        {
            properties[NotionIds.MonthRelationProperty] = new JsonObject
            {
                ["relation"] = new JsonArray(new JsonObject { ["id"] = monthPageId }),
            };
        }

        var payload = new JsonObject
        {
            ["parent"] = new JsonObject { ["database_id"] = NotionIds.TasksKanbanDatabaseId },
            ["properties"] = properties,
        };

        var page = await SendAsync(HttpMethod.Post, "/pages", payload, cancellationToken).ConfigureAwait(false);
        return new CreatedTask(
            page["id"]?.GetValue<string>() ?? string.Empty,
            page["url"]?.GetValue<string>());
    }

    /// <summary>
    /// Stamps End Time and Completed. If the row never got a Start Time, Start Time is
    /// backfilled with the same timestamp so rollups keep working.
    /// </summary>
    public async Task FinishTaskAsync(
        string pageId,
        bool hadStartTime,
        DateTimeOffset end,
        CancellationToken cancellationToken = default)
    {
        var properties = new JsonObject
        {
            ["Status"] = new JsonObject { ["status"] = new JsonObject { ["name"] = NotionIds.CompletedStatus } },
            ["End Time"] = DateProperty(Clock.Stamp(end)),
        };

        if (!hadStartTime)
        {
            properties["Start Time"] = DateProperty(Clock.Stamp(end));
        }

        var payload = new JsonObject { ["properties"] = properties };
        await SendAsync(new HttpMethod("PATCH"), $"/pages/{pageId}", payload, cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<ProjectChoice> ListRecentProjects(int limit = 25, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["sorts"] = new JsonArray(new JsonObject
            {
                ["timestamp"] = "last_edited_time",
                ["direction"] = "descending",
            }),
            ["page_size"] = limit,
        };

        var response = SendAsync(HttpMethod.Post, $"/data_sources/{NotionIds.FddDataSourceId}/query", payload, cancellationToken)
            .GetAwaiter().GetResult();

        var projects = new List<ProjectChoice>();
        if (response["results"] is JsonArray results)
        {
            foreach (var page in results)
            {
                if (page is not JsonObject obj)
                {
                    continue;
                }

                var id = obj["id"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                projects.Add(new ProjectChoice(id, PageTitle(obj) ?? "(untitled project)"));
            }
        }

        return projects;
    }

    /// <summary>
    /// Resolves the "October 2026" style Month row. Cached per calendar day (task-local
    /// cache: the extension process is a single long-lived COM server).
    /// </summary>
    public async Task<(string? MonthId, string? Warning)> ResolveMonthAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        lock (MonthCacheLock)
        {
            if (monthCachePopulated && monthCacheDay == today)
            {
                return (monthCacheId, null);
            }
        }

        var monthName = Clock.MonthName(now);
        monthTitleProperty ??= await FindTitlePropertyAsync(NotionIds.MonthDataSourceId, cancellationToken).ConfigureAwait(false);

        var payload = new JsonObject
        {
            ["filter"] = new JsonObject
            {
                ["property"] = monthTitleProperty,
                ["title"] = new JsonObject { ["equals"] = monthName },
            },
            ["page_size"] = 5,
        };

        var response = await SendAsync(
            HttpMethod.Post,
            $"/data_sources/{NotionIds.MonthDataSourceId}/query",
            payload,
            cancellationToken).ConfigureAwait(false);

        var matches = response["results"] as JsonArray ?? new JsonArray();
        string? id = null;
        string? warning = null;

        if (matches.Count == 1)
        {
            id = matches[0]?["id"]?.GetValue<string>();
        }
        else if (matches.Count == 0)
        {
            warning = $"No \"{monthName}\" row in the Month database — the task was created without a Month link.";
        }
        else
        {
            warning = $"{matches.Count} Month rows match \"{monthName}\" — Month left unlinked to avoid guessing.";
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            lock (MonthCacheLock)
            {
                monthCacheId = id;
                monthCacheDay = today;
                monthCachePopulated = true;
            }
        }

        return (id, warning);
    }

    private async Task<string> FindTitlePropertyAsync(string dataSourceId, CancellationToken cancellationToken)
    {
        try
        {
            var dataSource = await SendAsync(HttpMethod.Get, $"/data_sources/{dataSourceId}", null, cancellationToken).ConfigureAwait(false);
            if (dataSource["properties"] is JsonObject properties)
            {
                foreach (var property in properties)
                {
                    if (property.Value?["type"]?.GetValue<string>() == "title")
                    {
                        return property.Key;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Fall through to the conventional property name.
        }

        return "Name";
    }

    private async Task<JsonObject> SendAsync(
        HttpMethod method,
        string relativePath,
        JsonObject? payload,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;

            using var request = new HttpRequestMessage(method, NotionIds.ApiBase + relativePath);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Notion-Version", NotionIds.NotionVersion);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            if (payload is not null)
            {
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            }

            using var response = await Http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 1)
            {
                await Task.Delay(RetryDelay(response), cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var excerpt = Excerpt(body);
                throw new NotionApiException(
                    $"Notion rejected the request: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {excerpt}",
                    (int)response.StatusCode,
                    excerpt);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return new JsonObject();
            }

            try
            {
                return JsonNode.Parse(body) as JsonObject ?? new JsonObject();
            }
            catch (Exception)
            {
                throw new NotionApiException(
                    $"Notion returned HTTP {(int)response.StatusCode} but the body was not JSON.",
                    (int)response.StatusCode,
                    Excerpt(body));
            }
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var raw = values.FirstOrDefault();
            if (int.TryParse(raw, out var seconds))
            {
                return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 15));
            }

            if (DateTimeOffset.TryParse(raw, out var when))
            {
                var delta = when - DateTimeOffset.UtcNow;
                return delta > TimeSpan.Zero ? TimeSpan.FromSeconds(Math.Min(delta.TotalSeconds, 15)) : TimeSpan.FromSeconds(1);
            }
        }

        return TimeSpan.FromSeconds(2);
    }

    private static JsonObject StatusFilter(string status) => new()
    {
        ["property"] = "Status",
        ["status"] = new JsonObject { ["equals"] = status },
    };

    private static JsonObject DateProperty(string stamp) => new()
    {
        ["date"] = new JsonObject { ["start"] = stamp },
    };

    private static JsonObject TextObject(string content) => new()
    {
        ["type"] = "text",
        ["text"] = new JsonObject { ["content"] = content },
    };

    private static string? PageTitle(JsonObject page)
    {
        if (page["properties"] is not JsonObject properties)
        {
            return null;
        }

        foreach (var property in properties)
        {
            if (property.Value?["type"]?.GetValue<string>() == "title")
            {
                return PlainText(property.Value["title"]);
            }
        }

        return null;
    }

    private static string? PlainText(JsonNode? richText)
    {
        if (richText is not JsonArray array)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var fragment in array)
        {
            builder.Append(fragment?["plain_text"]?.GetValue<string>()
                ?? fragment?["text"]?["content"]?.GetValue<string>()
                ?? string.Empty);
        }

        var text = builder.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static string? DateStart(JsonNode? dateProperty) => dateProperty?["date"]?["start"]?.GetValue<string>();

    internal static string Excerpt(string? body, int max = 400)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(empty response body)";
        }

        var flat = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}

