using GDK.TimeSync.Core;
using GDK.TimeSync.Jira;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTimeSyncCore();
builder.Services.AddTimeSyncJira(builder.Configuration);

using var host = builder.Build();
await host.StartAsync();
