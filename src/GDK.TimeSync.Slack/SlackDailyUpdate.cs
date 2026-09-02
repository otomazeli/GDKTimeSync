using System.Security.Cryptography;
using System.Text;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Slack;

public sealed record SlackDailyCompletedItem(string JiraIssueKey, string Description, WorkStatus Status, bool PostedToJira = true);

public sealed record SlackDailyUpdateOptions(string Title, string Header, IReadOnlyList<string>? ExtraLines = null, string JiraUser = "");

// Field names match the Data Variables of a Slack Workflow Builder "Webhook" trigger
// (not a classic Incoming Webhook, whose endpoint accepts a single free-form "text" field).
public sealed record SlackDailyUpdate(DateOnly Date, string SlackTitle, string SlackTaskHeading, string SlackExtraLines, string SlackUser)
{
    public string ContentFingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\0', SlackTitle, SlackTaskHeading, SlackExtraLines, SlackUser))));
}
