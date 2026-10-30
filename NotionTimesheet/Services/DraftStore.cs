namespace NotionTimesheet.Services;

internal sealed record TaskDraft(
    string? Title,
    string? Description,
    string Priority,
    string Category,
    string Status,
    DateTimeOffset SavedAt);

/// <summary>
/// In-memory draft of the Start task form, so going back to the project picker (or fixing a
/// rejected submit) does not lose what was typed. Cleared after a successful create and
/// ignored once stale. Process-local by design: an extension reload starts a fresh server.
/// </summary>
internal static class DraftStore
{
    private static readonly object Gate = new();

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private static TaskDraft? last;

    public static void Save(TaskDraft draft)
    {
        lock (Gate)
        {
            last = draft;
        }
    }

    public static TaskDraft? TryGet()
    {
        lock (Gate)
        {
            if (last is null || DateTimeOffset.UtcNow - last.SavedAt > MaxAge)
            {
                return null;
            }

            return last;
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            last = null;
        }
    }
}
