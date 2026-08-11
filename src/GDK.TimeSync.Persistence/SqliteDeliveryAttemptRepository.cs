using GDK.TimeSync.Core;

namespace GDK.TimeSync.Persistence;

public sealed class SqliteDeliveryAttemptRepository(SqliteDatabase database) : IDeliveryAttemptRepository
{
    public async Task<DeliveryAttempt?> GetAsync(Guid plannedWorkItemId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT toggl_entry_id, tempo_worklog_id, status, failure_code, slack_state
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
            (SlackDeliveryState)reader.GetInt32(4));
    }

    public async Task SaveAsync(DeliveryAttempt attempt, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO delivery_attempts(planned_work_item_id, toggl_entry_id, tempo_worklog_id, status, failure_code, slack_state)
            VALUES ($id, $togglEntryId, $tempoWorklogId, $status, $failureCode, $slackState)
            ON CONFLICT(planned_work_item_id) DO UPDATE SET
                toggl_entry_id = excluded.toggl_entry_id,
                tempo_worklog_id = excluded.tempo_worklog_id,
                status = excluded.status,
                failure_code = excluded.failure_code,
                slack_state = excluded.slack_state
            """;
        command.Parameters.AddWithValue("$id", attempt.PlannedWorkItemId.ToString("D"));
        command.Parameters.AddWithValue("$togglEntryId", (object?)attempt.TogglEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$tempoWorklogId", (object?)attempt.TempoWorklogId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)attempt.Status);
        command.Parameters.AddWithValue("$failureCode", attempt.FailureCode is { } failureCode ? (int)failureCode : DBNull.Value);
        command.Parameters.AddWithValue("$slackState", (int)attempt.SlackState);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
