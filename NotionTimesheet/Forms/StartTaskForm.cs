using System.Text.Json;
using System.Text.Json.Nodes;
using NotionTimesheet.Models;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Forms;

/// <summary>
/// The Start task Adaptive Card plus its submit logic. Start Time is captured at submit
/// time (never when the form is opened), so a form left open does not skew the timesheet.
/// </summary>
internal sealed partial class StartTaskForm : FormContent
{
    private readonly NotionClient client;
    private readonly string defaultStatus;
    private readonly IReadOnlyList<ProjectChoice> projects;
    private readonly ProjectChoice? project;
    private readonly bool createNewProject;

    public StartTaskForm(
        NotionClient client,
        string defaultStatus,
        IReadOnlyList<ProjectChoice> projects,
        ProjectChoice? project,
        bool createNewProject)
    {
        this.client = client;
        this.defaultStatus = defaultStatus;
        this.projects = projects;
        this.project = project;
        this.createNewProject = createNewProject;
        TemplateJson = BuildCard();
    }

    public override CommandResult SubmitForm(string payload)
    {
        try
        {
            ExtensionHost.LogMessage($"NotionTimesheet: SubmitForm received {NotionErrors.Truncate(payload, 600)}");
            return SubmitFormCore(payload);
        }
        catch (Exception ex)
        {
            ExtensionHost.LogMessage($"NotionTimesheet: SubmitForm crashed: {ex}");
            return Fail($"Nothing was created. Unexpected error: {NotionErrors.Describe(ex)}");
        }
    }

    private CommandResult SubmitFormCore(string payload)
    {
        JsonObject? inputs;
        try
        {
            inputs = JsonNode.Parse(payload) as JsonObject;
        }
        catch (Exception)
        {
            inputs = null;
        }

        var title = ReadString(inputs, "title")?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            // Belt and braces: the card marks the field required, this stops an empty POST too.
            ExtensionHost.LogMessage("NotionTimesheet: SubmitForm blocked, empty title.");
            return Fail("Task title is required — nothing was created in Notion.");
        }

        var description = ReadString(inputs, "description")?.Trim();
        var priority = Pick(ReadString(inputs, "priority"), NotionIds.Priorities, NotionIds.DefaultPriority);
        var category = Pick(ReadString(inputs, "category"), NotionIds.Categories, NotionIds.DefaultCategory);
        var status = Pick(ReadString(inputs, "status"), NotionIds.Statuses, defaultStatus);

        var start = Clock.Now();

        // Project was chosen on the picker page; in "create new" mode the typed name decides.
        // Nothing is created if this step fails.
        string? projectId = project?.Id;
        string? projectNote = null;

