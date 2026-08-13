using Microsoft.Data.Sqlite;

namespace GDK.TimeSync.Tests;

public static class MigrationProbeProgram
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--migration-probe")
            return 0;

        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = args[1] }.ToString());
            await connection.OpenAsync();
            await using (var begin = connection.CreateCommand())
            {
                begin.CommandText = "BEGIN IMMEDIATE";
                await begin.ExecuteNonQueryAsync();
            }

            await File.WriteAllTextAsync(args[2], "locked");
            while (!File.Exists(args[3]))
                await Task.Delay(10);

            await using var commit = connection.CreateCommand();
            commit.CommandText = "COMMIT";
            await commit.ExecuteNonQueryAsync();
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
