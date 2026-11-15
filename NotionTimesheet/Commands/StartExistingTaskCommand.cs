using NotionTimesheet.Models;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Commands;

/// <summary>
/// Starts an existing backlog row: stamps Start Time = now and moves it to In-Progress.
/// Single PATCH, so invoking twice rewrites the same fields instead of duplicating work.
/// </summary>
internal sealed partial class StartExistingTaskCommand : InvokableCommand
{
    private readonly NotionClient client;
    private readonly OpenTask task;
    private readonly Action refresh;

    public StartExistingTaskCommand(NotionClient client, OpenTask task, Action refresh)
    {
        this.client = client;
        this.task = task;
        this.refresh = refresh;
    }

    public override string Name => "Start";

    public override IconInfo Icon => new("\uE768"); // Play

    public override CommandResult Invoke()
    {
        var start = Clock.Now();

        try
        {
            string? monthId = null;
            try
            {
                (monthId, _) = client.ResolveMonthAsync(start).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                ExtensionHost.LogMessage($"NotionTimesheet: Month lookup failed on start-existing: {NotionErrors.Describe(ex)}");
            }

            client.StartExistingTaskAsync(task.PageId, start, monthId).GetAwaiter().GetResult();
            refresh();

            return CommandResult.ShowToast(new ToastArgs
            {
                Message = NotionErrors.Truncate($"Started \"{task.Title}\" at {Clock.Time(start)} WIB", 300),
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
