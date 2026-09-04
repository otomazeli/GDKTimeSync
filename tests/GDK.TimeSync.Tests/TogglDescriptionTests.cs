using GDK.TimeSync.Core;

namespace GDK.TimeSync.Tests;

// The Toggl description is the only field carrying the Jira key across the round trip:
// TogglSyncService.ParseDescription reads "KEY - Comment" back off an imported entry to recover it.
// The writer sent the bare comment, so an entry this app created came back from Toggl with no key --
// the reader and the writer disagreed about the format.
public sealed class TogglDescriptionTests
{
    [Fact]
    public void PrefixesTheJiraKeySoAnImportedEntryCanBeMatchedBackToItsIssue()
    {
        var item = Item("CGMFRAVII-8431", "DMP — Endpoint : vérifier l'existence d'un DMP (validate-existence)");

        Assert.Equal(
            "CGMFRAVII-8431 - DMP — Endpoint : vérifier l'existence d'un DMP (validate-existence)",
            item.TogglDescription);
    }

    // Whatever we write has to survive TogglSyncService's parser unchanged, or a synced entry drifts
    // a little further from its original on every round trip.
    [Fact]
    public void RoundTripsThroughTheSeparatorTheImporterStrips()
    {
        const string comment = "DMP — Endpoint : vérifier l'existence d'un DMP (validate-existence)";
        var description = Item("CGMFRAVII-8431", comment).TogglDescription;

        var key = description[..description.IndexOf(' ')];
        var remainder = description[(description.IndexOf(' ') + 1)..].TrimStart();
        if (remainder.Length > 0 && (remainder[0] == '-' || remainder[0] == '|'))
            remainder = remainder[1..].TrimStart();

        Assert.Equal("CGMFRAVII-8431", key);
        Assert.Equal(comment, remainder);
    }

    [Fact]
    public void DoesNotRepeatAKeyTheCommentAlreadyStartsWith()
    {
        var item = Item("CGMFRAVII-8431", "CGMFRAVII-8431 - Already written out in full");

        Assert.Equal("CGMFRAVII-8431 - Already written out in full", item.TogglDescription);
    }

    [Fact]
    public void FallsBackToTheCommentWhenThereIsNoJiraKey()
    {
        Assert.Equal("Admin, no ticket", Item("", "Admin, no ticket").TogglDescription);
    }

    [Fact]
    public void FallsBackToTheKeyWhenThereIsNoComment()
    {
        Assert.Equal("CGMFRAVII-8431", Item("CGMFRAVII-8431", "   ").TogglDescription);
    }

    [Fact]
    public void IsEmptyWhenThereIsNeither()
    {
        Assert.Equal("", Item("", "").TogglDescription);
    }

    private static PlannedWorkItem Item(string jiraIssueKey, string comment) =>
        PlannedWorkItem.Create(new DateOnly(2026, 9, 3), "Work", jiraIssueKey, comment, TimeSpan.FromMinutes(30));
}
