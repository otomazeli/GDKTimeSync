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
            is_billable INTEGER NOT NULL,
            work_status INTEGER NOT NULL DEFAULT 0 CHECK (work_status IN (0, 1, 2, 3, 4)));
        CREATE INDEX IF NOT EXISTS ix_planned_work_items_plan_date ON planned_work_items(plan_date);
        CREATE TABLE IF NOT EXISTS recurring_task_templates (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            jira_issue_key TEXT NOT NULL,
            description TEXT NOT NULL,
            duration_seconds INTEGER NOT NULL,
            toggl_project TEXT NOT NULL,
            tempo_category TEXT NOT NULL,
            is_billable INTEGER NOT NULL,
            work_status INTEGER NOT NULL DEFAULT 0 CHECK (work_status IN (0, 1, 2, 3, 4)));
        CREATE TABLE IF NOT EXISTS delivery_attempts (
            planned_work_item_id TEXT PRIMARY KEY,
            toggl_entry_id INTEGER NULL,
            tempo_worklog_id INTEGER NULL,
            status INTEGER NOT NULL,
            failure_code INTEGER NULL,
            slack_state INTEGER NOT NULL);
        """;

    private readonly string connectionString;
    private readonly string readOnlyConnectionString;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InitializationLocks = new(StringComparer.OrdinalIgnoreCase);

    public SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true, Pooling = false }.ToString();
        readOnlyConnectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, ForeignKeys = true, Pooling = false }.ToString();
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
                await BeginImmediateAsync(connection, cancellationToken);
                try
                {
                    await using (var command = connection.CreateCommand())
                    {
                        command.CommandText = Schema;
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await EnsureWorkStatusColumnsAsync(connection, cancellationToken);
                    await CommitAsync(connection, cancellationToken);
                }
                catch
                {
                    await RollbackAsync(connection);
                    throw;
                }

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

    public async Task<SqliteConnection> OpenReadOnlyConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(readOnlyConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task EnsureWorkStatusColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await EnsureWorkStatusColumnAsync(connection, "planned_work_items", cancellationToken);
        await EnsureWorkStatusColumnAsync(connection, "recurring_task_templates", cancellationToken);
    }

    private static async Task EnsureWorkStatusColumnAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var exists = await HasWorkStatusColumnAsync(connection, tableName, cancellationToken);

        if (!exists)
        {
            try
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN work_status INTEGER NOT NULL DEFAULT 0";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException exception) when (IsDuplicateColumn(exception))
            {
                if (!await HasWorkStatusColumnAsync(connection, tableName, cancellationToken))
                    throw;
            }
        }

        await using var update = connection.CreateCommand();
        update.CommandText = $"UPDATE {tableName} SET work_status = 0 WHERE work_status IS NULL OR work_status NOT IN (0, 1, 2, 3, 4)";
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasWorkStatusColumnAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName})";
        await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (string.Equals(reader.GetString(1), "work_status", StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static bool IsDuplicateColumn(SqliteException exception) =>
        exception.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase);

    private static async Task BeginImmediateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "BEGIN IMMEDIATE";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task CommitAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "COMMIT";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RollbackAsync(SqliteConnection connection)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "ROLLBACK";
            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException)
        {
        }
    }
}
