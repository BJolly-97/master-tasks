using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TaskPanel.Models;

public class TaskListModel : INotifyPropertyChanged
{
    private string _name = "List";

    public TaskListModel()
    {
        Tasks.CollectionChanged += Tasks_CollectionChanged;
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TaskItem> Tasks { get; set; } = new();

    /// <summary>Whether any task here is due within a week (or overdue) — drives the tab pill's color.</summary>
    [JsonIgnore]
    public bool HasUrgentTask => Tasks.Any(t => t.IsUrgent);

    private void Tasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (TaskItem task in e.OldItems)
                task.PropertyChanged -= Task_PropertyChanged;

        if (e.NewItems is not null)
            foreach (TaskItem task in e.NewItems)
                task.PropertyChanged += Task_PropertyChanged;

        OnPropertyChanged(nameof(HasUrgentTask));
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskItem.IsUrgent))
            OnPropertyChanged(nameof(HasUrgentTask));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
