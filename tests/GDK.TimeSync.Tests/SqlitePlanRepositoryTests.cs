using GDK.TimeSync.Core;
using GDK.TimeSync.Persistence;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

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

    [Fact]
    public void Create_DefaultsWorkStatusToInProgress()
    {
        var item = PlannedWorkItem.Create(new DateOnly(2026, 8, 13));
        var template = RecurringTaskTemplate.Create("Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT");

        Assert.Equal(WorkStatus.InProgress, item.Status);
        Assert.Equal(WorkStatus.InProgress, template.Status);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsPlanWorkStatus()
    {
        var repository = CreatePlanRepository();
        var date = new DateOnly(2026, 8, 13);
        var plan = DailyPlan.Create(date, [PlannedWorkItem.Create(date) with { Status = WorkStatus.Done }]);

        await repository.SaveAsync(plan);

        Assert.Equal(WorkStatus.Done, Assert.Single((await repository.GetAsync(date))!.Items).Status);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsTogglProjectIdentityAndPostingIntent()
    {
        var repository = CreatePlanRepository();
        var date = new DateOnly(2026, 8, 13);
        var item = PlannedWorkItem.Create(date, start: new TimeOnly(8, 15), end: new TimeOnly(8, 45)) with
        {
            TogglProjectId = 77,
            PostToToggl = false
        };

        await repository.SaveAsync(DailyPlan.Create(date, [item]));

        var loaded = Assert.Single((await repository.GetAsync(date))!.Items);
        Assert.Equal(77, loaded.TogglProjectId);
        Assert.False(loaded.PostToToggl);
        Assert.Equal(new TimeOnly(8, 15), loaded.Start);
        Assert.Equal(new TimeOnly(8, 45), loaded.End);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsTogglEntryLinkAndSource()
    {
        var repository = CreatePlanRepository();
        var date = new DateOnly(2026, 8, 24);
        var item = PlannedWorkItem.Create(date) with { TogglEntryId = 555, Source = ItemSource.Toggl };

        await repository.SaveAsync(DailyPlan.Create(date, [item]));

        var loaded = Assert.Single((await repository.GetAsync(date))!.Items);
        Assert.Equal(555, loaded.TogglEntryId);
        Assert.Equal(ItemSource.Toggl, loaded.Source);
    }

    [Fact]
    public async Task SaveAsync_DefaultsTogglEntryLinkAndSourceWhenNotSet()
    {
        var repository = CreatePlanRepository();
        var date = new DateOnly(2026, 8, 24);
        var item = PlannedWorkItem.Create(date);

        await repository.SaveAsync(DailyPlan.Create(date, [item]));

        var loaded = Assert.Single((await repository.GetAsync(date))!.Items);
        Assert.Null(loaded.TogglEntryId);
        Assert.Equal(ItemSource.Local, loaded.Source);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsTemplateWorkStatus()
    {
        var repository = CreateTemplateRepository();
        var template = RecurringTaskTemplate.Create("Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT") with { Status = WorkStatus.Waiting };

        await repository.SaveAsync(template);

        Assert.Equal(WorkStatus.Waiting, Assert.Single(await repository.ListAsync()).Status);
    }

    [Fact]
    public async Task ExistingPlanRows_MigrateToInProgressWorkStatus()
    {
        var databasePath = CreateDatabasePath();
        await CreateLegacyPlanDatabaseAsync(databasePath);
        var repository = new SqliteDailyPlanRepository(new SqliteDatabase(databasePath));

        var loaded = await repository.GetAsync(new DateOnly(2026, 8, 13));

        Assert.Equal(WorkStatus.InProgress, Assert.Single(loaded!.Items).Status);
    }

    [Fact]
    public async Task ExistingTemplateRows_MigrateToInProgressWorkStatus()
    {
        var databasePath = CreateDatabasePath();
        await CreateLegacyPlanDatabaseAsync(databasePath);
        var repository = new SqliteTemplateRepository(new SqliteDatabase(databasePath));

        var loaded = await repository.ListAsync();

        Assert.Equal(WorkStatus.InProgress, Assert.Single(loaded).Status);
    }

    [Fact]
    public async Task InvalidPersistedWorkStatus_IsReadAsInProgress()
    {
        var databasePath = CreateDatabasePath();
        await CreateDatabaseWithInvalidWorkStatusAsync(databasePath);

        var plan = await new SqliteDailyPlanRepository(new SqliteDatabase(databasePath)).GetAsync(new DateOnly(2026, 8, 13));
        var template = Assert.Single(await new SqliteTemplateRepository(new SqliteDatabase(databasePath)).ListAsync());

        Assert.Equal(WorkStatus.InProgress, Assert.Single(plan!.Items).Status);
        Assert.Equal(WorkStatus.InProgress, template.Status);
    }

    [Fact]
    public async Task InvalidPersistedWorkStatus_IsNormalizedOnDiskDuringMigration()
    {
        var databasePath = CreateDatabasePath();
        await CreateDatabaseWithInvalidWorkStatusAsync(databasePath);

        await using (var connection = await new SqliteDatabase(databasePath).OpenConnectionAsync())
        {
        }

        await using var rawConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await rawConnection.OpenAsync();
        await using var command = rawConnection.CreateCommand();
        command.CommandText = "SELECT work_status FROM planned_work_items UNION ALL SELECT work_status FROM recurring_task_templates";
        await using var reader = await command.ExecuteReaderAsync();
        var statuses = new List<int>();

        while (await reader.ReadAsync())
            statuses.Add(reader.GetInt32(0));

        Assert.Equal([(int)WorkStatus.InProgress, (int)WorkStatus.InProgress], statuses);
    }

    [Fact]
    public async Task SaveAsync_RejectsUndefinedPlanWorkStatus()
    {
        var repository = CreatePlanRepository();
        var date = new DateOnly(2026, 8, 13);
        var plan = DailyPlan.Create(date, [PlannedWorkItem.Create(date) with { Status = (WorkStatus)999 }]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SaveAsync(plan));
    }

    [Fact]
    public async Task SaveAsync_RejectsUndefinedTemplateWorkStatus()
    {
        var repository = CreateTemplateRepository();
        var template = RecurringTaskTemplate.Create("Knowledge transfer", "CGMFRAVII-2767", "Knowledge transfer", TimeSpan.FromMinutes(30), "CGM", "DEVELOPMENT") with { Status = (WorkStatus)999 };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.SaveAsync(template));
    }

    [Fact]
    public async Task NewSchema_RejectsInvalidWorkStatuses()
    {
        var database = new SqliteDatabase(CreateDatabasePath());
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO daily_plans(plan_date) VALUES ('2026-08-13'); INSERT INTO planned_work_items(id, plan_date, start_time, end_time, name, jira_issue_key, comment, duration_seconds, toggl_project, tempo_category, is_billable, work_status) VALUES ('00000000-0000-0000-0000-000000000001', '2026-08-13', NULL, NULL, 'Knowledge transfer', 'CGMFRAVII-2767', 'Knowledge transfer', 1800, 'CGM', 'DEVELOPMENT', 1, 999);";

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());

        command.CommandText = """
            INSERT INTO recurring_task_templates(id, name, jira_issue_key, description, duration_seconds, toggl_project, tempo_category, is_billable, work_status)
            VALUES ('00000000-0000-0000-0000-000000000002', 'Knowledge transfer', 'CGMFRAVII-2767', 'Knowledge transfer', 1800, 'CGM', 'DEVELOPMENT', 1, 999)
            """;

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task SeparateProcessLock_BlocksMigrationUntilReleased()
    {
        var databasePath = CreateDatabasePath();
        await CreateLegacyPlanDatabaseAsync(databasePath);
        var readyPath = CreateTemporaryPath();
        var releasePath = CreateTemporaryPath();
        using var probe = StartMigrationProbe(databasePath, readyPath, releasePath);
        using var parentCancellation = new CancellationTokenSource();
        var parentStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<SqliteConnection>? parentOpen = null;

        try
        {
            await WaitForFileAsync(readyPath);
            parentOpen = Task.Run(async () =>
            {
                try
                {
                    var open = new SqliteDatabase(databasePath).OpenConnectionAsync(parentCancellation.Token);
                    parentStarted.TrySetResult();
                    return await open;
                }
                catch (Exception exception)
                {
                    parentStarted.TrySetException(exception);
                    throw;
                }
            });
            await parentStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var completed = await Task.WhenAny(parentOpen, Task.Delay(TimeSpan.FromMilliseconds(250)));

            Assert.NotSame(parentOpen, completed);
            Assert.False(probe.HasExited);

            await File.WriteAllTextAsync(releasePath, "release");
            await parentOpen.WaitAsync(TimeSpan.FromSeconds(10));
            await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(0, probe.ExitCode);
            var plan = await new SqliteDailyPlanRepository(new SqliteDatabase(databasePath)).GetAsync(new DateOnly(2026, 8, 13));
            var template = Assert.Single(await new SqliteTemplateRepository(new SqliteDatabase(databasePath)).ListAsync());
            Assert.Equal(WorkStatus.InProgress, Assert.Single(plan!.Items).Status);
            Assert.Equal(WorkStatus.InProgress, template.Status);
        }
        finally
        {
            await ReleaseProbeAsync(releasePath);
            await StopProbeAsync(probe);
            await ObserveParentOpenAsync(parentOpen, parentCancellation);
        }
    }

    [Fact]
    public async Task OpenConnectionAsync_SkipsTheMigrationProbeOnceADatabaseIsAlreadyMigrated()
    {
        var databasePath = CreateDatabasePath();
        await CreateLegacyPlanDatabaseAsync(databasePath);
        var database = new SqliteDatabase(databasePath);
        await using (var warmup = await database.OpenConnectionAsync())
        {
        }

        var readyPath = CreateTemporaryPath();
        var releasePath = CreateTemporaryPath();
        using var probe = StartMigrationProbe(databasePath, readyPath, releasePath);
        Task<SqliteConnection>? secondOpen = null;

        try
        {
            await WaitForFileAsync(readyPath);

            secondOpen = database.OpenConnectionAsync();
            var completed = await Task.WhenAny(secondOpen, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(secondOpen, completed);
        }
        finally
        {
            await ReleaseProbeAsync(releasePath);
            await StopProbeAsync(probe);
            if (secondOpen is not null)
            {
                await using var connection = await secondOpen;
            }
        }
    }

    [Fact]
    public async Task DistinctDatabaseInitializers_MigrateTheSameLegacyDatabase()
    {
        var databasePath = CreateDatabasePath();
        await CreateLegacyPlanDatabaseAsync(databasePath);
        var databases = Enumerable.Range(0, 4).Select(_ => new SqliteDatabase(databasePath)).ToArray();

        var connections = await Task.WhenAll(databases.Select(database => database.OpenConnectionAsync()));
        foreach (var connection in connections)
            await connection.DisposeAsync();

        var plan = await new SqliteDailyPlanRepository(new SqliteDatabase(databasePath)).GetAsync(new DateOnly(2026, 8, 13));
        Assert.Equal(WorkStatus.InProgress, Assert.Single(plan!.Items).Status);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
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

    private string CreateTemporaryPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.Tests.{Guid.NewGuid():N}.signal");
        databasePaths.Add(path);
        return path;
    }

    private static Process StartMigrationProbe(string databasePath, string readyPath, string startPath)
    {
        var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add(typeof(MigrationProbeProgram).Assembly.Location);
        startInfo.ArgumentList.Add("--migration-probe");
        startInfo.ArgumentList.Add(databasePath);
        startInfo.ArgumentList.Add(readyPath);
        startInfo.ArgumentList.Add(startPath);
        return Process.Start(startInfo)!;
    }

    private static async Task WaitForFileAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(File.Exists(path), "Migration probe did not signal readiness.");
    }

    private static async Task ReleaseProbeAsync(string releasePath)
    {
        try
        {
            if (!File.Exists(releasePath))
                await File.WriteAllTextAsync(releasePath, "release");
        }
        catch
        {
        }
    }

    private static async Task StopProbeAsync(Process probe)
    {
        var probeExited = probe.WaitForExitAsync();
        if (!probe.HasExited && await Task.WhenAny(probeExited, Task.Delay(TimeSpan.FromSeconds(10))) != probeExited)
        {
            try
            {
                if (!probe.HasExited)
                    probe.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (!probe.HasExited)
        {
            try
            {
                await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
            }
        }
    }

    private static async Task ObserveParentOpenAsync(Task<SqliteConnection>? parentOpen, CancellationTokenSource cancellation)
    {
        if (parentOpen is null)
            return;

        if (await Task.WhenAny(parentOpen, Task.Delay(TimeSpan.FromSeconds(10))) != parentOpen)
        {
            cancellation.Cancel();
            if (await Task.WhenAny(parentOpen, Task.Delay(TimeSpan.FromSeconds(10))) != parentOpen)
            {
                _ = parentOpen.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully)
                        task.Result.Dispose();
                    else
                        _ = task.Exception;
                }, TaskScheduler.Default);
                return;
            }
        }

        if (parentOpen.IsCompleted)
        {
            try
            {
                await using var connection = await parentOpen;
            }
            catch
            {
            }
        }
    }

    private static async Task CreateLegacyPlanDatabaseAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE daily_plans (plan_date TEXT PRIMARY KEY);
            CREATE TABLE planned_work_items (
                id TEXT PRIMARY KEY,
                plan_date TEXT NOT NULL,
                start_time TEXT NULL,
                end_time TEXT NULL,
                name TEXT NOT NULL,
                jira_issue_key TEXT NOT NULL,
                comment TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL,
                toggl_project TEXT NOT NULL,
                tempo_category TEXT NOT NULL,
                is_billable INTEGER NOT NULL);
            CREATE TABLE recurring_task_templates (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                jira_issue_key TEXT NOT NULL,
                description TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL,
                toggl_project TEXT NOT NULL,
                tempo_category TEXT NOT NULL,
                is_billable INTEGER NOT NULL);
            INSERT INTO daily_plans(plan_date) VALUES ('2026-08-13');
            INSERT INTO planned_work_items VALUES ('00000000-0000-0000-0000-000000000001', '2026-08-13', NULL, NULL, 'Knowledge transfer', 'CGMFRAVII-2767', 'Knowledge transfer', 1800, 'CGM', 'DEVELOPMENT', 1);
            INSERT INTO recurring_task_templates VALUES ('00000000-0000-0000-0000-000000000002', 'Knowledge transfer', 'CGMFRAVII-2767', 'Knowledge transfer', 1800, 'CGM', 'DEVELOPMENT', 1);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateDatabaseWithInvalidWorkStatusAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE daily_plans (plan_date TEXT PRIMARY KEY);
            CREATE TABLE planned_work_items (
                id TEXT PRIMARY KEY, plan_date TEXT NOT NULL, start_time TEXT NULL, end_time TEXT NULL,
                name TEXT NOT NULL, jira_issue_key TEXT NOT NULL, comment TEXT NOT NULL, duration_seconds INTEGER NOT NULL,
                toggl_project TEXT NOT NULL, tempo_category TEXT NOT NULL, is_billable INTEGER NOT NULL, work_status INTEGER NOT NULL);
            CREATE TABLE recurring_task_templates (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, jira_issue_key TEXT NOT NULL, description TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL, toggl_project TEXT NOT NULL, tempo_category TEXT NOT NULL,
                is_billable INTEGER NOT NULL, work_status INTEGER NOT NULL);
            INSERT INTO daily_plans(plan_date) VALUES ('2026-08-13');
            INSERT INTO planned_work_items VALUES ('00000000-0000-0000-0000-000000000001', '2026-08-13', NULL, NULL, 'Knowledge transfer', 'CGMFRAVII-2767', 'Knowledge transfer', 1800, 'CGM', 'DEVELOPMENT', 1, 999);
            INSERT INTO recurring_task_templates VALUES ('00000000-0000-0000-0000-000000000002', 'Knowledge transfer', 'CGMFRAVII-2767', 'Knowledge transfer', 1800, 'CGM', 'DEVELOPMENT', 1, 999);
            """;
        await command.ExecuteNonQueryAsync();
    }
}
