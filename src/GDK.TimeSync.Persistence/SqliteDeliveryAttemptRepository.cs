using GDK.TimeSync.Core;

namespace GDK.TimeSync.Persistence;

public sealed class SqliteDeliveryAttemptRepository(SqliteDatabase database) : IDeliveryAttemptRepository, IDeliveryHistoryRepository
{
    public async Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, plannedWorkItemId, cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryAttempt>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenReadOnlyConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT planned_work_item_id, toggl_entry_id, tempo_worklog_id, status, failure_code, slack_state,
                   toggl_write_recorded_at_utc, tempo_write_recorded_at_utc, reconciliation_recorded_at_utc
            FROM delivery_attempts
            ORDER BY planned_work_item_id
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var attempts = new List<DeliveryAttempt>();
        while (await reader.ReadAsync(cancellationToken))
            attempts.Add(ReadAttempt(reader));
        return attempts;
    }

    public async Task<IReadOnlyList<DeliveryHistoryEntry>> ListHistoryAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenReadOnlyConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // LEFT JOIN: an attempt outlives its planned item if the plan row was replaced, and such a
        // row must still be listed (with no date/description) rather than disappear from history.
        command.CommandText = """
            SELECT a.planned_work_item_id, a.toggl_entry_id, a.tempo_worklog_id, a.status, a.failure_code, a.slack_state,
                   a.toggl_write_recorded_at_utc, a.tempo_write_recorded_at_utc, a.reconciliation_recorded_at_utc,
                   i.plan_date, i.jira_issue_key, i.comment
            FROM delivery_attempts a
            LEFT JOIN planned_work_items i ON i.id = a.planned_work_item_id
            ORDER BY i.plan_date DESC, i.start_time, i.jira_issue_key
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<DeliveryHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(new DeliveryHistoryEntry(
                ReadAttempt(reader),
                reader.IsDBNull(9) ? null : DateOnly.Parse(reader.GetString(9), System.Globalization.CultureInfo.InvariantCulture),
                reader.IsDBNull(10) ? "" : reader.GetString(10),
                reader.IsDBNull(11) ? "" : reader.GetString(11)));
        return entries;
    }

    public async Task<DeliveryAttemptClaim> ClaimAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO delivery_attempts(planned_work_item_id, toggl_entry_id, tempo_worklog_id, status, failure_code, slack_state,
                                          toggl_write_recorded_at_utc, tempo_write_recorded_at_utc, reconciliation_recorded_at_utc)
            VALUES ($id, NULL, NULL, $status, NULL, $slackState, NULL, NULL, NULL)
            ON CONFLICT(planned_work_item_id) DO NOTHING
            """;
        command.Parameters.AddWithValue("$id", plannedWorkItemId.ToString("D"));
        command.Parameters.AddWithValue("$status", (int)DeliveryAttemptStatus.InProgress);
        command.Parameters.AddWithValue("$slackState", (int)SlackDeliveryState.NotSupported);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 1)
            return new DeliveryAttemptClaim(new DeliveryAttempt(plannedWorkItemId, null, null, DeliveryAttemptStatus.InProgress, null, SlackDeliveryState.NotSupported), true);

        return new DeliveryAttemptClaim((await GetAsync(connection, plannedWorkItemId, cancellationToken))!, false);
    }

    private static async Task<DeliveryAttempt?> GetAsync(Microsoft.Data.Sqlite.SqliteConnection connection, Guid plannedWorkItemId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT toggl_entry_id, tempo_worklog_id, status, failure_code, slack_state,
                   toggl_write_recorded_at_utc, tempo_write_recorded_at_utc, reconciliation_recorded_at_utc
            FROM delivery_attempts
            WHERE planned_work_item_id = $id
            """;
        command.Parameters.AddWithValue("$id", plannedWorkItemId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new DeliveryAttempt(
            plannedWorkItemId,
            reader.IsDBNull(0) ? null : reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetInt64(1),
            (DeliveryAttemptStatus)reader.GetInt32(2),
            reader.IsDBNull(3) ? null : (DeliveryFailureCode)reader.GetInt32(3),
            (SlackDeliveryState)reader.GetInt32(4),
            ReadTimestamp(reader, 5),
            ReadTimestamp(reader, 6),
            ReadTimestamp(reader, 7));
    }

    public async Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO delivery_attempts(planned_work_item_id, toggl_entry_id, tempo_worklog_id, status, failure_code, slack_state,
                                          toggl_write_recorded_at_utc, tempo_write_recorded_at_utc, reconciliation_recorded_at_utc)
            VALUES ($id, $togglEntryId, $tempoWorklogId, $status, $failureCode, $slackState,
                    $togglWriteAt, $tempoWriteAt, $reconciliationAt)
            ON CONFLICT(planned_work_item_id) DO UPDATE SET
                toggl_entry_id = excluded.toggl_entry_id,
                tempo_worklog_id = excluded.tempo_worklog_id,
                status = excluded.status,
                failure_code = excluded.failure_code,
                slack_state = excluded.slack_state,
                toggl_write_recorded_at_utc = excluded.toggl_write_recorded_at_utc,
                tempo_write_recorded_at_utc = excluded.tempo_write_recorded_at_utc,
                reconciliation_recorded_at_utc = excluded.reconciliation_recorded_at_utc
            """;
        command.Parameters.AddWithValue("$id", attempt.PlannedWorkItemId.ToString("D"));
        command.Parameters.AddWithValue("$togglEntryId", (object?)attempt.TogglEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$tempoWorklogId", (object?)attempt.TempoWorklogId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)attempt.Status);
        command.Parameters.AddWithValue("$failureCode", attempt.FailureCode is { } failureCode ? (int)failureCode : DBNull.Value);
        command.Parameters.AddWithValue("$slackState", (int)attempt.SlackState);
        command.Parameters.AddWithValue("$togglWriteAt", WriteTimestamp(attempt.TogglWriteRecordedAtUtc));
        command.Parameters.AddWithValue("$tempoWriteAt", WriteTimestamp(attempt.TempoWriteRecordedAtUtc));
        command.Parameters.AddWithValue("$reconciliationAt", WriteTimestamp(attempt.ReconciliationRecordedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DeliveryAttempt ReadAttempt(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(Guid.Parse(reader.GetString(0)),
            reader.IsDBNull(1) ? null : reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            (DeliveryAttemptStatus)reader.GetInt32(3),
            reader.IsDBNull(4) ? null : (DeliveryFailureCode)reader.GetInt32(4),
            (SlackDeliveryState)reader.GetInt32(5),
            ReadTimestamp(reader, 6),
            ReadTimestamp(reader, 7),
            ReadTimestamp(reader, 8));

    private static DateTimeOffset? ReadTimestamp(Microsoft.Data.Sqlite.SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTimeOffset.Parse(reader.GetString(ordinal), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);

    private static object WriteTimestamp(DateTimeOffset? value) =>
        (object?)value?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? DBNull.Value;
}
