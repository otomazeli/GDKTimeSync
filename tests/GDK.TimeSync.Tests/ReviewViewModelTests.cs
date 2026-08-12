using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class ReviewViewModelTests
{
    [Fact]
    public void DryRun_ForAValidLocalPlan_ReportsSequenceWithoutUsingDelivery()
    {
        var plan = DailyPlan.Create(DateOnly.FromDateTime(DateTime.Today), [
            PlannedWorkItem.Create(DateOnly.FromDateTime(DateTime.Today), jiraIssueKey: "CGMFRAVII-1", duration: TimeSpan.FromMinutes(30))
        ]);
        var review = new ReviewViewModel(() => plan);

        review.DryRunCommand.Execute(null);

        Assert.Contains("Toggl", review.DryRunSummary);
        Assert.Empty(review.DryRunBlockers);
        Assert.False(review.PostAllCommand.CanExecute(null));
    }

    [Fact]
    public void ConfirmReview_ShowsPreviewWithoutEnablingPostAll()
    {
        var review = new ReviewViewModel();

        review.ConfirmReviewCommand.Execute(null);

        Assert.True(review.IsConfirmationVisible);
        Assert.False(review.PostAllCommand.CanExecute(null));
    }

    [Fact]
    public void DryRun_ForInvalidLocalPlan_ReportsOnlyLocalBlockers()
    {
        var plan = DailyPlan.Create(DateOnly.FromDateTime(DateTime.Today), [
            PlannedWorkItem.Create(DateOnly.FromDateTime(DateTime.Today), jiraIssueKey: "", duration: TimeSpan.Zero, start: new TimeOnly(10, 0), end: new TimeOnly(9, 0))
        ]);
        var review = new ReviewViewModel(() => plan);

        review.DryRunCommand.Execute(null);

        Assert.Equal(3, review.DryRunBlockers.Count);
    }

    [Fact]
    public void PostAll_IsUnavailableBeforeDeliveryWorkflowExists()
    {
        var review = new ReviewViewModel();

        Assert.False(review.CanPostAll);
        Assert.False(review.PostAllCommand.CanExecute(null));
    }
}
