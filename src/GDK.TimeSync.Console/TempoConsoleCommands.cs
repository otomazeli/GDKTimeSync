using System.Globalization;
using System.Text.Json;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using Microsoft.Extensions.DependencyInjection;

internal static class TempoConsoleCommands
{
    public static async Task RunAsync(string[] args, IServiceProvider services)
    {
        var tempo = services.GetRequiredService<TempoClient>();

        if (args is ["tempo-discover"])
        {
            Console.WriteLine(JsonSerializer.Serialize(await tempo.GetWorkAttributesAsync()));
            return;
        }

        if (args is ["tempo-create", var issueKey, var date, var time, var duration, var comment])
        {
            var user = await services.GetRequiredService<JiraClient>().GetMyselfAsync();
            if (string.IsNullOrWhiteSpace(user.Name))
            {
                throw new InvalidOperationException("Jira did not return a user name for the Tempo worklog worker.");
            }

            var started = DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .ToDateTime(TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture));
            var request = new TempoWorklogCreateRequest(
                user.Name,
                issueKey,
                started,
                int.Parse(duration, CultureInfo.InvariantCulture),
                comment);

            Console.WriteLine(JsonSerializer.Serialize(await tempo.CreateWorklogAsync(request)));
            return;
        }

        throw new ArgumentException("Use tempo-discover or tempo-create <issue-key> <yyyy-MM-dd> <HH:mm> <seconds> <comment>.");
    }
}
