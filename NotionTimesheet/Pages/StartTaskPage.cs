using NotionTimesheet.Commands;
using NotionTimesheet.Models;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Pages;

/// <summary>
/// Top-level "Start task" command. Hub page with two paths: "New task" opens the searchable
/// project picker (which then opens the task form), and every unstarted Tasks Kanban row
/// (Do Later / Do Today / In-Progress with no Start Time) can be started directly.
/// </summary>
internal sealed partial class StartTaskPage : ListPage
{
    private readonly NotionSettingsManager settings;

    public StartTaskPage(NotionSettingsManager settings)
    {
        this.settings = settings;
        Name = "Open";
        Title = "Start task";
        Icon = new IconInfo("\uE768"); // Play
        PlaceholderText = "Search unstarted tasks, or pick New task to create one";
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
        var items = new List<IListItem>
        {
            new ListItem(new ProjectPickerPage(settings))
            {
                Title = "New task…",
                Subtitle = "Pick a project, then create a Tasks Kanban row",
                Icon = new IconInfo("\uE710"), // Add
                Section = "New",
            },
        };

        IReadOnlyList<OpenTask> startable = [];
        try
        {
            startable = client.QueryStartableTasks();
        }
        catch (Exception ex)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "Existing tasks unavailable",
                Subtitle = NotionErrors.Truncate(NotionErrors.Describe(ex), 300),
                Icon = new IconInfo("\uE7BA"),
            });

            return [.. items];
        }

        if (startable.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "No unstarted tasks",
                Subtitle = "Every Do Later / Do Today row already has a Start Time",
                Icon = new IconInfo("\uE946"), // Info
                Section = "Start existing",
            });

            return [.. items];
        }

        foreach (var task in startable)
        {
            items.Add(new ListItem(new StartExistingTaskCommand(client, task, Refresh))
            {
                Title = task.Title,
                Subtitle = BuildSubtitle(task),
                Icon = new IconInfo("\uE768"),
                Section = "Start existing",
            });
        }

        return [.. items];
    }

    private static string BuildSubtitle(OpenTask task)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(task.Status))
        {
            parts.Add(task.Status);
        }

        if (!string.IsNullOrWhiteSpace(task.Category))
        {
            parts.Add(task.Category);
        }

        parts.Add("Start this task");
        return string.Join(" · ", parts);
    }

    private void Refresh() => RaiseItemsChanged();
}
