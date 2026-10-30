using NotionTimesheet.Models;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Pages;

/// <summary>
/// Top-level "Start task" command. It is a searchable project picker first: Command Palette's
/// own search box filters this list as you type (e.g. "13396" narrows to that one project),
/// which an Adaptive Card choice set cannot do. Picking a row opens the task form with the
/// project already linked.
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
        PlaceholderText = "Search FDD/Projects — typing filters this list";
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

        IReadOnlyList<ProjectChoice> projects = [];
        string? warning = null;
        try
        {
            projects = client.ListProjects();
        }
        catch (Exception ex)
        {
            warning = $"Project list unavailable ({NotionErrors.Describe(ex)}) — you can still start without a project.";
        }

        var items = new List<IListItem>
        {
            new ListItem(new StartTaskFormPage(settings, client, projects, null, createNewProject: false))
            {
                Title = "No project",
                Subtitle = "Start the task without an FDD/Project link",
                Icon = new IconInfo("\uE768"),
            },
            new ListItem(new StartTaskFormPage(settings, client, projects, null, createNewProject: true))
            {
                Title = "Create new project…",
                Subtitle = "Adds a page to FDD/Projects, then links it to the task",
                Icon = new IconInfo("\uE710"), // Add
            },
        };

        if (warning is not null)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "Project list unavailable",
                Subtitle = NotionErrors.Truncate(warning, 300),
                Icon = new IconInfo("\uE7BA"),
            });
        }

        foreach (var project in projects)
        {
            items.Add(new ListItem(new StartTaskFormPage(settings, client, projects, project, createNewProject: false))
            {
                Title = project.Title,
                Subtitle = "Link this FDD/Project",
                Icon = new IconInfo("\uE8B7"), // Folder
            });
        }

        return [.. items];
    }
}

