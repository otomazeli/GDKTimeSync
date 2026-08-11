using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace GDK.TimeSync.Persistence;

public sealed class SqliteDatabase
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS daily_plans (plan_date TEXT PRIMARY KEY);
        CREATE TABLE IF NOT EXISTS planned_work_items (
            id TEXT PRIMARY KEY,
            plan_date TEXT NOT NULL REFERENCES daily_plans(plan_date) ON DELETE CASCADE,
            start_time TEXT NULL,
            end_time TEXT NULL,
            name TEXT NOT NULL,
            jira_issue_key TEXT NOT NULL,
            comment TEXT NOT NULL,
            duration_seconds INTEGER NOT NULL,
            toggl_project TEXT NOT NULL,
            tempo_category TEXT NOT NULL,
            is_billable INTEGER NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_planned_work_items_plan_date ON planned_work_items(plan_date);
        CREATE TABLE IF NOT EXISTS recurring_task_templates (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            jira_issue_key TEXT NOT NULL,
            description TEXT NOT NULL,
            duration_seconds INTEGER NOT NULL,
            toggl_project TEXT NOT NULL,
            tempo_category TEXT NOT NULL,
            is_billable INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS delivery_attempts (
            planned_work_item_id TEXT PRIMARY KEY,
            toggl_entry_id INTEGER NULL,
            tempo_worklog_id INTEGER NULL,
            status INTEGER NOT NULL,
            failure_code INTEGER NULL,
            slack_state INTEGER NOT NULL);
        """;

    private readonly string connectionString;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InitializationLocks = new(StringComparer.OrdinalIgnoreCase);

    public SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true, Pooling = false }.ToString();
    }

    public string DatabasePath { get; }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var initializationLock = InitializationLocks.GetOrAdd(DatabasePath, _ => new SemaphoreSlim(1, 1));
        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            var connection = new SqliteConnection(connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = Schema;
                await command.ExecuteNonQueryAsync(cancellationToken);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }
        finally
        {
            initializationLock.Release();
        }
    }
}
