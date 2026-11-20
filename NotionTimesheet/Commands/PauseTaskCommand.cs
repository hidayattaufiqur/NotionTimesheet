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
            // An identical pause still being processed (double-invoke): wait for it, then reuse.
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
            while (!RecentPauses.ContainsKey(task.PageId) && InFlightPauses.Contains(task.PageId))
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    break;
                }

                Monitor.Wait(PauseGate, TimeSpan.FromSeconds(5));
                PrunePauses();
            }

            if (RecentPauses.TryGetValue(task.PageId, out var previous))
            {
                ExtensionHost.LogMessage($"NotionTimesheet: duplicate pause ignored for {task.PageId}, reusing copy {previous.Created.PageId}.");
                return CommandResult.ShowToast(new ToastArgs
                {
                    Message = previous.Message,
                    Result = CommandResult.KeepOpen(),
                });
            }

            InFlightPauses.Add(task.PageId);
        }

        try
        {
            // Server-side backstop: if an unstarted copy of this title was queued in the last
            // 10 minutes, the first pause already went through — finish (idempotent PATCH)
            // and reuse the copy instead of POSTing another.
            var existingCopy = client.FindRecentByTitleAsync(
                task.Title,
                DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
                onlyUnstarted: true).GetAwaiter().GetResult();

            if (existingCopy is not null)
            {
                client.FinishTaskAsync(task.PageId, hadStartTime: true, now).GetAwaiter().GetResult();
                refresh();

                ExtensionHost.LogMessage($"NotionTimesheet: server-side pause duplicate found, reusing copy {existingCopy.PageId}.");
                var reuseMessage = $"\"{task.Title}\" was already paused — reusing the queued copy instead of creating a duplicate.";
                if (!string.IsNullOrWhiteSpace(existingCopy.Url))
                {
                    reuseMessage += $" {existingCopy.Url}";
                }

                reuseMessage = NotionErrors.Truncate(reuseMessage, 400);
                lock (PauseGate)
                {
                    RecentPauses[task.PageId] = (DateTimeOffset.UtcNow, existingCopy, reuseMessage);
                }

                return CommandResult.ShowToast(new ToastArgs
                {
                    Message = reuseMessage,
                    Result = CommandResult.KeepOpen(),
                });
            }

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
        finally
        {
            lock (PauseGate)
            {
                InFlightPauses.Remove(task.PageId);
                Monitor.PulseAll(PauseGate);
            }
        }
    }

    /// <summary>
    /// Double-invoke guard: pausing twice would finish once but queue two copies.
    /// Same 120-second window as the Start form guard.
    /// </summary>
    private static readonly object PauseGate = new();
    private static readonly Dictionary<string, (DateTimeOffset At, CreatedTask Created, string Message)> RecentPauses = new();
    private static readonly HashSet<string> InFlightPauses = [];
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
