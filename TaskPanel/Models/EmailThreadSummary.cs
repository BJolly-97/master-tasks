namespace TaskPanel.Models;

/// <summary>
/// A summarized Gmail thread shown in the Inbox tab — either a real fetched result
/// from <see cref="Services.GmailInboxService"/>, or (before you've connected an
/// account) a small set of placeholder rows showing what the tab will look like.
/// </summary>
public class EmailThreadSummary
{
    // Gmail message ID — empty for placeholder rows, populated for real fetches so
    // a manual dismissal can be remembered across refreshes.
    public string MessageId { get; set; } = string.Empty;

    public string Sender { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
    public bool IsUnread { get; set; }
    public bool IsFlagged { get; set; }

    // Filtering-only flags, not shown in the UI directly.
    public bool IsAutoReply { get; set; }
    public bool IsBulkMail { get; set; }
}
