using NotionTimesheet.Commands;
using NotionTimesheet.Models;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Pages;

/// <summary>
/// Top-level "Finish task" command. Rows are queried live every time the page is shown;
/// the first item is an explicit refresh so the list can be re-pulled without leaving.
/// </summary>
internal sealed partial class FinishTaskPage : ListPage
{
    private readonly NotionSettingsManager settings;

    private IReadOnlyList<OpenTask> tasks = [];
    private string? error;
    private int? errorStatusCode;

    public FinishTaskPage(NotionSettingsManager settings)
    {
        this.settings = settings;
        Name = "Open";
        Title = "Finish task";
        Icon = new IconInfo("\uE823"); // Stop
        PlaceholderText = "No In-Progress or Do Today tasks in Tasks Kanban";
    }

    public override IListItem[] GetItems()
    {
        var resolution = TokenStore.Resolve();
        if (!resolution.HasToken)
        {
            return
            [
                new ListItem(new SetupPage(resolution.Problem))
                {
                    Title = "Notion token not configured",
                    Subtitle = $"Open this for the exact path ({TokenStore.TokenFile})",
                    Icon = new IconInfo("\uE72E"),
                },
            ];
        }

        var client = new NotionClient(resolution.Token!);
        tasks = [];
        error = null;
        errorStatusCode = null;

        try
        {
            tasks = client.QueryOpenTasks();
        }
        catch (NotionApiException ex)
        {
            error = $"HTTP {ex.StatusCode}: {ex.BodyExcerpt ?? ex.Message}";
            errorStatusCode = ex.StatusCode;
        }
        catch (Exception ex)
        {
            error = NotionErrors.Describe(ex);
        }

        var items = new List<IListItem>
        {
            new ListItem(new AnonymousCommand(Refresh) { Name = "Refresh", Result = CommandResult.KeepOpen() })
            {
                Title = "Refresh list",
                Subtitle = "Re-query Tasks Kanban for In-Progress and Do Today rows",
                Icon = new IconInfo("\uE72C"), // Refresh
            },
        };

        if (error is not null)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = errorStatusCode == 401 ? "Notion rejected the token (HTTP 401)" : "Notion request failed",
                Subtitle = NotionErrors.Truncate(error, 300),
                Icon = new IconInfo("\uE7BA"), // Warning
            });

            return [.. items];
        }

        if (tasks.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "Nothing is in progress",
                Subtitle = "Start a task first, or press Refresh after changing Status in Notion",
                Icon = new IconInfo("\uE946"), // Info
            });

            return [.. items];
        }

        foreach (var task in tasks)
        {
            var moreCommands = new List<IContextItem>();
            if (!string.IsNullOrWhiteSpace(task.Url))
            {
                moreCommands.Add(new CommandContextItem(new OpenUrlCommand(task.Url))
                {
                    Title = "Open in Notion",
                });
            }

            items.Add(new ListItem(new FinishTaskCommand(client, task, Refresh))
            {
                Title = task.Title,
                Subtitle = BuildSubtitle(task),
                Icon = new IconInfo("\uE823"),
                MoreCommands = [.. moreCommands],
            });
        }

        return [.. items];
    }

    private static string BuildSubtitle(OpenTask task)
    {
        var parts = new List<string> { $"Start {Clock.Friendly(task.StartTime)}" };
        if (!string.IsNullOrWhiteSpace(task.Category))
        {
            parts.Add(task.Category);
        }

        return string.Join(" · ", parts);
    }

    private void Refresh() => RaiseItemsChanged();
}

