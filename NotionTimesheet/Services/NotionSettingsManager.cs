using Microsoft.CommandPalette.Extensions.Toolkit;

namespace NotionTimesheet.Services;

/// <summary>
/// Extension settings, persisted next to the token in %LOCALAPPDATA%\CmdPalNotion.
/// Only non-secret preferences live here.
/// </summary>
internal sealed class NotionSettingsManager : JsonSettingsManager
{
    private readonly ChoiceSetSetting defaultStatus;

    public NotionSettingsManager()
    {
        FilePath = Path.Combine(TokenStore.Folder, "settings.json");

        defaultStatus = new ChoiceSetSetting(
            "defaultStatus",
            "Default status",
            "Status preselected by the Start task form.",
            [
                new ChoiceSetSetting.Choice("Do Later", "Do Later"),
                new ChoiceSetSetting.Choice("Do Today", "Do Today"),
            ])
        {
            Value = NotionIds.DefaultStatus,
        };

        Settings.Add(defaultStatus);
        LoadSettings();
        Settings.SettingsChanged += (_, _) => SaveSettings();
    }

    public string DefaultStatus =>
        string.IsNullOrWhiteSpace(defaultStatus.Value) ? NotionIds.DefaultStatus : defaultStatus.Value;
}

