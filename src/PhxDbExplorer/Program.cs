using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhxDbExplorer.Configuration;
using PhxDbExplorer.Providers;
using PhxDbExplorer.Tools;

DatabaseConfig config;
try
{
    config = DatabaseConfig.FromEnvironment();
}
catch (InvalidOperationException ex)
{
    await Console.Error.WriteLineAsync($"[MCP Server] Configuration error: {ex.Message}");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
// All log output must go to stderr — stdout is reserved for MCP JSON-RPC messages
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<ISchemaProvider>(_ => SchemaProviderFactory.Create(config));

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "phx-dbexplorer", Version = "1.0.0" };
    })
    .WithStdioServerTransport()
    .WithTools<SchemaTools>();

await builder.Build().RunAsync();
return 0;
