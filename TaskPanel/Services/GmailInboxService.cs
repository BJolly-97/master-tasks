using System.IO;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using TaskPanel.Models;

namespace TaskPanel.Services;

/// <summary>
/// Talks to the Gmail API (read-only scope) for the account you authorize, then
/// filters and labels results entirely on-device — no email content is sent
/// anywhere except to/from Google's own API.
/// </summary>
public static class GmailInboxService
{
    private static readonly string[] Scopes = { GmailService.Scope.GmailReadonly };

    // Where the OAuth client JSON you download from Google Cloud Console should be
    // saved (repo root, next to the .git folder). Deliberately outside the
    // git-tracked project files, and .gitignored regardless as a second safety net.
    public static readonly string CredentialsPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "gmail-credentials.json");

    private static readonly string TokenStorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskPanel", "google-token-cache");

    public static bool CredentialsFileExists => File.Exists(Path.GetFullPath(CredentialsPath));

    /// <summary>
    /// Fetches the most recent inbox messages, filters out likely-noise senders using
    /// local heuristics only, and flags threads that look like they need a response.
    /// Anything you've manually dismissed before is excluded too. Returns counts so
    /// the caller can show how much was filtered automatically vs. by hand.
    /// </summary>
    public static async Task<(List<EmailThreadSummary> Kept, int TotalFetched, int ManuallyDismissedCount)> FetchAndFilterAsync(
        int maxResults = 25, CancellationToken ct = default)
    {
        var service = await BuildServiceAsync(ct);

        var listRequest = service.Users.Messages.List("me");
        listRequest.LabelIds = "INBOX";
        listRequest.MaxResults = maxResults;
        var listResponse = await listRequest.ExecuteAsync(ct);

        var all = new List<EmailThreadSummary>();
        if (listResponse.Messages is not null)
        {
            foreach (var msgRef in listResponse.Messages)
            {
                var getRequest = service.Users.Messages.Get("me", msgRef.Id);
                getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                getRequest.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(
                    new[] { "From", "Subject", "Date", "Auto-Submitted", "X-Autoreply", "X-Autorespond", "List-Unsubscribe" });
                var msg = await getRequest.ExecuteAsync(ct);

                var thread = ToThread(msg);
                if (thread is not null) all.Add(thread);
            }
        }

        var dismissed = LoadDismissedIds();
        var passedHeuristic = all.Where(LooksActionable).ToList();
        var kept = passedHeuristic.Where(t => !dismissed.Contains(t.MessageId)).ToList();
        var manuallyDismissedCount = passedHeuristic.Count - kept.Count;

        return (kept, all.Count, manuallyDismissedCount);
    }

    // --- Manual dismissal, persisted locally so it survives a refresh ---------------

    private static readonly string DismissedIdsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskPanel", "dismissed-emails.json");

    private static HashSet<string> LoadDismissedIds()
    {
        try
        {
            if (File.Exists(DismissedIdsPath))
                return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(DismissedIdsPath)) ?? new();
        }
        catch
        {
            // Corrupt file — just start fresh rather than crash the app.
        }
        return new HashSet<string>();
    }

    public static void DismissEmail(string messageId)
    {
        if (string.IsNullOrEmpty(messageId)) return;

        var dismissed = LoadDismissedIds();
        if (!dismissed.Add(messageId)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(DismissedIdsPath)!);
        File.WriteAllText(DismissedIdsPath, JsonSerializer.Serialize(dismissed));
    }

    private static async Task<GmailService> BuildServiceAsync(CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(CredentialsPath);
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            GoogleClientSecrets.FromStream(stream).Secrets,
            Scopes,
            "master-tasks-user",
            ct,
            new FileDataStore(TokenStorePath, true));

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Master Tasks",
        });
    }

    private static EmailThreadSummary? ToThread(Message msg)
    {
        var headers = msg.Payload?.Headers;
        string Header(string name) =>
            headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

        var subject = Header("Subject");
        var snippet = System.Net.WebUtility.HtmlDecode(msg.Snippet ?? "");

        return new EmailThreadSummary
        {
            MessageId = msg.Id,
            Sender = ParseSenderName(Header("From")),
            Subject = string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject,
            Snippet = snippet,
            TimeLabel = ParseTimeLabel(Header("Date")),
            IsUnread = msg.LabelIds?.Contains("UNREAD") == true,
            IsAutoReply = IsAutomaticReply(
                Header("Auto-Submitted"), Header("X-Autoreply"), Header("X-Autorespond"), subject, snippet),
            IsBulkMail = !string.IsNullOrWhiteSpace(Header("List-Unsubscribe")),
        };
    }

    // RFC 3834 defines Auto-Submitted for exactly this purpose — by far the most
    // reliable signal when a mail system sets it. Most out-of-office replies
    // (Outlook/Exchange included) do. The subject/body patterns below are a
    // fallback for systems that don't bother setting the header.
    private static readonly string[] AutoReplySubjectPatterns =
    {
        "out of office", "automatic reply", "auto-reply", "auto reply", "away from",
    };

    private static readonly string[] AutoReplyBodyPatterns =
    {
        "out of office", "automatic reply", "on annual leave", "on leave and",
        "currently away", "will return on", "return to work on", "i am away",
        "for time-sensitive queries", "for immediate assistance", "i am currently out",
    };

    private static bool IsAutomaticReply(
        string autoSubmittedHeader, string xAutoreplyHeader, string xAutorespondHeader, string subject, string snippet)
    {
        if (autoSubmittedHeader.Contains("auto-replied", StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(xAutoreplyHeader) || !string.IsNullOrWhiteSpace(xAutorespondHeader)) return true;

        var subjectLower = subject.ToLowerInvariant();
        var snippetLower = snippet.ToLowerInvariant();
        return AutoReplySubjectPatterns.Any(subjectLower.Contains) || AutoReplyBodyPatterns.Any(snippetLower.Contains);
    }

    private static string ParseSenderName(string fromHeader)
    {
        // "Jordan Lee <jordan@example.com>" -> "Jordan Lee"; falls back to the raw value.
        var ltIndex = fromHeader.IndexOf('<');
        var name = ltIndex > 0 ? fromHeader[..ltIndex].Trim().Trim('"') : fromHeader.Trim();
        return string.IsNullOrWhiteSpace(name) ? fromHeader : name;
    }

    private static string ParseTimeLabel(string dateHeader)
    {
        if (!DateTime.TryParse(dateHeader, out var date)) return dateHeader;
        if (date.Date == DateTime.Today) return date.ToString("HH:mm");
        if (date.Date == DateTime.Today.AddDays(-1)) return "Yesterday";
        if (date.Date > DateTime.Today.AddDays(-7)) return date.ToString("ddd");
        return date.ToString("d MMM");
    }

    // --- Local, on-device "does this look worth showing?" heuristic ---------------

    private static readonly string[] NoiseSenderPatterns =
    {
        "noreply", "no-reply", "donotreply", "do-not-reply", "notification",
        "newsletter", "digest", "mailer", "marketing", "automated", "bounce",
        "postmaster", "updates@", "alerts@", "billing@", "payments@", "receipts@",
        "invoicing@", "accounts@", "subscriptions@",
    };

    private static readonly string[] ActionPhrases =
    {
        "please", "can you", "could you", "asap", "action required", "action needed",
        "deadline", "urgent", "reminder", "follow up", "followup", "waiting on",
        "need your", "requires your", "review", "approve", "confirm", "rsvp",
        "by friday", "by monday", "by tomorrow", "eod", "cob",
    };

    // Automated billing/subscription notices ("your trial ends", "card ending in...").
    // Weighted rather than an absolute exclusion (like auto-replies get) because a
    // genuine "please approve this invoice by Friday" should still win on balance —
    // it'll also hit ActionPhrases above and net out as actionable.
    private static readonly string[] TransactionalPatterns =
    {
        "trial ends", "trial expires", "your subscription", "your plan", "auto-renew",
        "renews on", "renewal", "card ending", "receipt for", "invoice #",
        "payment received", "payment successful", "your bill", "billing statement",
        "membership expires", "expires in", "your account will",
    };

    private static bool LooksActionable(EmailThreadSummary thread)
    {
        // Auto-replies routinely contain "please contact ..." style phrasing that
        // would otherwise score as actionable — they never actually are, so this
        // check wins regardless of anything else in the message.
        if (thread.IsAutoReply) return false;

        var score = 0;
        var senderLower = thread.Sender.ToLowerInvariant();
        var textLower = (thread.Subject + " " + thread.Snippet).ToLowerInvariant();

        if (NoiseSenderPatterns.Any(p => senderLower.Contains(p))) score -= 2;
        // List-Unsubscribe is a near-definitive "this is bulk/marketing mail" signal —
        // virtually required by anti-spam law for genuine mailing lists, and never
        // present on real one-to-one correspondence.
        if (thread.IsBulkMail) score -= 3;
        score -= TransactionalPatterns.Count(p => textLower.Contains(p)) * 2;
        if (textLower.Contains('?')) score += 1;
        score += ActionPhrases.Count(p => textLower.Contains(p)) * 2;
        if (thread.IsUnread) score += 1;

        thread.IsFlagged = score >= 3;
        return score >= 1;
    }
}
