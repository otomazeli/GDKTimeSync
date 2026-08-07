namespace GDK.TimeSync.Core;

public sealed class IssueKeyValidationOptions
{
    public IList<string> Patterns { get; } = ["^[A-Z][A-Z0-9]*-\\d+$"];
}
