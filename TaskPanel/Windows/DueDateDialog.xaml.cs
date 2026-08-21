using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace TaskPanel.Windows;

public partial class DueDateDialog : Window
{
    public DateTime? ResultDate { get; private set; }

    public DueDateDialog(string taskText, DateTime? currentDueDate)
    {
        InitializeComponent();

        var shortText = taskText.Length > 40 ? taskText[..40] + "…" : taskText;
        PromptText.Text = $"Due date for: \"{shortText}\"";
        DateBox.Text = currentDueDate?.ToString("yyyy-MM-dd") ?? string.Empty;

        Loaded += (_, _) =>
        {
            DateBox.Focus();
            DateBox.SelectAll();
        };
    }

    private void QuickPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tagText }) return;
        if (!int.TryParse(tagText, out var daysFromToday)) return;

        DateBox.Text = DateTime.Today.AddDays(daysFromToday).ToString("yyyy-MM-dd");
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => TrySave();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ResultDate = null;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DateBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TrySave();
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void TrySave()
    {
        var text = DateBox.Text.Trim();
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ResultDate = parsed.Date;
        DialogResult = true;
        Close();
    }
}
