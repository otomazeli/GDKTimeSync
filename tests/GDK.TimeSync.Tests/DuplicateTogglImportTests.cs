using GDK.TimeSync.Core;
using GDK.TimeSync.Desktop.ViewModels;
using GDK.TimeSync.Persistence;

namespace GDK.TimeSync.Tests;

// Three instances running against one database on 2026-09-08 wrote three rows per Toggl entry, each
// with its own item id. These cover the two guards that came out of it: the merge no longer accepts a
// second copy of an entry it already holds, and an existing file is repaired on open.
public sealed class DuplicateTogglImportTests : IDisposable
{
    private readonly List<string> databasePaths = [];

    [Fact]
    public async Task SaveConflict_DoesNotMergeARemoteCopyOfATogglEntryAlreadyListed()
    {
        var date = new DateOnly(2026, 9, 8);
        var local = PlannedWorkItem.Create(date, "Meeting", "CGMFRAVII-2763", "Agile Meetings") with { TogglEntryId = 4546309006 };
        // What another instance wrote for the same Toggl entry: same work, a different item id.
        var remoteCopy = PlannedWorkItem.Create(date, "Meeting", "CGMFRAVII-2763", "Agile Meetings") with { TogglEntryId = 4546309006 };
        var repository = new ConflictingRepository(DailyPlan.Create(date, [local]) with { Version = 1 })
        {
            FailSaveTimes = 1,
            OnConflict = current => current with { Items = [.. current.Items, remoteCopy], Version = current.Version + 1 }
        };
        var today = new TodayViewModel(repository, date);
        await today.InitializeAsync();

        today.Items.Single().Name = "Meeting (edited)";
        await today.FlushAsync();

        Assert.Null(today.PersistenceError);
        Assert.Equal(local.Id, Assert.Single(today.Items).Id);
        Assert.Equal(local.Id, Assert.Single(Assert.Single(repository.SavedPlans).Items).Id);
    }

    [Fact]
    public async Task OpeningAnExistingDatabase_CollapsesDuplicateRowsForOneTogglEntry()
    {
        var date = new DateOnly(2026, 9, 8);
        var first = PlannedWorkItem.Create(date, "Meeting", "CGMFRAVII-2763", "Agile Meetings") with { TogglEntryId = 4546309006 };
        var seedPath = CreateDatabasePath();
        await new SqliteDailyPlanRepository(new SqliteDatabase(seedPath)).SaveAsync(DailyPlan.Create(date,
        [
            first,
            first with { Id = Guid.NewGuid() },
            first with { Id = Guid.NewGuid() },
            // A different entry on the same day, and a row with no Toggl entry at all: both stay.
            PlannedWorkItem.Create(date, "Other", "CGMFRAVII-8431", "Other") with { TogglEntryId = 4546309063 },
            PlannedWorkItem.Create(date, "Typed here", "CGMFRAVII-8432", "Not imported")
        ]));

        // A copy stands in for the next launch: the repair runs when a process first opens the file.
        var reopenedPath = CreateDatabasePath();
        File.Copy(seedPath, reopenedPath);
        var plan = await new SqliteDailyPlanRepository(new SqliteDatabase(reopenedPath)).GetAsync(date);

        Assert.Equal(3, plan!.Items.Count);
        Assert.Equal(first.Id, Assert.Single(plan.Items, item => item.TogglEntryId == 4546309006).Id);
        Assert.Single(plan.Items, item => item.TogglEntryId == 4546309063);
        Assert.Single(plan.Items, item => item.TogglEntryId is null);
    }

    private string CreateDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.Tests.{Guid.NewGuid():N}.db");
        databasePaths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in databasePaths)
            try { File.Delete(path); } catch (IOException) { }
    }

    private sealed class ConflictingRepository(DailyPlan plan) : IDailyPlanRepository
    {
        public List<DailyPlan> SavedPlans { get; } = [];
        public int FailSaveTimes { get; set; }
        public Func<DailyPlan, DailyPlan>? OnConflict { get; set; }

        public Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<DailyPlan?>(plan);

        public Task SaveAsync(DailyPlan value, CancellationToken cancellationToken = default)
        {
            if (FailSaveTimes > 0)
            {
                FailSaveTimes--;
                if (OnConflict is not null) plan = OnConflict(plan);
                throw new PlanConcurrencyException(value.Date);
            }

            plan = value with { Version = value.Version + 1 };
            SavedPlans.Add(value);
            return Task.CompletedTask;
        }
    }
}
