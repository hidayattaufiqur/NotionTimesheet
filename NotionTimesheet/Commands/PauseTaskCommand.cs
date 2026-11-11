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

        try
        {
            var queued = client.PauseTaskAsync(task.PageId, now).GetAwaiter().GetResult();
            refresh();

            var message = $"Paused \"{task.Title}\" at {Clock.Time(now)} WIB — finished this run and re-queued it as Do Today.";
            if (!string.IsNullOrWhiteSpace(queued.Url))
            {
                message += $" {queued.Url}";
            }

            return CommandResult.ShowToast(new ToastArgs
            {
                Message = NotionErrors.Truncate(message, 400),
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
}
