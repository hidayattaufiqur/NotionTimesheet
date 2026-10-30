using NotionTimesheet.Pages;
using NotionTimesheet.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet;

/// <summary>Registers the two top-level commands in Command Palette.</summary>
public partial class NotionTimesheetCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] commands;
    private readonly NotionSettingsManager settingsManager = new();

    public NotionTimesheetCommandsProvider()
    {
        DisplayName = "Notion Timesheet";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        var start = new CommandItem(new StartTaskPage(settingsManager))
        {
            Title = "Start task",
            Subtitle = "Create a Tasks Kanban row with Start Time = now",
            Icon = new IconInfo("\uE768"), // Play
        };

        var finish = new CommandItem(new FinishTaskPage(settingsManager))
        {
            Title = "Finish task",
            Subtitle = "List In-Progress rows and stamp End Time = now",
            Icon = new IconInfo("\uE823"), // Stop
        };

        var settingsContext = new CommandContextItem(settingsManager.Settings.SettingsPage)
        {
            Title = "Notion Timesheet settings",
            Icon = new IconInfo("\uE713"),
        };

        start.MoreCommands = [settingsContext];
        finish.MoreCommands = [settingsContext];

        commands = [start, finish];
        Settings = settingsManager.Settings;
    }

    public override ICommandItem[] TopLevelCommands() => commands;
}