        if (createNewProject)
        {
            var newProjectTitle = ReadString(inputs, "projectNew")?.Trim();
            if (string.IsNullOrWhiteSpace(newProjectTitle))
            {
                return Fail("A name for the new FDD/Project is required — nothing was created.");
            }

            var exact = projects.FirstOrDefault(p =>
                string.Equals(p.Title.Trim(), newProjectTitle, StringComparison.OrdinalIgnoreCase));

            if (exact is not null)
            {
                projectId = exact.Id;
                projectNote = $"linked existing project \"{exact.Title}\"";
            }
            else
            {
                var partial = projects
                    .Where(p => p.Title.Contains(newProjectTitle, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (partial.Count == 1)
                {
                    projectId = partial[0].Id;
                    projectNote = $"linked existing project \"{partial[0].Title}\"";
                }
                else if (partial.Count > 1)
                {
                    var sample = string.Join(", ", partial.Take(3).Select(p => $"\"{p.Title}\""));
                    ExtensionHost.LogMessage($"NotionTimesheet: project \"{newProjectTitle}\" matched {partial.Count} projects; refusing.");
                    return Fail($"Nothing was created. \"{newProjectTitle}\" matches {partial.Count} projects: {sample}. Type more of the name, or go back and pick one from the list.");
                }
                else
                {
                    try
                    {
                        var createdProject = client.CreateProjectAsync(newProjectTitle).GetAwaiter().GetResult();
                        projectId = createdProject.Id;
                        projectNote = $"created project \"{newProjectTitle}\"";
                    }
                    catch (Exception ex)
                    {
                        return Fail($"Nothing was created. The FDD/Project \"{newProjectTitle}\" could not be created: {NotionErrors.Describe(ex)}");
                    }
                }
            }
        }

        var draft = new StartTaskDraft(
            Title: title,
            Description: string.IsNullOrWhiteSpace(description) ? null : description,
            Priority: priority,
            Category: category,
            Status: status,
            ProjectPageId: projectId);

        string? monthId = null;
        string? monthWarning = null;
        try
        {
            (monthId, monthWarning) = client.ResolveMonthAsync(start).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            monthWarning = $"Month lookup failed ({NotionErrors.Describe(ex)}) — the task was still created without a Month link.";
        }

        try
        {
            var created = client.CreateTaskAsync(draft, start, monthId).GetAwaiter().GetResult();

            var message = $"Started \"{title}\" at {Clock.Time(start)} WIB";
            if (projectNote is not null)
            {
                message += $" — {projectNote}";
            }

            if (monthWarning is not null)
            {
                message += $" — {monthWarning}";
            }

            if (!string.IsNullOrWhiteSpace(created.Url))
            {
                message += $" {created.Url}";
            }

            message = NotionErrors.Truncate(message, 500);
            ExtensionHost.LogMessage($"NotionTimesheet: created task {created.PageId} \"{title}\" project={projectId ?? "none"} month={monthId ?? "none"}");
            ShowStatus(message, MessageState.Success);
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = message,
                Result = CommandResult.GoHome(),
            });
        }
        catch (Exception ex)
        {
            return Fail($"Nothing was created in Notion. {NotionErrors.Describe(ex)}");
        }
    }

    private static CommandResult Fail(string message)
    {
        var text = NotionErrors.Truncate(message, 500);
        ShowStatus(text, MessageState.Error);
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = text,
            Result = CommandResult.KeepOpen(),
        });
    }

    private static void ShowStatus(string message, MessageState state)
    {
        try
        {
            new ToastStatusMessage(new StatusMessage { Message = message, State = state }).Show();
        }
        catch (Exception)
        {
            // Status area is best-effort; the toast is the authoritative feedback.
        }
    }

    private string BuildCard()
    {
        var now = Clock.Now();

        var body = new JsonArray
        {
            TextBlock("Start a Tasks Kanban timer", size: "medium", weight: "bolder", style: "heading"),
            TextBlock($"Start Time is stamped when you submit · now {Clock.Time(now)} WIB · Timeline Date {Clock.Day(now)}"),
            TextBlock(createNewProject
                ? "New FDD/Project will be created (or an existing one reused if the name matches)."
                : project is null
                    ? "Project: none"
                    : $"Project: {project.Title}",
                isSubtle: true),
            new JsonObject
            {
                ["type"] = "Input.Text",
                ["id"] = "title",
                ["label"] = "Task title",
                ["placeholder"] = "What are you working on?",
                ["isRequired"] = true,
                ["errorMessage"] = "A task title is required.",
                ["style"] = "text",
            },
            new JsonObject
            {
                ["type"] = "Input.Text",
                ["id"] = "description",
                ["label"] = "Description (optional)",
                ["placeholder"] = "Short note for the timesheet",
                ["isMultiline"] = true,
                ["style"] = "text",
            },
            ChoiceSet("priority", "Priority", NotionIds.Priorities.Select(option => (option, option)), NotionIds.DefaultPriority),
            ChoiceSet("category", "Category", NotionIds.Categories.Select(option => (option, option)), NotionIds.DefaultCategory),
        };

        if (createNewProject)
        {
            body.Add(new JsonObject
            {
                ["type"] = "Input.Text",
                ["id"] = "projectNew",
                ["label"] = "New FDD/Project name",
                ["placeholder"] = "e.g. OSMT - 13396 follow-up",
                ["isRequired"] = true,
                ["errorMessage"] = "A project name is required.",
                ["style"] = "text",
            });
        }

        body.Add(ChoiceSet(
            "status",
            "Status",
            NotionIds.Statuses.Select(option => (option, option)),
            string.IsNullOrWhiteSpace(defaultStatus) ? NotionIds.DefaultStatus : defaultStatus));


        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.6",
            ["body"] = body,
            ["actions"] = new JsonArray(new JsonObject
            {
                ["type"] = "Action.Submit",
                ["title"] = "Start task",
            }),
        };

        return card.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject TextBlock(
        string text,
        string? size = null,
        string? weight = null,
        string? style = null,
        bool isSubtle = false,
        string? color = null)
    {
        var block = new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = text,
            ["wrap"] = true,
        };

        if (size is not null)
        {
            block["size"] = size;
        }

        if (weight is not null)
        {
            block["weight"] = weight;
        }

        if (style is not null)
        {
            block["style"] = style;
        }

        if (isSubtle)
        {
            block["isSubtle"] = true;
        }

        if (color is not null)
        {
            block["color"] = color;
        }

        return block;
    }

    private static JsonObject ChoiceSet(
        string id,
        string label,
        IEnumerable<(string Title, string Value)> options,
        string defaultValue,
        string style = "compact")
    {
        var choices = new JsonArray();
        var hasDefault = false;

        foreach (var (optionTitle, optionValue) in options)
        {
            choices.Add(new JsonObject
            {
                ["title"] = optionTitle,
                ["value"] = optionValue,
            });

            if (optionValue == defaultValue)
            {
                hasDefault = true;
            }
        }

        if (!hasDefault && choices.Count > 0)
        {
            defaultValue = choices[0]!["value"]!.GetValue<string>();
        }

        return new JsonObject
        {
            ["type"] = "Input.ChoiceSet",
            ["id"] = id,
            ["label"] = label,
            ["style"] = style,
            ["value"] = defaultValue,
            ["choices"] = choices,
        };
    }

    private static string? ReadString(JsonObject? inputs, string key)
    {
        try
        {
            return inputs?[key]?.GetValue<string>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Pick(string? value, string[] allowed, string fallback) =>
        !string.IsNullOrWhiteSpace(value) && allowed.Contains(value, StringComparer.Ordinal) ? value : fallback;
}

