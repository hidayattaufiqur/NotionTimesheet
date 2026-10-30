using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using NotionTimesheet.Services;

namespace NotionTimesheet.Pages;

/// <summary>
/// First-run helper. Shown instead of the form/list when no token can be found, so the
/// user never gets a bare 401 from Notion.
/// </summary>
internal sealed partial class SetupPage : ContentPage
{
    private readonly string? problem;

    public SetupPage(string? problem = null)
    {
        this.problem = problem;
        Name = "Open";
        Title = "Notion token needed";
        Icon = new IconInfo("\uE72E"); // Lock
    }

    public override IContent[] GetContent() => [new MarkdownContent(BuildBody())];

    private string BuildBody()
    {
        var resolution = TokenStore.Resolve();
        var folder = TokenStore.Folder;
        var file = TokenStore.TokenFile;

        var state = resolution.HasToken
            ? $"A token is configured from `{resolution.Source}`."
            : string.IsNullOrWhiteSpace(problem) ? resolution.Problem ?? "No Notion token found." : problem;

        return $"""
# Notion Timesheet needs a Notion integration token

{state}

## Where to put it

1. Open `{folder}` (create the folder if it does not exist).
2. Create a file named `token.txt`.
3. Paste the integration token as a single line. It starts with `ntn_` or `secret_`.

Exact path:

```
{file}
```

`token.txt` is only read at call time and never copied into the extension, its package or its settings.

## Alternative

Set the user environment variable `NOTION_API_KEY` instead. The environment variable wins over `token.txt`.

## After adding or changing the token

Run **Reload Command Palette extensions** in the palette so the extension picks up the new configuration, then use **Start task** again.
""";
    }
}

