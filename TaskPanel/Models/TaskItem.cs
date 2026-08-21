using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TaskPanel.Models;

public class TaskItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isDone;
    private DateTime? _dueDate;
    private bool _isExpanded;

    public TaskItem()
    {
        // Keep the collapse/expand toggle's "▸ N" label current as sub-tasks come and go.
        SubTasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SubTaskToggleLabel));
    }

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }

    public bool IsDone
    {
        get => _isDone;
        set { _isDone = value; OnPropertyChanged(); }
    }

    /// <summary>Optional completion deadline. Most tasks won't have one.</summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            _dueDate = value;
            // A changed date might introduce or clear urgency — let a fresh
            // reminder fire for it rather than staying silent forever.
            HasNotifiedUrgent = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsUrgent));
            OnPropertyChanged(nameof(IsOverdue));
            OnPropertyChanged(nameof(DueDateLabel));
        }
    }

    /// <summary>Due today, in the past, or within the next 7 days.</summary>
    [JsonIgnore]
    public bool IsUrgent => DueDate.HasValue && DueDate.Value.Date <= DateTime.Today.AddDays(7);

    [JsonIgnore]
    public bool IsOverdue => DueDate.HasValue && DueDate.Value.Date < DateTime.Today;

    [JsonIgnore]
    public string DueDateLabel => DueDate switch
    {
        null => "\U0001F5D3", // 🗓 — no date set yet
        { } d when IsOverdue => $"⚠ {d:d MMM}",
        { } d => $"{d:d MMM}",
    };

    // Whether a "this is due soon" tray reminder has already fired for the
    // current due date, so it doesn't repeat every timer tick. Not persisted —
    // resets each session, and explicitly reset whenever DueDate changes.
    [JsonIgnore]
    public bool HasNotifiedUrgent { get; set; }

    /// <summary>Smaller checklist items nested under this task. Not urgency-tracked or dated themselves.</summary>
    public ObservableCollection<SubTaskItem> SubTasks { get; set; } = new();

    /// <summary>Whether the sub-tasks panel is currently shown. Transient UI state, not persisted.</summary>
    [JsonIgnore]
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(SubTaskToggleLabel)); }
    }

    [JsonIgnore]
    public string SubTaskToggleLabel => SubTasks.Count == 0
        ? "+ sub-task"
        : $"{(IsExpanded ? "▾" : "▸")} {SubTasks.Count} sub-task{(SubTasks.Count == 1 ? "" : "s")}";

    /// <summary>Re-raises the date-derived bindings so the UI re-groups/re-labels as time passes.</summary>
    public void RefreshUrgency()
    {
        OnPropertyChanged(nameof(IsUrgent));
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(DueDateLabel));
    }

    // Back-reference to the owning list, so a row can remove itself
    // without walking the visual tree. Not persisted.
    [JsonIgnore]
    public TaskListModel? Owner { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
