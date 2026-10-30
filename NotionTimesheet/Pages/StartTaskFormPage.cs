using NotionTimesheet.Models;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Pages;

/// <summary>
/// The Start task form, pre-bound to the project picked on the previous list page (or to
/// "create a new project" mode). Keeping the pick on its own list page is what makes
/// type-to-search work, because Command Palette filters list items natively.
/// </summary>
internal sealed partial class StartTaskFormPage : ContentPage
{
    private readonly NotionSettingsManager settings;
    private readonly NotionClient client;
    private readonly IReadOnlyList<ProjectChoice> projects;
    private readonly ProjectChoice? project;
    private readonly bool createNewProject;

    public StartTaskFormPage(
        NotionSettingsManager settings,
        NotionClient client,
        IReadOnlyList<ProjectChoice> projects,
        ProjectChoice? project,
        bool createNewProject)
    {
        this.settings = settings;
        this.client = client;
        this.projects = projects;
        this.project = project;
        this.createNewProject = createNewProject;

        Name = "Start";
        Title = createNewProject ? "Start task — new project" : "Start task";
        Icon = new IconInfo("\uE768");
    }

    public override IContent[] GetContent() =>
        [new NotionTimesheet.Forms.StartTaskForm(client, settings.DefaultStatus, projects, project, createNewProject)];
}

