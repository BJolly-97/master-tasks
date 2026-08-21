using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TaskPanel.Models;

/// <summary>
/// A lightweight checklist item nested under a <see cref="TaskItem"/>. Ticking one
/// off archives just that piece and leaves the parent task untouched.
/// </summary>
public class SubTaskItem : INotifyPropertyChanged
{
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }

    // Back-reference to the owning task, so a row can remove/archive itself
    // without walking the visual tree. Not persisted.
    [JsonIgnore]
    public TaskItem? Owner { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
