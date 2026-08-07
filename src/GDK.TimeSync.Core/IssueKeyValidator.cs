using System.Text.RegularExpressions;

namespace GDK.TimeSync.Core;

public sealed class IssueKeyValidator(IssueKeyValidationOptions options)
{
    public bool IsValid(string issueKey) => options.Patterns.Any(pattern => Regex.IsMatch(issueKey, pattern));
}
