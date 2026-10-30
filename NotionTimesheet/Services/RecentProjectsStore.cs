using System.Text.Json.Nodes;
using NotionTimesheet.Models;

namespace NotionTimesheet.Services;

/// <summary>
/// The last few projects used, newest first, persisted next to the token so the picker can
/// put them at the top. Not a secret, so a plain JSON file is fine.
/// </summary>
internal static class RecentProjectsStore
{
    private const int MaxEntries = 3;

    private static readonly object Gate = new();

    private static string FilePath => Path.Combine(TokenStore.Folder, "recent-projects.json");

    public static List<ProjectChoice> Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return [];
                }

                if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonArray array)
                {
                    return [];
                }

                var result = new List<ProjectChoice>();
                foreach (var node in array)
                {
                    var id = node?["id"]?.GetValue<string>();
                    var title = node?["title"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title))
                    {
                        result.Add(new ProjectChoice(id, title));
                    }
                }

                return result;
            }
            catch (Exception)
            {
                return [];
            }
        }
    }

    public static void Remember(ProjectChoice project)
    {
        if (string.IsNullOrWhiteSpace(project.Id) || string.IsNullOrWhiteSpace(project.Title))
        {
            return;
        }

        lock (Gate)
        {
            var entries = Load()
                .Where(p => !string.Equals(p.Id, project.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            entries.Insert(0, project);

            var array = new JsonArray();
            foreach (var entry in entries.Take(MaxEntries))
            {
                array.Add(new JsonObject
                {
                    ["id"] = entry.Id,
                    ["title"] = entry.Title,
                });
            }

            try
            {
                Directory.CreateDirectory(TokenStore.Folder);
                File.WriteAllText(FilePath, array.ToJsonString());
            }
            catch (Exception)
            {
                // Recent list is a convenience; never let it break task creation.
            }
        }
    }
}
