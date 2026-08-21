using System.Collections.ObjectModel;

namespace TaskPanel.Models;

public class AppData
{
    public ObservableCollection<TaskListModel> Lists { get; set; } = new();

    public ObservableCollection<ArchivedTask> Archive { get; set; } = new();

    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double WindowWidth { get; set; } = 320;
    public double WindowHeight { get; set; } = 480;

    /// <summary>The Friday the "export your archive" nudge last fired, so it shows at most once per Friday.</summary>
    public DateTime? LastExportNudgeDate { get; set; }
}
