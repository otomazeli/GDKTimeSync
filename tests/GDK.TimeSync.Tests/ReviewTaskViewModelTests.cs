using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class ReviewTaskViewModelTests
{
    private static PlannedWorkItem Item(bool postToToggl = true, long? togglEntryId = null) =>
        PlannedWorkItem.Create(new DateOnly(2026, 9, 1), "Work", "CGM-1", "Comment",
            TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT", start: new TimeOnly(9, 0), end: new TimeOnly(9, 30))
            with { PostToToggl = postToToggl, TogglEntryId = togglEntryId };

    [Fact]
    public void AFreshTaskIsPendingEverywhereAndSelectedByDefault()
    {
        var row = new ReviewTaskViewModel(Item());

        Assert.Equal(DeliveryMark.Pending, row.Toggl);
        Assert.Equal(DeliveryMark.Pending, row.Jira);
        Assert.Equal(DeliveryMark.Pending, row.Tempo);
        Assert.True(row.CanPost);
        Assert.True(row.IsSelected);
        Assert.Null(row.FailureText);
    }

    // Still ticked after a successful delivery: the tick also decides what goes into the Slack
    // update, which is composed after posting. CanPost is what stops it being posted twice.
    [Fact]
    public void ASucceededTaskShowsAllThreeDeliveredAndCannotBePostedButStaysTicked()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, 101, 201,
            DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported));

        Assert.Equal(DeliveryMark.Delivered, row.Toggl);
        Assert.Equal(DeliveryMark.Delivered, row.Jira);
        Assert.Equal(DeliveryMark.Delivered, row.Tempo);
        Assert.False(row.CanPost);
        Assert.True(row.IsSelected);
    }

    // Delivery is ordered Toggl -> Jira -> Tempo, so a Tempo failure proves Jira validated.
    [Fact]
    public void ATempoFailureMarksTogglAndJiraDeliveredAndTempoFailed()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, 101, null,
            DeliveryAttemptStatus.Failed, DeliveryFailureCode.TempoFailed, SlackDeliveryState.NotSupported)
            { FailureDetail = "User is invalid" });

        Assert.Equal(DeliveryMark.Delivered, row.Toggl);
        Assert.Equal(DeliveryMark.Delivered, row.Jira);
        Assert.Equal(DeliveryMark.Failed, row.Tempo);
        Assert.Equal("Tempo: User is invalid", row.FailureText);
    }

    // Each fixture matches the attempt shape delivery would really produce for that code: delivery is
    // ordered Toggl -> Jira -> Tempo, so a Jira/Tempo failure can only occur once Toggl has already
    // succeeded and TogglEntryId is set.
    [Theory]
    [InlineData(DeliveryFailureCode.TogglFailed, null, "Toggl: Toggl delivery failed.")]
    [InlineData(DeliveryFailureCode.JiraFailed, 101L, "Jira: Jira delivery failed.")]
    [InlineData(DeliveryFailureCode.JiraIssueNotFound, 101L, "Jira: Jira issue was not found.")]
    [InlineData(DeliveryFailureCode.TempoFailed, 101L, "Tempo: Tempo delivery failed.")]
    public void WithoutDetailTheFailureTextFallsBackToTheCodedReason(DeliveryFailureCode code, long? togglEntryId, string expected)
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, togglEntryId, null,
            DeliveryAttemptStatus.Failed, code, SlackDeliveryState.NotSupported));

        Assert.Equal(expected, row.FailureText);
    }

    [Fact]
    public void ATogglFailureMarksTogglFailedAndLeavesJiraAndTempoPending()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, null, null,
            DeliveryAttemptStatus.Failed, DeliveryFailureCode.TogglFailed, SlackDeliveryState.NotSupported));

        Assert.Equal(DeliveryMark.Failed, row.Toggl);
        Assert.Equal(DeliveryMark.Pending, row.Jira);
        Assert.Equal(DeliveryMark.Pending, row.Tempo);
    }

    [Theory]
    [InlineData(DeliveryFailureCode.JiraFailed)]
    [InlineData(DeliveryFailureCode.JiraIssueNotFound)]
    public void AJiraFailureMarksTogglDeliveredJiraFailedAndTempoPending(DeliveryFailureCode code)
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, 101, null,
            DeliveryAttemptStatus.Failed, code, SlackDeliveryState.NotSupported));

        Assert.Equal(DeliveryMark.Delivered, row.Toggl);
        Assert.Equal(DeliveryMark.Failed, row.Jira);
        Assert.Equal(DeliveryMark.Pending, row.Tempo);
    }

    // Cancelled blames no step in the derivation table: all three marks stay Pending so a later
    // change to Mark's failedHere lists cannot silently start blaming one.
    [Fact]
    public void ACancelledAttemptLeavesAllThreeMarksPending()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, null, null,
            DeliveryAttemptStatus.Cancelled, DeliveryFailureCode.Cancelled, SlackDeliveryState.NotSupported));

        Assert.Equal(DeliveryMark.Pending, row.Toggl);
        Assert.Equal(DeliveryMark.Pending, row.Jira);
        Assert.Equal(DeliveryMark.Pending, row.Tempo);
    }

    // Regression guard for the Task 1 defect: PostAllCoordinator.RequiresManualReconciliation builds
    // its attempt with `attempt with { ... }`, so a stale FailureDetail from an earlier Tempo/Jira
    // failure can survive onto an attempt whose FailureCode has since been changed to
    // PersistenceFailed. FailureDetail must only be trusted for the codes PostAllCoordinator actually
    // pairs a message with (JiraFailed, JiraIssueNotFound, TempoFailed) -- never for PersistenceFailed.
    [Fact]
    public void APersistenceFailureWithAStaleDetailReportsTheCodedReasonNotTheStaleMessage()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, 101, 201,
            DeliveryAttemptStatus.Failed, DeliveryFailureCode.PersistenceFailed, SlackDeliveryState.NotSupported)
            { FailureDetail = "User is invalid" });

        Assert.Equal("Delivery: Delivery state could not be saved.", row.FailureText);
    }

    // An item neither marked for Toggl nor already linked to an entry cannot be delivered at all.
    // It can still be reported in Slack, so it stays tickable and only CanPost says no.
    [Fact]
    public void ATaskThatCannotBeDeliveredCannotBePosted()
    {
        var row = new ReviewTaskViewModel(Item(postToToggl: false));

        Assert.False(row.CanPost);
        Assert.True(row.IsSelected);
    }

    [Fact]
    public void ATaskNotMarkedForTogglButAlreadyLinkedCanStillBePosted()
    {
        var row = new ReviewTaskViewModel(Item(postToToggl: false, togglEntryId: 555));

        Assert.True(row.CanPost);
    }

    // The tick is the user's own choice about scope now, so nothing overrides it in either direction.
    [Fact]
    public void UntickingAndRetickingARowThatCannotBePostedIsRespected()
    {
        var row = new ReviewTaskViewModel(Item(postToToggl: false));

        row.IsSelected = false;
        Assert.False(row.IsSelected);

        row.IsSelected = true;
        Assert.True(row.IsSelected);
    }

    [Fact]
    public void ApplyAttemptUpdatesTheMarksAndClosesASucceededRowToFurtherPosting()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item);

        row.ApplyAttempt(new DeliveryAttempt(item.Id, 101, 201,
            DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported));

        Assert.Equal(DeliveryMark.Delivered, row.Tempo);
        Assert.False(row.CanPost);
        Assert.True(row.IsSelected);
    }
}
