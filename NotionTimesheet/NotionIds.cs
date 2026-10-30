namespace NotionTimesheet;

/// <summary>
/// Every Notion identifier and picker value used by the extension lives here so a future
/// re-org only needs edits in one place.
/// </summary>
internal static class NotionIds
{
    public const string ApiBase = "https://api.notion.com/v1";

    /// <summary>Notion-Version header. The 2025-09-03 API splits databases into data sources.</summary>
    public const string NotionVersion = "2025-09-03";

    // Tasks Kanban
    public const string TasksKanbanDataSourceId = "fd88b4e2-bb79-4944-91e3-0fd88473bbff";
    public const string TasksKanbanDatabaseId = "89bb7257-5959-4095-9816-2cbc207c8d26";

    // Month rows (e.g. "October 2026") — never created by this extension, only linked.
    public const string MonthDataSourceId = "2334188b-6f6a-8179-8f33-000b25758536";

    // FDD / Project pages — the optional relation picker reads from here.
    public const string FddDataSourceId = "2334188b-6f6a-8172-9818-000b6c2435db";

    public static readonly string[] Priorities = ["Trivial", "Medium", "Crucial"];

    public static readonly string[] Categories = ["Nine Dots", "College", "Personal", "Comlab", "Magang", "Proclub"];

    public static readonly string[] Statuses = ["Do Later", "Do Today"];

    public const string DefaultPriority = "Medium";
    public const string DefaultCategory = "Nine Dots";
    public const string DefaultStatus = "Do Today";

    public const string CompletedStatus = "Completed";
    public const string ProjectRelationProperty = "FDD/Project";
    public const string MonthRelationProperty = "Month";
}

