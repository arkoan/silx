using Microsoft.Extensions.DependencyInjection;
namespace Silx.Mcp;

/// <summary>Configuration commune du serveur MCP : celle du programme silx-mcp et celle de l'application Silx (mode direct).</summary>
public static class Serveur
{
    public static string Version => typeof(SessionSilx).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>Enregistre la session et le serveur MCP (identité, instructions, outils) ; le transport reste à choisir.</summary>
    public static IMcpServerBuilder Configurer(IServiceCollection services, SessionSilx session)
    {
        services.AddSingleton(session);
        return services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new() { Name = "silx", Title = "Silx — simulation électronique", Version = Version };
                o.ServerInstructions = Instructions.Texte;
            })
            .WithTools<OutilsSilx>();
    }
}
