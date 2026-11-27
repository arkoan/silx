using System.Net.Sockets;
using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Silx.Etude;
namespace Silx.App.Tests;

/// <summary>
/// Mode direct : un client MCP se branche sur le socket de la fenêtre (comme silx-mcp quand l'application est ouverte) ;
/// ses actions doivent apparaître dans la fenêtre, et l'utilisateur doit pouvoir l'interrompre.
/// </summary>
public class IATests
{
    static string Socket() => Path.Combine(Path.GetTempPath(), $"silx-ui-{Guid.NewGuid():N}"[..24] + ".sock");

    static async Task<McpClient> Client(string socket)
    {
        var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await s.ConnectAsync(new UnixDomainSocketEndPoint(socket));
        var flux = new NetworkStream(s, ownsSocket: true);
        return await McpClient.CreateAsync(new StreamClientTransport(flux, flux));
    }

    static async Task<(JsonElement Json, bool Erreur, string Texte)> Appel(McpClient c, string outil, Dictionary<string, object?>? args = null)
    {
        var r = await c.CallToolAsync(outil, args ?? new());
        var texte = string.Concat(r.Content.OfType<TextContentBlock>().Select(t => t.Text));
        bool erreur = r.IsError == true;
        return (erreur ? default : JsonDocument.Parse(texte).RootElement.Clone(), erreur, texte);
    }

    static void Shot(MainWindow w, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        if (Environment.GetEnvironmentVariable("SILX_SHOT_DIR") is { } dir) frame!.Save(Path.Combine(dir, name));
    }

    static async Task Attendre(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(30); }
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task L_IA_travaille_sous_les_yeux_de_l_utilisateur()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var socket = Socket();
        await w.DemarrerLiaisonIAAsync(socket);
        Assert.NotNull(w.HoteIA);
        await using var c = await Client(socket);
        await Attendre(() => w.BadgeIA == "IA connectée");

        // Les onglets ouverts dans l'application sont ceux de l'IA.
        var (liste, _, texte) = await Appel(c, "list_projects");
        Assert.StartsWith("direct", liste.GetProperty("mode").GetString());
        Assert.Equal(w.Projets.Count, liste.GetProperty("ouverts").GetArrayLength());
        Assert.Contains(w.ProjetActif.Nom, texte);

        // L'IA ouvre une démo : un nouvel onglet, au premier plan.
        int onglets = w.Projets.Count;
        var (p, _, _) = await Appel(c, "open_demo", new() { ["nom"] = "Filtre RC" });
        var id = p.GetProperty("id").GetString();
        Assert.Equal(onglets + 1, w.Projets.Count);
        Assert.Equal("Filtre RC", w.ProjetActif.Nom);

        // Elle simule le banc de Bode : l'écran passe sur ce banc, verdict et courbes affichés sans resimuler.
        var (sim, err, t2) = await Appel(c, "run_simulation", new() { ["projet"] = id, ["banc"] = "Réponse en fréquence" });
        Assert.False(err, t2);
        Assert.Equal("Réponse en fréquence", w.BancActif?.Nom);
        Assert.NotNull(w.Resultats);
        Assert.Equal(sim[0].GetProperty("verdict").GetString() == "PASS", w.Resultats!.Global == Silx.Mesures.Gabarits.Verdict.Pass);
        Shot(w, "90-ia-simulation.png");

        // Elle change R1 : l'écran montre le circuit sous test, R1 sélectionnée ; ⌘Z annule.
        var (_, err2, t3) = await Appel(c, "set_component_value", new() { ["projet"] = id, ["reference"] = "R1", ["valeur"] = "2.2k" });
        Assert.False(err2, t3);
        Assert.Null(w.BancActif);
        Assert.Equal("R1", w.Canevas.Selected?.Ref);
        Assert.Equal("2.2k", w.EtudeCourante.Schema.Components.Single(k => k.Ref == "R1").Value);
        Assert.True(w.ProjetActif.Modifie);
        Assert.Contains("change R1 en 2.2k", w.ActiviteIA[^1]);
        Shot(w, "91-ia-modification.png");
        w.Canevas.Undo();
        Assert.Equal("1k", w.EtudeCourante.Schema.Components.Single(k => k.Ref == "R1").Value);

