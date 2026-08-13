using GDK.TimeSync.Core;
using GDK.TimeSync.Persistence;
using Microsoft.Data.Sqlite;

namespace GDK.TimeSync.Tests;

public sealed class SqliteDailySlackDeliveryRepositoryTests : IAsyncLifetime
{
    private readonly List<string> databasePaths = [];

    [Fact]
    public async Task TryClaimAsync_RejectsSecondSendForSameDateAndFingerprint()
    {
        var repository = CreateRepository();
        var date = new DateOnly(2026, 8, 13);

        Assert.True(await repository.TryClaimAsync(date, Fingerprint));
        Assert.False(await repository.TryClaimAsync(date, Fingerprint));
    }

    [Fact]
    public async Task TryClaimAsync_RejectsAnotherFingerprintForTheSameDate()
    {
        var repository = CreateRepository();
        var date = new DateOnly(2026, 8, 13);

        Assert.True(await repository.TryClaimAsync(date, Fingerprint));
        Assert.False(await repository.TryClaimAsync(date, new string('b', 64)));
    }

    [Fact]
    public async Task TryClaimAsync_RejectsParallelClaims()
    {
        var databasePath = CreateDatabasePath();
        var first = new SqliteDailySlackDeliveryRepository(new SqliteDatabase(databasePath));
        var second = new SqliteDailySlackDeliveryRepository(new SqliteDatabase(databasePath));
        var date = new DateOnly(2026, 8, 13);

        var claims = await Task.WhenAll(first.TryClaimAsync(date, Fingerprint), second.TryClaimAsync(date, Fingerprint));

        Assert.Equal(1, claims.Count(acquired => acquired));
    }

    [Theory]
    [InlineData(DailySlackDeliveryState.Sent)]
    [InlineData(DailySlackDeliveryState.ReconciliationRequired)]
    public async Task TryClaimAsync_RejectsPersistedFinalStates(DailySlackDeliveryState state)
    {
        var repository = CreateRepository();
        var date = new DateOnly(2026, 8, 13);
        await repository.SaveAsync(new DailySlackDelivery(date, Fingerprint, state, state == DailySlackDeliveryState.Sent ? null : DailySlackFailureCode.Cancelled));

        Assert.False(await repository.TryClaimAsync(date, Fingerprint));
    }

    [Fact]
    public async Task GetAsync_RoundTripsOnlySafeDeliveryState()
    {
        var repository = CreateRepository();
        var date = new DateOnly(2026, 8, 13);
        var delivery = new DailySlackDelivery(date, Fingerprint, DailySlackDeliveryState.ReconciliationRequired, DailySlackFailureCode.Transport);

        await repository.SaveAsync(delivery);

        Assert.Equal(delivery, await repository.GetAsync(date));
    }

    [Fact]
    public async Task SaveAsync_CancelledDeliveryBecomesReconciliationRequired()
    {
        var repository = CreateRepository();
        var date = new DateOnly(2026, 8, 13);

        await repository.SaveAsync(new DailySlackDelivery(date, Fingerprint, DailySlackDeliveryState.InProgress, DailySlackFailureCode.Cancelled));

        Assert.Equal(
            new DailySlackDelivery(date, Fingerprint, DailySlackDeliveryState.ReconciliationRequired, DailySlackFailureCode.Cancelled),
            await repository.GetAsync(date));
    }

    [Fact]
    public async Task Schema_ContainsOnlySafeDailyDeliveryColumns()
    {
        var databasePath = CreateDatabasePath();
        var repository = new SqliteDailySlackDeliveryRepository(new SqliteDatabase(databasePath));
        await repository.TryClaimAsync(new DateOnly(2026, 8, 13), Fingerprint);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(daily_slack_deliveries)";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(1));

        Assert.Equal(["delivery_date", "content_fingerprint", "state", "failure_code"], columns);
        Assert.DoesNotContain(columns, column => column.Contains("body", StringComparison.OrdinalIgnoreCase) || column.Contains("message", StringComparison.OrdinalIgnoreCase) || column.Contains("url", StringComparison.OrdinalIgnoreCase) || column.Contains("header", StringComparison.OrdinalIgnoreCase) || column.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (var databasePath in databasePaths)
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        return Task.CompletedTask;
    }

    private SqliteDailySlackDeliveryRepository CreateRepository() => new(new SqliteDatabase(CreateDatabasePath()));

    private string CreateDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"GDK.TimeSync.Tests.{Guid.NewGuid():N}.db");
        databasePaths.Add(path);
        return path;
    }

    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
}
