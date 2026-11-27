using Silx.Mcp;

// Serveur MCP de Silx, transport stdio : le client MCP (Claude Desktop, Claude Code, autre agent) lance ce programme et
// lui parle sur l'entrée et la sortie standard. Les journaux vont sur la sortie d'erreur, jamais sur la sortie standard.
// Si l'application Silx est ouverte, les outils s'exécutent dans l'application (mode « en direct », voir Relais) ;
// sinon en arrière-plan, sur les fichiers.
if (args.Contains("--version"))
{
    Console.WriteLine($"silx-mcp {typeof(SessionSilx).Assembly.GetName().Version}");
    return;
}

var direct = args.Contains("--arriere-plan") ? null : CanalDirect.Chemin();
var relais = new Relais(Console.OpenStandardInput(), Console.OpenStandardOutput(), direct,
    Environment.GetEnvironmentVariable("SILX_MCP_JOURNAL") == "1" ? m => Console.Error.WriteLine($"silx-mcp : {m}") : null);
await relais.RunAsync();
