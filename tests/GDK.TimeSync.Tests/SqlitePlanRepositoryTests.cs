using GDK.TimeSync.Core;
using GDK.TimeSync.Persistence;

namespace GDK.TimeSync.Tests;

public sealed class SqlitePlanRepositoryTests : IAsyncLifetime
{
    private readonly List<string> databasePaths = [];

    [Fact]
    public async Task SaveAsync_ReplacesThePlanForItsDateWithoutDuplicatingItems()
    {
        var repository = CreatePlanRepository();
        var item = PlannedWorkItem.Create(new DateOnly(2026, 8, 10), comment: "Initial", duration: TimeSpan.FromMinutes(30));
        var plan = DailyPlan.Create(new DateOnly(2026, 8, 10), [item]);

        await repository.SaveAsync(plan);
        await repository.SaveAsync(plan with { Items = [item with { Comment = "Updated" }] });

        var loaded = await repository.GetAsync(new DateOnly(2026, 8, 10));

        Assert.Single(loaded!.Items);
        Assert.Equal("Updated", loaded.Items[0].Comment);
    }

    [Fact]
    public async Task SaveAsync_StoresPlansForDifferentDatesIndependently()
    {
        var repository = CreatePlanRepository();
        await repository.SaveAsync(DailyPlan.Create(new DateOnly(2026, 8, 10), [PlannedWorkItem.Create(new DateOnly(2026, 8, 10), comment: "Monday")]));
        await repository.SaveAsync(DailyPlan.Create(new DateOnly(2026, 8, 11), [PlannedWorkItem.Create(new DateOnly(2026, 8, 11), comment: "Tuesday")]));

        var first = await repository.GetAsync(new DateOnly(2026, 8, 10));
        var second = await repository.GetAsync(new DateOnly(2026, 8, 11));

        Assert.Equal("Monday", Assert.Single(first!.Items).Comment);
        Assert.Equal("Tuesday", Assert.Single(second!.Items).Comment);
    }

    [Fact]
    public async Task SaveAsync_StoresTemplatesForLaterListing()
    {
        var repository = CreateTemplateRepository();
        var template = RecurringTaskTemplate.Create("Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");

        await repository.SaveAsync(template);

        var loaded = Assert.Single(await repository.ListAsync());
        Assert.Equal(template, loaded);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var databasePath in databasePaths)
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }

        return Task.CompletedTask;
    }

    private SqliteDailyPlanRepository CreatePlanRepository()
    {
        var database = new SqliteDatabase(CreateDatabasePath());
        return new SqliteDailyPlanRepository(database);
    }

    private SqliteTemplateRepository CreateTemplateRepository()
    {
        var database = new SqliteDatabase(CreateDatabasePath());
        return new SqliteTemplateRepository(database);
    }

    private string CreateDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.Tests.{Guid.NewGuid():N}.db");
        databasePaths.Add(path);
        return path;
    }
}
