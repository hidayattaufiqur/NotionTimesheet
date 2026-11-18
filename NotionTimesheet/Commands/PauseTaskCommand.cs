using NotionTimesheet.Models;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Commands;

/// <summary>
/// Pause: finishes the running row, then re-queues a copy as Do Today with no Start/End Time —
/// the same shape the Notion Pause button leaves behind.
/// </summary>
internal sealed partial class PauseTaskCommand : InvokableCommand
{
    private readonly NotionClient client;
    private readonly OpenTask task;
    private readonly Action refresh;

    public PauseTaskCommand(NotionClient client, OpenTask task, Action refresh)
    {
        this.client = client;
        this.task = task;
        this.refresh = refresh;
    }

    public override string Name => "Pause";

    public override IconInfo Icon => new("\uE769"); // Pause

    public override CommandResult Invoke()
    {
        var now = Clock.Now();

        lock (PauseGate)
        {
            PrunePauses();
            if (RecentPauses.TryGetValue(task.PageId, out var previous))
            {
                ExtensionHost.LogMessage($"NotionTimesheet: duplicate pause ignored for {task.PageId}, reusing copy {previous.Created.PageId}.");
                return CommandResult.ShowToast(new ToastArgs
                {
                    Message = previous.Message,
                    Result = CommandResult.KeepOpen(),
                });
            }
        }

        try
        {
            var queued = client.PauseTaskAsync(task.PageId, now).GetAwaiter().GetResult();
            refresh();

            var message = $"Paused \"{task.Title}\" at {Clock.Time(now)} WIB — finished this run and re-queued it as Do Today.";
            if (!string.IsNullOrWhiteSpace(queued.Url))
            {
                message += $" {queued.Url}";
            }

            message = NotionErrors.Truncate(message, 400);
            lock (PauseGate)
            {
                PrunePauses();
                RecentPauses[task.PageId] = (DateTimeOffset.UtcNow, queued, message);
            }

            return CommandResult.ShowToast(new ToastArgs
            {
                Message = message,
                Result = CommandResult.KeepOpen(),
            });
        }
        catch (Exception ex)
        {
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = NotionErrors.Truncate($"Notion did not accept the pause. {NotionErrors.Describe(ex)}", 500),
                Result = CommandResult.KeepOpen(),
            });
        }
    }

    /// <summary>
    /// Double-invoke guard: pausing twice would finish once but queue two copies.
    /// Same 120-second window as the Start form guard.
    /// </summary>
    private static readonly object PauseGate = new();
    private static readonly Dictionary<string, (DateTimeOffset At, CreatedTask Created, string Message)> RecentPauses = new();
    private static readonly TimeSpan PauseDedupeWindow = TimeSpan.FromSeconds(120);

    private static void PrunePauses()
    {
        var cutoff = DateTimeOffset.UtcNow - PauseDedupeWindow;
        foreach (var key in RecentPauses.Where(kv => kv.Value.At < cutoff).Select(kv => kv.Key).ToList())
        {
            RecentPauses.Remove(key);
        }
    }
}
