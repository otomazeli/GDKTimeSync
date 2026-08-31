using GDK.TimeSync.Core;

namespace GDK.TimeSync.Persistence;

public sealed class SqliteDailySlackDeliveryRepository(SqliteDatabase database) : IDailySlackDeliveryRepository
{
    public async Task<DailySlackDelivery?> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT content_fingerprint, state, failure_code FROM daily_slack_deliveries WHERE delivery_date = $date";
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DailySlackDelivery(
                date,
                reader.GetString(0),
                (DailySlackDeliveryState)reader.GetInt32(1),
                reader.IsDBNull(2) ? null : (DailySlackFailureCode)reader.GetInt32(2))
            : null;
    }

    public async Task<bool> TryClaimAsync(DateOnly date, string contentFingerprint, CancellationToken cancellationToken = default)
    {
        ValidateFingerprint(contentFingerprint);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO daily_slack_deliveries(delivery_date, content_fingerprint, state, failure_code)
            VALUES ($date, $fingerprint, $state, NULL)
            ON CONFLICT(delivery_date) DO NOTHING
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$fingerprint", contentFingerprint);
        command.Parameters.AddWithValue("$state", (int)DailySlackDeliveryState.InProgress);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task SaveAsync(DailySlackDelivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (delivery.FailureCode is not null)
            delivery = delivery with { State = DailySlackDeliveryState.ReconciliationRequired };
        ValidateFingerprint(delivery.ContentFingerprint);
        if (!Enum.IsDefined(delivery.State))
            throw new ArgumentOutOfRangeException(nameof(delivery));
        if (delivery.FailureCode is { } failureCode && !Enum.IsDefined(failureCode))
            throw new ArgumentOutOfRangeException(nameof(delivery));

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE daily_slack_deliveries
            SET state = $state, failure_code = $failureCode
            WHERE delivery_date = $date
              AND content_fingerprint = $fingerprint
              AND state = $inProgress
            """;
        command.Parameters.AddWithValue("$date", delivery.Date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$fingerprint", delivery.ContentFingerprint);
        command.Parameters.AddWithValue("$state", (int)delivery.State);
        command.Parameters.AddWithValue("$failureCode", delivery.FailureCode is { } savedFailureCode ? (int)savedFailureCode : DBNull.Value);
        command.Parameters.AddWithValue("$inProgress", (int)DailySlackDeliveryState.InProgress);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Daily Slack delivery state conflict.");
    }

    private static void ValidateFingerprint(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        if (fingerprint.Length != 64 || fingerprint.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Content fingerprint must be a SHA-256 hexadecimal value.", nameof(fingerprint));
    }
}
