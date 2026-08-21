namespace TaskPanel.Models;

/// <summary>A completed task, kept under the name of the list it was archived from.</summary>
public class ArchivedTask
{
    public string Text { get; set; } = string.Empty;
    public string SourceListName { get; set; } = string.Empty;
}
