using GDK.TimeSync.Core;
using GDK.TimeSync.Jira;
using GDK.TimeSync.Tempo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTimeSyncCore();
builder.Services.AddTimeSyncJira(builder.Configuration);
builder.Services.AddTimeSyncTempo(builder.Configuration);

using var host = builder.Build();
if (args.Length == 0)
{
    await host.StartAsync();
}
else
{
    await TempoConsoleCommands.RunAsync(args, host.Services);
}
