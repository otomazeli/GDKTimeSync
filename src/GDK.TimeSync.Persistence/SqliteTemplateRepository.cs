using GDK.TimeSync.Core;

namespace GDK.TimeSync.Persistence;

public sealed class SqliteTemplateRepository(SqliteDatabase database) : ITemplateRepository
{
    public async Task<IReadOnlyList<RecurringTaskTemplate>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, jira_issue_key, description, duration_seconds, toggl_project, toggl_project_id, tempo_category, is_billable, work_status
            FROM recurring_task_templates
            ORDER BY name, id
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var templates = new List<RecurringTaskTemplate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            templates.Add(new RecurringTaskTemplate(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                TimeSpan.FromSeconds(reader.GetInt64(4)),
                reader.GetString(5),
                reader.GetString(7),
                reader.GetBoolean(8),
                ReadWorkStatus(reader.IsDBNull(9) ? 0 : reader.GetInt32(9)))
                { TogglProjectId = reader.IsDBNull(6) ? null : reader.GetInt64(6) });
        }

        return templates;
    }

    public async Task SaveAsync(RecurringTaskTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (!Enum.IsDefined(template.Status))
            throw new ArgumentOutOfRangeException(nameof(template), "The template must have a defined work status.");
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO recurring_task_templates(id, name, jira_issue_key, description, duration_seconds, toggl_project, toggl_project_id, tempo_category, is_billable, work_status)
            VALUES ($id, $name, $jiraIssueKey, $description, $durationSeconds, $togglProject, $togglProjectId, $tempoCategory, $isBillable, $workStatus)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                jira_issue_key = excluded.jira_issue_key,
                description = excluded.description,
                duration_seconds = excluded.duration_seconds,
                toggl_project = excluded.toggl_project,
                toggl_project_id = excluded.toggl_project_id,
                tempo_category = excluded.tempo_category,
                is_billable = excluded.is_billable,
                work_status = excluded.work_status
            """;
        command.Parameters.AddWithValue("$id", template.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", template.Name);
        command.Parameters.AddWithValue("$jiraIssueKey", template.JiraIssueKey);
        command.Parameters.AddWithValue("$description", template.Description);
        command.Parameters.AddWithValue("$durationSeconds", Convert.ToInt64(template.Duration.TotalSeconds));
        command.Parameters.AddWithValue("$togglProject", template.TogglProject);
        command.Parameters.AddWithValue("$togglProjectId", (object?)template.TogglProjectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$tempoCategory", template.TempoCategory);
        command.Parameters.AddWithValue("$isBillable", template.IsBillable);
        command.Parameters.AddWithValue("$workStatus", (int)template.Status);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static WorkStatus ReadWorkStatus(int value) =>
        Enum.IsDefined((WorkStatus)value) ? (WorkStatus)value : WorkStatus.InProgress;
}
