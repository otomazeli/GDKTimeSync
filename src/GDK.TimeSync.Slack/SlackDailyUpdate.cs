using System.Security.Cryptography;
using System.Text;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Slack;

public sealed record SlackDailyCompletedItem(string TogglProject, string JiraIssueKey, string Description, WorkStatus Status);

public sealed record SlackDailyUpdateOptions(string Title, string Header, IReadOnlyList<string>? ExtraLines = null);

public sealed record SlackDailyUpdate(DateOnly Date, string Text)
{
    public string ContentFingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text)));
}
