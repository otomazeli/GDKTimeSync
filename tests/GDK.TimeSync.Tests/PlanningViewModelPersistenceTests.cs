using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;

namespace GDK.TimeSync.Tests;

public sealed class PlanningViewModelPersistenceTests
{
    [Fact]
    public async Task Templates_SeedsOnceAndSavesNewEditedTemplate()
    {
        var repository = new InMemoryTemplateRepository();
        var viewModel = new TemplatesViewModel(new TodayViewModel(), repository);

        await viewModel.InitializeAsync();
        await new TemplatesViewModel(new TodayViewModel(), repository).InitializeAsync();
        viewModel.NewTemplateCommand.Execute(null);
        var template = viewModel.Templates.Last();
        template.Name = "Daily stand-up";
        template.JiraIssueKey = "CGMFRAVII-42";
        template.Description = "Team planning";
        template.Duration = TimeSpan.FromMinutes(15);
        template.TogglProject = "CGM";
        template.TempoCategory = "DEVELOPMENT";
        await viewModel.FlushAsync();

        Assert.Equal(2, repository.Templates.Count);
        Assert.Contains(repository.Templates, saved => saved.Name == "Daily stand-up" && saved.JiraIssueKey == "CGMFRAVII-42");
    }

    [Fact]
    public async Task TodayFlushAsync_WaitsForThePendingPlanSave()
    {
        var repository = new BlockingDailyPlanRepository();
        var viewModel = new TodayViewModel(repository, new DateOnly(2026, 8, 10));
        await viewModel.InitializeAsync();

        viewModel.AddItemCommand.Execute(null);
        var flush = viewModel.FlushAsync();

        Assert.False(flush.IsCompleted);
        repository.AllowSave();
        await flush;
        Assert.NotNull(repository.SavedPlan);
    }

    [Fact]
    public async Task TemplatesInitialization_ReportsARecoverableLoadFailure()
    {
        var viewModel = new TemplatesViewModel(new TodayViewModel(), new FailingTemplateRepository());

        await viewModel.InitializeAsync();

        Assert.Contains("Could not load templates", viewModel.StatusMessage);
        Assert.Single(viewModel.Templates);
    }

    [Fact]
    public async Task TodayInitialization_ReportsARecoverableLoadFailureAndRemainsSaveable()
    {
        var repository = new RecoveringDailyPlanRepository();
        var viewModel = new TodayViewModel(repository, new DateOnly(2026, 8, 10));

        await viewModel.InitializeAsync();

        Assert.Contains("Could not load today's plan", viewModel.PersistenceError);
        Assert.True(Assert.Single(viewModel.Items).IsEditable);
        viewModel.AddItemCommand.Execute(null);
        await viewModel.FlushAsync();
        Assert.NotNull(repository.SavedPlan);
    }

    private sealed class InMemoryTemplateRepository : ITemplateRepository
    {
        public List<RecurringTaskTemplate> Templates { get; } = [];

        public Task<IReadOnlyList<RecurringTaskTemplate>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RecurringTaskTemplate>>(Templates.ToArray());

        public Task SaveAsync(RecurringTaskTemplate template, CancellationToken cancellationToken = default)
        {
            var index = Templates.FindIndex(saved => saved.Id == template.Id);
            if (index < 0) Templates.Add(template); else Templates[index] = template;
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDailyPlanRepository : IDailyPlanRepository
    {
        private readonly TaskCompletionSource savePermission = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DailyPlan? SavedPlan { get; private set; }

        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<DailyPlan?>(null);

        public async Task SaveAsync(DailyPlan plan, CancellationToken cancellationToken = default)
        {
            SavedPlan = plan;
            await savePermission.Task.WaitAsync(cancellationToken);
        }

        public void AllowSave() => savePermission.SetResult();
    }

    private sealed class FailingTemplateRepository : ITemplateRepository
    {
        public Task<IReadOnlyList<RecurringTaskTemplate>> ListAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("The database is unavailable.");

        public Task SaveAsync(RecurringTaskTemplate template, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecoveringDailyPlanRepository : IDailyPlanRepository
    {
        public DailyPlan? SavedPlan { get; private set; }

        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The database is unavailable.");

        public Task SaveAsync(DailyPlan plan, CancellationToken cancellationToken = default)
        {
            SavedPlan = plan;
            return Task.CompletedTask;
        }
    }
}
