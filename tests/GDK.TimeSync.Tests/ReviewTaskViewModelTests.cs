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
        Assert.True(row.CanSelect);
        Assert.True(row.IsSelected);
        Assert.Null(row.FailureText);
    }

    [Fact]
    public void ASucceededTaskShowsAllThreeDeliveredAndCannotBeSelected()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, 101, 201,
            DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported));

        Assert.Equal(DeliveryMark.Delivered, row.Toggl);
        Assert.Equal(DeliveryMark.Delivered, row.Jira);
        Assert.Equal(DeliveryMark.Delivered, row.Tempo);
        Assert.False(row.CanSelect);
        Assert.False(row.IsSelected);
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

    [Theory]
    [InlineData(DeliveryFailureCode.TogglFailed, "Toggl: Toggl delivery failed.")]
    [InlineData(DeliveryFailureCode.JiraFailed, "Jira: Jira delivery failed.")]
    [InlineData(DeliveryFailureCode.JiraIssueNotFound, "Jira: Jira issue was not found.")]
    [InlineData(DeliveryFailureCode.TempoFailed, "Tempo: Tempo delivery failed.")]
    public void WithoutDetailTheFailureTextFallsBackToTheCodedReason(DeliveryFailureCode code, string expected)
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item, new DeliveryAttempt(item.Id, null, null,
            DeliveryAttemptStatus.Failed, code, SlackDeliveryState.NotSupported));

        Assert.Equal(expected, row.FailureText);
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

    // The existing per-task guard: an item neither marked for Toggl nor already linked to an entry
    // cannot be delivered at all, so the grid must not offer it.
    [Fact]
    public void ATaskThatCannotBeDeliveredCannotBeSelected()
    {
        var row = new ReviewTaskViewModel(Item(postToToggl: false));

        Assert.False(row.CanSelect);
        Assert.False(row.IsSelected);
    }

    [Fact]
    public void ATaskNotMarkedForTogglButAlreadyLinkedCanStillBeSelected()
    {
        var row = new ReviewTaskViewModel(Item(postToToggl: false, togglEntryId: 555));

        Assert.True(row.CanSelect);
    }

    [Fact]
    public void SettingIsSelectedOnAnUnselectableRowIsIgnored()
    {
        var row = new ReviewTaskViewModel(Item(postToToggl: false));

        row.IsSelected = true;

        Assert.False(row.IsSelected);
    }

    [Fact]
    public void ApplyAttemptUpdatesTheMarksAndDeselectsASucceededRow()
    {
        var item = Item();
        var row = new ReviewTaskViewModel(item);

        row.ApplyAttempt(new DeliveryAttempt(item.Id, 101, 201,
            DeliveryAttemptStatus.Succeeded, null, SlackDeliveryState.NotSupported));

        Assert.Equal(DeliveryMark.Delivered, row.Tempo);
        Assert.False(row.IsSelected);
        Assert.False(row.CanSelect);
    }
}
