using GDK.TimeSync.Core;
using Microsoft.Data.Sqlite;

namespace GDK.TimeSync.Persistence;

public sealed class SqliteDailyPlanRepository(SqliteDatabase database) : IDailyPlanRepository
{
    public async Task<DailyPlan?> GetAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        var dateValue = date.ToString("yyyy-MM-dd");

        await using var planCommand = connection.CreateCommand();
        planCommand.CommandText = "SELECT 1 FROM daily_plans WHERE plan_date = $date";
        planCommand.Parameters.AddWithValue("$date", dateValue);
        if (await planCommand.ExecuteScalarAsync(cancellationToken) is null)
            return null;

        await using var itemCommand = connection.CreateCommand();
        itemCommand.CommandText = """
            SELECT id, start_time, end_time, name, jira_issue_key, comment, duration_seconds, toggl_project, toggl_project_id, tempo_category, is_billable, post_to_toggl, work_status, toggl_entry_id, source
            FROM planned_work_items
            WHERE plan_date = $date
            ORDER BY rowid
            """;
        itemCommand.Parameters.AddWithValue("$date", dateValue);
        await using var reader = await itemCommand.ExecuteReaderAsync(cancellationToken);
        var items = new List<PlannedWorkItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new PlannedWorkItem(
                Guid.Parse(reader.GetString(0)),
                date,
                reader.IsDBNull(1) ? null : TimeOnly.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : TimeOnly.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                TimeSpan.FromSeconds(reader.GetInt64(6)),
                reader.GetString(7),
                reader.GetString(9),
                reader.GetBoolean(10),
                ReadWorkStatus(reader.IsDBNull(12) ? 0 : reader.GetInt32(12)))
                {
                    TogglProjectId = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    PostToToggl = reader.GetBoolean(11),
                    TogglEntryId = reader.IsDBNull(13) ? null : reader.GetInt64(13),
                    Source = reader.IsDBNull(14) ? ItemSource.Local : ReadSource(reader.GetInt32(14))
                });
        }

        return DailyPlan.Create(date, items);
    }

    public async Task SaveAsync(DailyPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Items.Any(item => item.Day != plan.Date))
            throw new ArgumentException("Every item must belong to the plan date.", nameof(plan));
        if (plan.Items.Any(item => !Enum.IsDefined(item.Status)))
            throw new ArgumentOutOfRangeException(nameof(plan), "Every item must have a defined work status.");

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var dateValue = plan.Date.ToString("yyyy-MM-dd");

        await using (var planCommand = connection.CreateCommand())
        {
            planCommand.Transaction = transaction;
            planCommand.CommandText = "INSERT INTO daily_plans(plan_date) VALUES ($date) ON CONFLICT(plan_date) DO NOTHING";
            planCommand.Parameters.AddWithValue("$date", dateValue);
            await planCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM planned_work_items WHERE plan_date = $date";
            deleteCommand.Parameters.AddWithValue("$date", dateValue);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var item in plan.Items)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO planned_work_items(id, plan_date, start_time, end_time, name, jira_issue_key, comment, duration_seconds, toggl_project, toggl_project_id, tempo_category, is_billable, post_to_toggl, work_status, toggl_entry_id, source)
                VALUES ($id, $date, $start, $end, $name, $jiraIssueKey, $comment, $durationSeconds, $togglProject, $togglProjectId, $tempoCategory, $isBillable, $postToToggl, $workStatus, $togglEntryId, $source)
                """;
            insertCommand.Parameters.AddWithValue("$id", item.Id.ToString("D"));
            insertCommand.Parameters.AddWithValue("$date", dateValue);
            insertCommand.Parameters.AddWithValue("$start", (object?)item.Start?.ToString("HH:mm:ss") ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$end", (object?)item.End?.ToString("HH:mm:ss") ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$name", item.Name);
            insertCommand.Parameters.AddWithValue("$jiraIssueKey", item.JiraIssueKey);
            insertCommand.Parameters.AddWithValue("$comment", item.Comment);
            insertCommand.Parameters.AddWithValue("$durationSeconds", Convert.ToInt64(item.Duration.TotalSeconds));
            insertCommand.Parameters.AddWithValue("$togglProject", item.TogglProject);
            insertCommand.Parameters.AddWithValue("$togglProjectId", (object?)item.TogglProjectId ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$tempoCategory", item.TempoCategory);
            insertCommand.Parameters.AddWithValue("$isBillable", item.IsBillable);
            insertCommand.Parameters.AddWithValue("$postToToggl", item.PostToToggl);
            insertCommand.Parameters.AddWithValue("$workStatus", (int)item.Status);
            insertCommand.Parameters.AddWithValue("$togglEntryId", (object?)item.TogglEntryId ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$source", (int)item.Source);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static WorkStatus ReadWorkStatus(int value) =>
        Enum.IsDefined((WorkStatus)value) ? (WorkStatus)value : WorkStatus.InProgress;

    private static ItemSource ReadSource(int value) =>
        Enum.IsDefined((ItemSource)value) ? (ItemSource)value : ItemSource.Local;
}
