namespace NotionTimesheet.Models;

/// <summary>A row returned by the In-Progress / Do Today query.</summary>
internal sealed record OpenTask(
    string PageId,
    string Title,
    string? StartTime,
    string? Category,
    string? Url)
{
    public bool HasStartTime => !string.IsNullOrWhiteSpace(StartTime);
}

/// <summary>Minimal shape of a page created in Tasks Kanban.</summary>
internal sealed record CreatedTask(string PageId, string? Url);

/// <summary>A row of the FDD/Project data source offered by the form picker.</summary>
internal sealed record ProjectChoice(string Id, string Title);

/// <summary>Everything the Start task form collects.</summary>
internal sealed record StartTaskDraft(
    string Title,
    string? Description,
    string Priority,
    string Category,
    string Status,
    string? ProjectPageId);

