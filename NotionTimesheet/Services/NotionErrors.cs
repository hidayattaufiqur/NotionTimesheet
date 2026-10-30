namespace NotionTimesheet.Services;

internal static class NotionErrors
{
    /// <summary>One-line, user-facing description of a failure. Never includes the token.</summary>
    public static string Describe(Exception exception, int max = 400) => exception switch
    {
        NotionApiException api => $"HTTP {api.StatusCode}: {api.BodyExcerpt ?? api.Message}",
        TaskCanceledException => "Notion did not answer in time (request timed out).",
        HttpRequestException http => $"Could not reach api.notion.com: {http.Message}",
        _ => exception.Message,
    };

    public static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max] + "…";
    }
}

