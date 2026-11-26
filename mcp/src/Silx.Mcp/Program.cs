using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Silx.Mcp;

// Serveur MCP de Silx, transport stdio : le client MCP (Claude Desktop, Claude Code, autre agent) lance ce programme et
// lui parle sur l'entrée et la sortie standard. Les journaux vont sur la sortie d'erreur, jamais sur la sortie standard.
if (args.Contains("--version"))
{
    Console.WriteLine($"silx-mcp {typeof(SessionSilx).Assembly.GetName().Version}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
// Une erreur d'outil (projet introuvable, banc inconnu…) est renvoyée au client : inutile de la journaliser en plus.
builder.Logging.AddFilter("ModelContextProtocol.Server.McpServer", LogLevel.Critical);
builder.Services.AddSingleton<SessionSilx>();
builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "silx", Title = "Silx — simulation électronique", Version = typeof(SessionSilx).Assembly.GetName().Version?.ToString(3) ?? "0.1.0" };
        o.ServerInstructions = Instructions.Texte;
    })
    .WithStdioServerTransport()
    .WithTools<OutilsSilx>();
await builder.Build().RunAsync();
