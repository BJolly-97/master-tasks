using System.IO;
using System.Text.Json;
using TaskPanel.Models;

namespace TaskPanel.Services;

public static class DataStore
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskPanel");

    private static readonly string FilePath = Path.Combine(Folder, "data.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static AppData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<AppData>(json, Options);
                if (data is not null)
                {
                    // Re-link each task to its owning list, and each sub-task to its
                    // owning task (neither back-reference is persisted).
                    foreach (var list in data.Lists)
                        foreach (var task in list.Tasks)
                        {
                            task.Owner = list;
                            foreach (var subTask in task.SubTasks)
                                subTask.Owner = task;
                        }
                    return data;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable file — fall through to a fresh default below
            // rather than crash the app on startup.
        }

        var fresh = new AppData();
        fresh.Lists.Add(new TaskListModel { Name = "Today" });
        return fresh;
    }

    public static void Save(AppData data)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var json = JsonSerializer.Serialize(data, Options);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Best-effort save; a failure here shouldn't crash a background app.
        }
    }
}
