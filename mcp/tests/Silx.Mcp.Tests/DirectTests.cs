using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Silx.Etude;
namespace Silx.Mcp.Tests;

/// <summary>
/// Mode direct, bout en bout : un hôte joue l'application Silx (socket local, session avec un onglet déjà ouvert) et
/// silx-mcp est lancé comme par un client MCP. Les appels doivent arriver dans la session de l'« application », et
/// l'aiguillage doit suivre l'ouverture et la fermeture de l'application.
/// </summary>
public class DirectTests
{
    static string Socket() => Path.Combine(Path.GetTempPath(), $"silx-test-{Guid.NewGuid():N}"[..28] + ".sock");

    static async Task<McpClient> Client(string socket)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "silx-mcp.dll");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "silx", Command = "dotnet", Arguments = [dll],
            EnvironmentVariables = new Dictionary<string, string?> { ["SILX_DIRECT"] = socket },
        });
        return await McpClient.CreateAsync(transport);
    }

    static async Task<(JsonElement Json, bool Erreur, string Texte)> Appel(McpClient c, string outil, Dictionary<string, object?>? args = null)
    {
        var r = await c.CallToolAsync(outil, args ?? new());
        var texte = string.Concat(r.Content.OfType<TextContentBlock>().Select(t => t.Text));
        bool erreur = r.IsError == true;
        return (erreur ? default : JsonDocument.Parse(texte).RootElement.Clone(), erreur, texte);
    }

    /// <summary>Session de l'« application » : un onglet ouvert (le filtre RC), comme dans Silx.</summary>
    static (SessionSilx Session, Dictionary<Banc, ResultatEtude> Resultats) Application()
    {
        var s = new SessionSilx { EnDirect = true };
        var resultats = new Dictionary<Banc, ResultatEtude>();
        s.Adopter("Onglet de l'appli", null, Etude.Etude.Demo(), resultats);
        return (s, resultats);
    }

    [Fact]
    public async Task Les_appels_arrivent_dans_l_application()
    {
        var socket = Socket();
        var (session, resultats) = Application();
        var appels = new List<string>();
        McpRequestFilter<CallToolRequestParams, CallToolResult> filtre = next => async (ctx, ct) =>
        {
            lock (appels) appels.Add(ctx.Params!.Name);
            return await next(ctx, ct);
        };
        await using var hote = new HoteDirect(session, socket, filtre);
        Assert.True(await hote.DemarrerAsync());
        // Une seconde application ne prend pas le canal.
        await using (var autre = new HoteDirect(new SessionSilx(), socket)) Assert.False(await autre.DemarrerAsync());

        await using var c = await Client(socket);
        var (liste, _, texte) = await Appel(c, "list_projects");
        Assert.StartsWith("direct", liste.GetProperty("mode").GetString());
        Assert.Contains("Onglet de l'appli", texte);

        var (sim, err, t2) = await Appel(c, "run_simulation", new() { ["banc"] = "1" });
        Assert.False(err, t2);
        Assert.Equal("PASS", sim[0].GetProperty("verdict").GetString());
        // Résultats rangés dans le dictionnaire de l'onglet : l'application les affiche sans resimuler.
        Assert.Single(resultats);
        Assert.Equal(["list_projects", "run_simulation"], appels);
        // La sonde de la seconde application s'est déconnectée : il reste le client.
        for (int i = 0; i < 40 && hote.Connexions != 1; i++) await Task.Delay(50);
        Assert.True(hote.Connexions == 1, $"connexions : {hote.Connexions}");
    }

    [Fact]
    public async Task Bascule_quand_l_application_s_ouvre_puis_se_ferme()
    {
        var socket = Socket();
        await using var c = await Client(socket);
        var (liste, _, _) = await Appel(c, "list_projects");
        Assert.StartsWith("arrière-plan", liste.GetProperty("mode").GetString());

        // L'application s'ouvre : l'appel suivant la rejoint (l'arrière-plan n'a aucun projet ouvert).
        var (session, _) = Application();
        var hote = new HoteDirect(session, socket);
        Assert.True(await hote.DemarrerAsync());
        var (l2, _, t2) = await Appel(c, "list_projects");
        Assert.StartsWith("direct", l2.GetProperty("mode").GetString());
        Assert.Contains("Onglet de l'appli", t2);

        // L'application se ferme : retour en arrière-plan, sans erreur pour le client.
        await hote.DisposeAsync();
        Assert.False(File.Exists(socket));
        await Task.Delay(300);
        var (l3, err, t3) = await Appel(c, "list_projects");
        Assert.False(err, t3);
        Assert.StartsWith("arrière-plan", l3.GetProperty("mode").GetString());
        var (_, err4, t4) = await Appel(c, "open_demo", new() { ["nom"] = "Filtre RC" });
        Assert.False(err4, t4);
    }

    [Fact]
    public async Task L_arriere_plan_garde_ses_projets_ouverts()
    {
        var socket = Socket();
        await using var c = await Client(socket);
        await Appel(c, "open_demo", new() { ["nom"] = "Filtre RC" });
        var (session, _) = Application();
        await using var hote = new HoteDirect(session, socket);
        Assert.True(await hote.DemarrerAsync());
        // Un projet est ouvert en arrière-plan : on n'en change pas en cours de travail.
        var (l, _, _) = await Appel(c, "list_projects");
        Assert.StartsWith("arrière-plan", l.GetProperty("mode").GetString());
        Assert.Equal(0, hote.Connexions);
    }
}