        // L'onglet fermé par l'utilisateur disparaît pour l'IA.
        w.FermerSansDemander(w.ProjetActif);
        var (l2, _, _) = await Appel(c, "list_projects");
        Assert.Equal(onglets, l2.GetProperty("ouverts").GetArrayLength());
    }

    [AvaloniaFact]
    public async Task Interrompre_puis_reprendre()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var socket = Socket();
        await w.DemarrerLiaisonIAAsync(socket);
        await using var c = await Client(socket);
        var (p, _, _) = await Appel(c, "open_demo", new() { ["nom"] = "Filtre RC" });
        var id = p.GetProperty("id").GetString();

        // Une longue campagne : pendant qu'elle tourne, le badge le dit ; « Interrompre » l'arrête.
        var campagne = Appel(c, "run_campaign", new() { ["projet"] = id, ["banc"] = "1", ["mode"] = "monte_carlo", ["tirages"] = 3000 });
        await Attendre(() => w.IAOccupee);
        Assert.StartsWith("IA : lance une campagne", w.BadgeIA);
        Assert.Equal("Interrompre", w.BoutonIA);
        Shot(w, "92-ia-en-cours.png");
        w.InterrompreIA();
        var (_, err, texte) = await campagne;
        Assert.True(err);
        Assert.Contains("Interrompu par l'utilisateur", texte);
        Assert.Equal("Reprendre", w.BoutonIA);

        // En pause : les appels suivants sont refusés, avec la consigne de s'arrêter.
        var (_, err2, t2) = await Appel(c, "describe_project", new() { ["projet"] = id });
        Assert.True(err2);
        Assert.Contains("pause", t2);
        w.ReprendreIA();
        var (_, err3, t3) = await Appel(c, "describe_project", new() { ["projet"] = id });
        Assert.False(err3, t3);
        Assert.Equal("IA connectée", w.BadgeIA);
    }

    [AvaloniaFact]
    public async Task Projet_modifie_non_ferme_par_l_IA()
    {
        var w = new MainWindow { Width = 1440, Height = 900 }; w.Show();
        var socket = Socket();
        await w.DemarrerLiaisonIAAsync(socket);
        await using var c = await Client(socket);
        var (p, _, _) = await Appel(c, "open_demo", new() { ["nom"] = "Filtre RC" });
        var id = p.GetProperty("id").GetString();
        await Appel(c, "set_component_value", new() { ["projet"] = id, ["reference"] = "C1", ["valeur"] = "470n" });
        int n = w.Projets.Count;
        var (_, err, texte) = await Appel(c, "close_project", new() { ["projet"] = id });
        Assert.True(err);
        Assert.Contains("modifications non enregistrées", texte);
        Assert.Equal(n, w.Projets.Count);

        // Enregistré par l'IA : l'onglet prend le nom du fichier et n'est plus modifié ; il peut alors être fermé.
        var fichier = Path.Combine(Path.GetTempPath(), $"silx-ia-{Guid.NewGuid():N}.silx");
        var (_, err2, t2) = await Appel(c, "save_project", new() { ["projet"] = id, ["chemin"] = fichier });
        Assert.False(err2, t2);
        Assert.False(w.ProjetActif.Modifie);
        Assert.Equal(fichier, w.ProjetActif.Chemin);
        var (_, err3, t3) = await Appel(c, "close_project", new() { ["projet"] = id });
        Assert.False(err3, t3);
        Assert.Equal(n - 1, w.Projets.Count);
        File.Delete(fichier);
    }
}
