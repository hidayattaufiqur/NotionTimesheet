using NotionTimesheet.Forms;
using NotionTimesheet.Models;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Pages;

/// <summary>Top-level "Start task…" command: a form page backed by Notion.</summary>
internal sealed partial class StartTaskPage : ContentPage
{
    private readonly NotionSettingsManager settings;

    public StartTaskPage(NotionSettingsManager settings)
    {
        this.settings = settings;
        Name = "Open";
        Title = "Start task";
        Icon = new IconInfo("\uE768"); // Play
    }

    public override IContent[] GetContent()
    {
        var resolution = TokenStore.Resolve();
        if (!resolution.HasToken)
        {
            return [new MarkdownContent(BuildMissingTokenNote(resolution))];
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
            warning = $"Project list unavailable ({NotionErrors.Describe(ex)}) — you can still start the task with No project.";
        }

        return [new StartTaskForm(client, settings.DefaultStatus, projects, warning)];
    }

    private static string BuildMissingTokenNote(TokenResolution resolution) =>
        $"""
# Notion token not configured

{resolution.Problem ?? "No Notion token found."}

## Fix

1. Create the folder `{TokenStore.Folder}` if needed.
2. Add `token.txt` in it with your Notion integration token on a single line (it starts with `ntn_` or `secret_`).

Exact path:

```
{TokenStore.TokenFile}
```

The `NOTION_API_KEY` environment variable also works and takes precedence. After changing the token, run **Reload Command Palette extensions**.
""";
}

