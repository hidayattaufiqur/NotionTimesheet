using NotionTimesheet.Models;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Commands;

/// <summary>
/// Stamps End Time (and Status=Completed) on one row. Single PATCH, so it is idempotent
/// by construction: invoking twice rewrites the same two fields instead of duplicating work.
/// </summary>
internal sealed partial class FinishTaskCommand : InvokableCommand
{
    private readonly NotionClient client;
    private readonly OpenTask task;
    private readonly Action refresh;

    public FinishTaskCommand(NotionClient client, OpenTask task, Action refresh)
    {
        this.client = client;
        this.task = task;
        this.refresh = refresh;
    }

    public override string Name => "Finish";

    public override IconInfo Icon => new("\uE73E"); // Checkmark

    public override CommandResult Invoke()
    {
        var end = Clock.Now();

        try
        {
            client.FinishTaskAsync(task.PageId, task.HasStartTime, end).GetAwaiter().GetResult();
            refresh();

            var message = task.HasStartTime
                ? $"Finished \"{task.Title}\" at {Clock.Time(end)} WIB"
                : $"Finished \"{task.Title}\" at {Clock.Time(end)} WIB (Start Time was empty, so it was set to the same moment)";

            return CommandResult.ShowToast(new ToastArgs
            {
                Message = NotionErrors.Truncate(message, 300),
                Result = CommandResult.KeepOpen(),
            });
        }
        catch (Exception ex)
        {
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = NotionErrors.Truncate($"Notion did not accept the update. {NotionErrors.Describe(ex)}", 500),
                Result = CommandResult.KeepOpen(),
            });
        }
    }
}

