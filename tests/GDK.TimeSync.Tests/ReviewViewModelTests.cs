using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop;
using GDK.TimeSync.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GDK.TimeSync.Tests;

public sealed class ReviewViewModelTests
{
    [Fact]
    public void DryRun_ForAValidLocalPlan_ReportsSequenceWithoutUsingDelivery()
    {
        var plan = DailyPlan.Create(DateOnly.FromDateTime(DateTime.Today), [
            PlannedWorkItem.Create(DateOnly.FromDateTime(DateTime.Today), jiraIssueKey: "CGMFRAVII-1", duration: TimeSpan.FromMinutes(30))
        ]);
        var review = new ReviewViewModel(new FixedPlanSnapshotProvider(plan));

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
        var review = new ReviewViewModel(new FixedPlanSnapshotProvider(plan));

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

    [Fact]
    public void DryRun_WithTodaySnapshot_DoesNotReadOrWritePersistence()
    {
        var plan = DailyPlan.Create(DateOnly.FromDateTime(DateTime.Today), [
            PlannedWorkItem.Create(DateOnly.FromDateTime(DateTime.Today), jiraIssueKey: "CGMFRAVII-1", duration: TimeSpan.FromMinutes(30))
        ]);
        var repository = new ThrowingDailyPlanRepository();
        var today = new TodayViewModel(repository);
        today.Items.Add(new PlannedWorkItemViewModel("Work", "CGMFRAVII-1", duration: TimeSpan.FromMinutes(30)));
        var review = new ReviewViewModel(today);

        review.DryRunCommand.Execute(null);

        Assert.Equal(0, repository.GetCount);
        Assert.Equal(0, repository.SaveCount);
        Assert.True(today.FlushAsync().IsCompletedSuccessfully);
        Assert.False(review.PostAllCommand.CanExecute(null));
    }

    [Fact]
    public void Review_ConstructionAndRegistration_UseOnlyLocalPlanSnapshotProvider()
    {
        var constructor = Assert.Single(typeof(ReviewViewModel).GetConstructors());
        Assert.Equal([typeof(ILocalPlanSnapshotProvider)], constructor.GetParameters().Select(parameter => parameter.ParameterType));

        var services = new ServiceCollection();
        services.AddSingleton(new TodayViewModel(new ThrowingDailyPlanRepository()));
        App.RegisterReviewServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ReviewViewModel>(provider.GetRequiredService<ReviewViewModel>());
    }

    private sealed class FixedPlanSnapshotProvider(DailyPlan plan) : ILocalPlanSnapshotProvider
    {
        public DailyPlan GetSnapshot() => plan;
    }

    private sealed class ThrowingDailyPlanRepository : IDailyPlanRepository
    {
        public int GetCount { get; private set; }
        public int SaveCount { get; private set; }

        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            GetCount++;
            throw new InvalidOperationException("Dry Run must not read persistence.");
        }

        public Task SaveAsync(DailyPlan plan, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            throw new InvalidOperationException("Dry Run must not write persistence.");
        }
    }
}
