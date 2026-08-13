using GDK.TimeSync.Persistence;

namespace GDK.TimeSync.Tests;

public static class MigrationProbeProgram
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--migration-probe")
            return 0;

        try
        {
            await File.WriteAllTextAsync(args[2], "ready");
            while (!File.Exists(args[3]))
                await Task.Delay(10);

            await using var connection = await new SqliteDatabase(args[1]).OpenConnectionAsync();
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
