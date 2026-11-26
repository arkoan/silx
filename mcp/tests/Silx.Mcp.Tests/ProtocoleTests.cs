using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
namespace Silx.Mcp.Tests;

/// <summary>
/// Bout en bout : le serveur est lancé comme le ferait un client MCP (processus séparé, transport stdio), et piloté
/// par le client du SDK officiel.
/// </summary>
public class ProtocoleTests
{
    static async Task<McpClient> Client()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "silx-mcp.dll");
        Assert.True(File.Exists(dll), $"Serveur absent : {dll}");
        var transport = new StdioClientTransport(new StdioClientTransportOptions { Name = "silx", Command = "dotnet", Arguments = [dll] });
        return await McpClient.CreateAsync(transport);
    }

    static async Task<(JsonElement Json, bool Erreur, string Texte)> Appel(McpClient c, string outil, Dictionary<string, object?>? args = null)
    {
        var r = await c.CallToolAsync(outil, args ?? new());
        var texte = string.Concat(r.Content.OfType<TextContentBlock>().Select(t => t.Text));
        bool erreur = r.IsError == true;
        return (erreur ? default : JsonDocument.Parse(texte).RootElement.Clone(), erreur, texte);
    }

    [Fact]
    public async Task Outils_annonces_en_francais()
    {
        await using var c = await Client();
        Assert.Equal("silx", c.ServerInfo.Name);
        Assert.Contains("bancs de test", c.ServerInstructions);
        var outils = (await c.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        foreach (var n in new[] { "list_projects", "open_project", "open_demo", "describe_project", "list_testbenches", "list_measurements", "list_signals",
                     "get_netlist", "run_simulation", "run_campaign", "get_test_summary", "get_worst_case", "get_raw_curve", "set_component_value", "save_project", "close_project" })
            Assert.Contains(n, outils);
    }

    [Fact]
    public async Task Demo_simulee_puis_monte_carlo()
    {
        await using var c = await Client();
        var (p, _, _) = await Appel(c, "open_demo", new() { ["nom"] = "Ampli émetteur commun" });
        Assert.Equal("p1", p.GetProperty("id").GetString());

        var (sim, err, texte) = await Appel(c, "run_simulation", new() { ["projet"] = "p1" });
        Assert.False(err, texte);
        var banc = sim[0];
        Assert.Equal("PASS", banc.GetProperty("verdict").GetString());
        var gain = banc.GetProperty("criteres").EnumerateArray().Single(x => x.GetProperty("nom").GetString() == "Gain maximal").GetProperty("valeur").GetDouble();
        Assert.InRange(gain, 41, 44);   // ≈ 42 dB (voir Demos)
        Assert.Contains("Réponse en fréquence", texte);   // accents lisibles, non échappés

        var (mc, err2, t2) = await Appel(c, "run_campaign", new() { ["tirages"] = 12, ["graine"] = 3 });
        Assert.False(err2, t2);
        Assert.Equal(12, mc.GetProperty("tirages").GetInt32());
        Assert.True(mc.TryGetProperty("pire_cas", out _));

        var (pire, err3, t3) = await Appel(c, "get_worst_case", new() { ["nombre"] = 3 });
        Assert.False(err3, t3);
        Assert.Equal(3, pire.GetProperty("pires_cas").GetArrayLength());
        Assert.True(pire.GetProperty("pires_cas")[0].GetProperty("valeurs").TryGetProperty("RC", out _));

        var (resume, _, _) = await Appel(c, "get_test_summary");
        Assert.Equal("PASS", resume.GetProperty("verdict").GetString());
        Assert.Contains("12 conformes", resume.GetProperty("bancs")[0].GetProperty("campagne").GetString() ?? "");

        var (courbe, _, _) = await Appel(c, "get_raw_curve", new() { ["signal"] = "V(OUT)", ["max_points"] = 50 });
        Assert.Equal("dB", courbe.GetProperty("unite_y").GetString());
        Assert.InRange(courbe.GetProperty("x").GetArrayLength(), 2, 50);
    }

    [Fact]
    public async Task Erreurs_lisibles()
    {
        await using var c = await Client();
        var (_, err, texte) = await Appel(c, "run_simulation");
        Assert.True(err);
        Assert.Contains("Aucun projet ouvert", texte);
        var (_, err2, t2) = await Appel(c, "open_project", new() { ["chemin"] = "/nulle/part/projet.silx" });
        Assert.True(err2);
        Assert.Contains("introuvable", t2);
    }
}
