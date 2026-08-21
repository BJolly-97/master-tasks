using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskPanel.Models;
using TaskPanel.Native;
using TaskPanel.Services;
using TaskPanel.Windows;

namespace TaskPanel;

public partial class MainWindow : Window
{
    private static readonly Brush ActivePillBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xF9, 0xAD, 0x6A));

    private AppData? _data;
    private bool _showingArchive;
    private DispatcherTimer? _urgencyTimer;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AcrylicHelper.EnableAcrylic(this, 0x26, 0x4D, 0x59, 190);

        _data = DataStore.Load();
        DataContext = _data;

        var archiveView = (CollectionViewSource)Resources["ArchiveGroupedView"];
        archiveView.Source = _data.Archive;
        UpdateArchiveEmptyHint();

        if (_data.WindowLeft.HasValue && _data.WindowTop.HasValue)
        {
            Left = _data.WindowLeft.Value;
            Top = _data.WindowTop.Value;
        }
        Width = _data.WindowWidth;
        Height = _data.WindowHeight;

        if (_data.Lists.Count > 0)
            ListsTabControl.SelectedIndex = 0;

        // Re-checks urgency (and fires a tray reminder for anything newly due-soon)
        // right away, then periodically — so a task can still "bump" into Urgent
        // purely from time passing while the app sits open, not just on edits.
        RefreshUrgencyAndNotify();
        CheckFridayExportNudge();
        _urgencyTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _urgencyTimer.Tick += (_, _) =>
        {
            RefreshUrgencyAndNotify();
            CheckFridayExportNudge();
        };
        _urgencyTimer.Start();
    }

    /// <summary>
    /// A gentle once-per-Friday nudge to export the archive — never forces the export
    /// dialog, just a tray reminder, and stays quiet if the archive is empty anyway.
    /// </summary>
    private void CheckFridayExportNudge()
    {
        if (_data is null) return;
        if (DateTime.Today.DayOfWeek != DayOfWeek.Friday) return;
        if (_data.LastExportNudgeDate?.Date == DateTime.Today) return;
        if (_data.Archive.Count == 0) return;

        _data.LastExportNudgeDate = DateTime.Today;
        SaveNow();

        var count = _data.Archive.Count;
        var plural = count == 1 ? "task" : "tasks";
        (Application.Current as App)?.ShowReminder("Friday check-in",
            $"You've got {count} completed {plural} in the archive — worth exporting a report before the week wraps up?");
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // This is a background app: closing the panel hides it to the tray
        // instead of quitting. Real exit happens from the tray menu.
        e.Cancel = true;
        SaveNow();
        Hide();
    }

    public void SaveNow()
    {
        if (_data is null) return;
        _data.WindowLeft = Left;
        _data.WindowTop = Top;
        _data.WindowWidth = Width;
        _data.WindowHeight = Height;
        DataStore.Save(_data);
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        SaveNow();
        Hide();
    }

    /// <summary>
    /// Shows the panel (if hidden) and raises it above every other window, without
    /// permanently pinning it on top — so it surfaces on demand but still lets other
    /// apps (Chrome, Explorer, ...) cover it normally afterwards.
    /// </summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (!IsVisible) Show();

        Topmost = true;
        Activate();
        Topmost = false;
    }

    // --- Tab strip: lists + Archive + New ---------------------------------

    private void NewList_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;

        var dlg = new InputDialog("New list name:") { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            var list = new TaskListModel { Name = dlg.ResultText };
            _data.Lists.Add(list);
            ShowListsView();
            ListsTabControl.SelectedItem = list;
            SaveNow();
        }
    }

    private void ListsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Picking a real list tab always switches away from the archive view.
        if (_showingArchive) ShowListsView();
    }

    private void ShowArchive_Click(object sender, RoutedEventArgs e)
    {
        _showingArchive = true;
        ListContentPresenter.Visibility = Visibility.Collapsed;
        ArchivePanel.Visibility = Visibility.Visible;
        ArchiveButton.Background = ActivePillBrush;
    }

    private void ShowListsView()
    {
        _showingArchive = false;
        ArchivePanel.Visibility = Visibility.Collapsed;
        ListContentPresenter.Visibility = Visibility.Visible;
        ArchiveButton.Background = Brushes.Transparent;
    }

    private void TabHeader_Click(object sender, MouseButtonEventArgs e)
    {
        // Clicking a list tab always returns to the list view. TabControl only raises
        // SelectionChanged when the selected item actually changes, so if you were
        // looking at the Archive and click the tab that was already selected underneath
        // it, no SelectionChanged fires — this is what actually brings the list back.
        ShowListsView();

        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement { TemplatedParent: TabItem tabItem }) return;
        if (tabItem.DataContext is not TaskListModel list) return;

        RenameList(list);
    }

    private void TabHeader_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { TemplatedParent: TabItem tabItem } border) return;
        if (tabItem.DataContext is not TaskListModel list) return;

        ShowListsView();

        var menu = new ContextMenu();

        var rename = new MenuItem { Header = "Rename list" };
        rename.Click += (_, _) => RenameList(list);

        var delete = new MenuItem { Header = "Delete list" };
        delete.Click += (_, _) => DeleteList(list);

        menu.Items.Add(rename);
        menu.Items.Add(delete);

        border.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void RenameList(TaskListModel list)
    {
        var dlg = new InputDialog("Rename list:", list.Name) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            list.Name = dlg.ResultText;
            SaveNow();
        }
    }

    private void DeleteList(TaskListModel list)
    {
        if (_data is null) return;

        if (_data.Lists.Count <= 1)
        {
            MessageBox.Show(this, "You need at least one list.", "Master Tasks",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(this, $"Delete \"{list.Name}\" and all its tasks?", "Master Tasks",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (result == MessageBoxResult.Yes)
        {
            _data.Lists.Remove(list);
            SaveNow();
        }
    }

    // --- Tasks --------------------------------------------------------------

    /// <summary>
    /// Each tab's task ListBox is a fresh instance every time you switch to it, so
    /// this configures live grouping/sorting on it each time: urgent tasks (due
    /// within a week) sort to the top and get bumped under their own heading, and
    /// both re-evaluate live as IsUrgent changes (edits, or the periodic timer).
    /// </summary>
    private void TaskListBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBox listBox) return;

        var view = listBox.Items;
        view.SortDescriptions.Add(new SortDescription(nameof(TaskItem.IsUrgent), ListSortDirection.Descending));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TaskItem.IsUrgent)));
        view.IsLiveSorting = true;
        view.IsLiveGrouping = true;
        view.LiveSortingProperties.Add(nameof(TaskItem.IsUrgent));
        view.LiveGroupingProperties.Add(nameof(TaskItem.IsUrgent));
    }

    private void DueDate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskItem item }) return;

        var dlg = new DueDateDialog(item.Text, item.DueDate) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        item.DueDate = dlg.ResultDate;
        SaveNow();
        RefreshUrgencyAndNotify();
    }

    // --- Sub-tasks ------------------------------------------------------------

    private void SubTaskToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskItem item }) return;
        item.IsExpanded = !item.IsExpanded;
    }

    private void AddSubTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Parent: Panel panel }) return;
        var box = panel.Children.OfType<TextBox>().FirstOrDefault();
        if (box is not null) AddSubTaskFrom(box);
    }

    private void AddSubTask_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is TextBox box) AddSubTaskFrom(box);
    }

    private void AddSubTaskFrom(TextBox box)
    {
        if (box.DataContext is not TaskItem task) return;

        var text = box.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        task.SubTasks.Add(new SubTaskItem { Text = text, Owner = task });
        box.Clear();
        box.Focus();
        SaveNow();
    }

    private void DeleteSubTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SubTaskItem subTask }) return;
        subTask.Owner?.SubTasks.Remove(subTask);
        SaveNow();
    }

    /// <summary>Ticking a sub-task off archives just that piece — the parent task stays put.</summary>
    private void SubTaskDone_Checked(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        if (sender is not FrameworkElement { DataContext: SubTaskItem subTask }) return;

        var parent = subTask.Owner;
        parent?.SubTasks.Remove(subTask);
        _data.Archive.Insert(0, new ArchivedTask
        {
            Text = subTask.Text,
            SourceListName = parent?.Owner?.Name ?? "(unknown list)",
            ParentTaskText = parent?.Text,
        });
        UpdateArchiveEmptyHint();
        SaveNow();
    }

    /// <summary>
    /// Re-evaluates every task's urgency (so the UI re-groups even as time passes,
    /// not just on edits) and fires one tray reminder per task the first time it
    /// crosses into "due soon" — never repeating for the same due date.
    /// </summary>
    private void RefreshUrgencyAndNotify()
    {
        if (_data is null) return;

        var allTasks = _data.Lists.SelectMany(l => l.Tasks).ToList();
        foreach (var task in allTasks)
            task.RefreshUrgency();

        var app = Application.Current as App;
        foreach (var task in allTasks.Where(t => t.IsUrgent && !t.HasNotifiedUrgent))
        {
            task.HasNotifiedUrgent = true;

            var due = task.DueDate!.Value.Date;
            var when = due == DateTime.Today ? "today"
                : due < DateTime.Today ? "overdue"
                : due == DateTime.Today.AddDays(1) ? "tomorrow"
                : $"in {(due - DateTime.Today).Days} days";

            app?.ShowReminder("Task due soon", $"\"{task.Text}\" is due {when}.");
        }
    }

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Parent: Panel panel }) return;
        var box = panel.Children.OfType<TextBox>().FirstOrDefault();
        if (box is not null) AddTaskFrom(box);
    }

    private void AddTask_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is TextBox box) AddTaskFrom(box);
    }

    private void AddTaskFrom(TextBox box)
    {
        if (box.DataContext is not TaskListModel list) return;

        var text = box.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        list.Tasks.Add(new TaskItem { Text = text, Owner = list });
        box.Clear();
        box.Focus();
        SaveNow();
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TaskItem item }) return;
        item.Owner?.Tasks.Remove(item);
        SaveNow();
    }

    /// <summary>Ticking a task off archives it immediately, under its list's name.</summary>
    private void TaskDone_Checked(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        if (sender is not FrameworkElement { DataContext: TaskItem item }) return;

        var owner = item.Owner;
        var listName = owner?.Name ?? "(unknown list)";

        // Completing the overarching task also wraps up anything still open beneath
        // it, rather than silently losing those sub-tasks.
        foreach (var subTask in item.SubTasks.ToList())
        {
            _data.Archive.Insert(0, new ArchivedTask
            {
                Text = subTask.Text,
                SourceListName = listName,
                ParentTaskText = item.Text,
            });
        }
        item.SubTasks.Clear();

        owner?.Tasks.Remove(item);
        _data.Archive.Insert(0, new ArchivedTask { Text = item.Text, SourceListName = listName });
        UpdateArchiveEmptyHint();
        SaveNow();
    }

    // --- Archive --------------------------------------------------------------

    private void RestoreTask_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        if (sender is not FrameworkElement { DataContext: ArchivedTask archived }) return;

        var list = _data.Lists.FirstOrDefault(l => l.Name == archived.SourceListName);
        if (list is null)
        {
            MessageBox.Show(this, $"The list \"{archived.SourceListName}\" no longer exists, so this stays in the archive.",
                "Master Tasks", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        list.Tasks.Add(new TaskItem { Text = archived.Text, Owner = list });
        _data.Archive.Remove(archived);
        UpdateArchiveEmptyHint();
        SaveNow();
    }

    private void DeleteArchived_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;
        if (sender is not FrameworkElement { DataContext: ArchivedTask archived }) return;

        _data.Archive.Remove(archived);
        UpdateArchiveEmptyHint();
        SaveNow();
    }

    /// <summary>
    /// Exports exactly what's currently sitting in the archive right now — not the
    /// full historical archive, just this snapshot — grouped by originating list.
    /// </summary>
    private void ExportArchive_Click(object sender, RoutedEventArgs e)
    {
        if (_data is null) return;

        if (_data.Archive.Count == 0)
        {
            MessageBox.Show(this, "The archive is empty — nothing to export.", "Master Tasks",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var now = DateTime.Now;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export archive report",
            Filter = "Word Document (*.docx)|*.docx",
            FileName = $"Master Tasks Archive - {now:yyyy-MM-dd}.docx",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog(this) != true) return;

        var groups = _data.Archive
            .GroupBy(a => a.SourceListName)
            .Select(g => (ListName: g.Key, Tasks: (IReadOnlyList<string>)g.Select(a => a.Text).ToList()))
            .ToList();

        try
        {
            DocxExporter.Export(dlg.FileName, groups, now);
            MessageBox.Show(this, "Archive exported.", "Master Tasks", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't export: {ex.Message}", "Master Tasks", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateArchiveEmptyHint()
    {
        ArchiveEmptyHint.Visibility = (_data is not null && _data.Archive.Count == 0)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
