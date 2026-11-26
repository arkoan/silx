using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Silx.Etude;
using static Silx.Mcp.Descriptions;
namespace Silx.Mcp;

/// <summary>
/// Outils MCP de Silx. Noms en anglais (convention MCP, ceux de la spec fonctionnelle §3.9), descriptions en français.
/// Chaque outil appelle <see cref="SessionSilx"/> ; une <see cref="ErreurSilx"/> devient une erreur d'outil lisible.
/// </summary>
[McpServerToolType]
public sealed class OutilsSilx(SessionSilx session)
{
    const string DescProjet = "Identifiant du projet (p1, p2… donné par open_project ou list_projects) ou son nom. Facultatif si un seul projet est ouvert.";
    const string DescBanc = "Banc de test : nom ou numéro (à partir de 1). Facultatif si le projet n'a qu'un banc.";

    /// <summary>
    /// Réponses en JSON lisible : noms en snake_case comme les arguments, accents non échappés (le client et
    /// l'utilisateur lisent du français), valeurs nulles omises.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DictionaryKeyPolicy = null,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    static string Garde<T>(Func<T> f)
    {
        try { return JsonSerializer.Serialize<object?>(f(), Json); }
        catch (ErreurSilx ex) { throw new McpException(ex.Message); }
    }

    static async Task<string> GardeAsync<T>(Func<Task<T>> f)
    {
        try { return JsonSerializer.Serialize<object?>(await f(), Json); }
        catch (ErreurSilx ex) { throw new McpException(ex.Message); }
    }

    // ── projets ──

    [McpServerTool(Name = "list_projects", ReadOnly = true, Idempotent = true)]
    [Description("Liste les projets Silx ouverts par ce serveur et les projets de démonstration qu'on peut ouvrir avec open_demo.")]
    public string ListProjects() => Garde(() => new
    {
        ouverts = session.Projets.Select(Resume).ToList(),
        demos = SessionSilx.NomsDemos,
    });

    [McpServerTool(Name = "open_project", ReadOnly = true)]
    [Description("Ouvre un projet Silx (fichier .silx enregistré depuis l'application) et renvoie son identifiant et son contenu : circuit sous test, nœuds, bancs de test. Un projet déjà ouvert est relu depuis le disque.")]
    public string OpenProject([Description("Chemin du fichier .silx (absolu, ou commençant par ~).")] string chemin) =>
        Garde(() => Projet(session.Ouvrir(chemin)));

    [McpServerTool(Name = "open_demo", ReadOnly = true)]
    [Description("Ouvre un projet de démonstration de Silx (filtre RC, filtre à quartz, oscillateur Colpitts, ampli à émetteur commun avec Monte-Carlo) pour essayer les outils.")]
    public string OpenDemo([Description("Nom de la démo, tel que donné par list_projects.")] string nom) =>
        Garde(() => Projet(session.OuvrirDemo(nom)));

    [McpServerTool(Name = "describe_project", ReadOnly = true, Idempotent = true)]
    [Description("Décrit le circuit sous test d'un projet ouvert : composants (référence, type, valeur, tolérance, modèle, nœuds de chaque broche), nœuds nommés, vérifications du schéma (ERC) et liste des bancs.")]
    public string DescribeProject([Description(DescProjet)] string? projet = null) =>
        Garde(() => Projet(session.Projet_(projet)));

    [McpServerTool(Name = "close_project", Destructive = false, Idempotent = true)]
    [Description("Ferme un projet ouvert par ce serveur (les modifications non enregistrées sont perdues ; le fichier n'est pas touché).")]
    public string CloseProject([Description(DescProjet)] string? projet = null) =>
        Garde(() => { var p = session.Projet_(projet); session.Fermer(p.Id); return new { ferme = p.Id, modifications_perdues = p.Modifie }; });

    [McpServerTool(Name = "save_project", Destructive = true)]
    [Description("Enregistre le projet (après set_component_value, par exemple). Sans chemin : remplace le fichier d'origine, seulement avec ecraser = true. Un fichier existant n'est jamais remplacé sans ecraser = true.")]
    public string SaveProject(
        [Description(DescProjet)] string? projet = null,
        [Description("Chemin du fichier .silx à écrire. Facultatif : fichier d'origine.")] string? chemin = null,
        [Description("Autorise à remplacer un fichier existant.")] bool ecraser = false) =>
        Garde(() => { var p = session.Projet_(projet); SessionSilx.Enregistrer(p, chemin, ecraser); return new { enregistre = p.Chemin }; });

    // ── bancs et mesures ──

    [McpServerTool(Name = "list_testbenches", ReadOnly = true, Idempotent = true)]
    [Description("Détaille les bancs de test d'un projet : type d'analyse et réglages, éléments du banc (sources, charges), critères mesurés avec leurs bornes, gabarits dessinés, signaux sondés et campagne réglée (balayage, Monte-Carlo, coins).")]
    public string ListTestbenches(
        [Description(DescProjet)] string? projet = null,
        [Description("Banc à détailler (nom ou numéro). Facultatif : tous les bancs.")] string? banc = null) =>
        Garde(() =>
        {
            var p = session.Projet_(projet);
            var bancs = string.IsNullOrWhiteSpace(banc) ? p.Etude.Bancs.ToList() : [SessionSilx.Banc_(p, banc)];
            return (IReadOnlyList<BancVue>)bancs.Select(b => Banc(p, b)).ToList();
        });

    [McpServerTool(Name = "list_measurements", ReadOnly = true, Idempotent = true)]
    [Description("Catalogue des mesures que Silx sait faire sur une courbe (dépassement, temps de montée, bande passante, marge de phase, THD…), avec leur unité et le type d'analyse concerné.")]
    public string ListMeasurements() =>
        Garde(() => TypesMesure.Tous.Select(m => new MesureVue(m.Type.ToString(), m.Nom, m.Unite, Analyse(m.Analyse))).ToList());

    [McpServerTool(Name = "list_signals", ReadOnly = true, Idempotent = true)]
    [Description("Signaux d'un banc qu'on peut lire avec get_raw_curve : tensions de nœud (OUT, VCC, nœuds internes) et, après simulation, courants de broche (I(R1), I(Q1:1)…).")]
    public string ListSignals([Description(DescProjet)] string? projet = null, [Description(DescBanc)] string? banc = null) =>
        Garde(() =>
        {
            var p = session.Projet_(projet); var b = SessionSilx.Banc_(p, banc);
            return new { banc = b.Nom, simule = p.Simulations.ContainsKey(b), signaux = SessionSilx.SignauxDisponibles(p, b) };
        });

    [McpServerTool(Name = "get_netlist", ReadOnly = true, Idempotent = true)]
    [Description("Netlist SPICE générée pour un banc (circuit sous test + éléments du banc + analyse), telle que Silx la donne à ngspice.")]
    public string GetNetlist([Description(DescProjet)] string? projet = null, [Description(DescBanc)] string? banc = null) =>
        Garde(() =>
        {
            var p = session.Projet_(projet); var b = SessionSilx.Banc_(p, banc);
            var s = b.Fusion(p.Etude.Schema);
            return new { banc = b.Nom, netlist = CircuitBuilder.Preview(s, Connectivity.Build(s), CircuitBuilder.AnalysisFor(b, b.Type)) };
        });

    // ── simulation ──

    [McpServerTool(Name = "run_simulation")]
    [Description("Simule un banc (ou tous les bancs du projet en parallèle) à la valeur nominale avec ngspice, puis évalue les critères et gabarits : verdict PASS/FAIL par contrainte, valeur mesurée, bornes et marge. Les résultats sont gardés pour get_test_summary et get_raw_curve.")]
    public Task<string> RunSimulation(
        [Description(DescProjet)] string? projet = null,
        [Description("Banc à simuler (nom ou numéro). Facultatif : tous les bancs.")] string? banc = null,
        [Description("Joindre le journal ngspice (toujours joint en cas d'erreur).")] bool journal = false,
        CancellationToken ct = default) =>
        GardeAsync(async () =>
        {
            var p = session.Projet_(projet);
            var r = await SessionSilx.SimulerAsync(p, banc, ct);
            return (IReadOnlyList<SimulationVue>)r.Select(x => Simulation(p, x.Banc, x.Resultat, journal)).ToList();
        });

    [McpServerTool(Name = "run_campaign")]
    [Description("Lance une campagne sur un banc : balayage d'un paramètre (composant R, C, L, source continue, ou TEMP), Monte-Carlo dans les tolérances, ou coins (combinaisons extrêmes des tolérances). Sans mode, lance la campagne réglée dans le banc. Les réglages donnés ici ne valent que pour ce lancement (le projet n'est pas modifié). Renvoie le taux de conformité, les statistiques par mesure (moyenne, écart-type, extrêmes, Cpk) et le pire cas.")]
    public Task<string> RunCampaign(
        [Description(DescProjet)] string? projet = null,
        [Description(DescBanc)] string? banc = null,
        [Description("monte_carlo, balayage ou coins. Facultatif : campagne du banc.")] string? mode = null,
        [Description("Monte-Carlo : nombre de tirages (100 par défaut).")] int? tirages = null,
        [Description("Monte-Carlo : loi gauss (± tolérance = 3 σ) ou uniforme.")] string? loi = null,
        [Description("Monte-Carlo : graine du générateur (résultats reproductibles).")] int? graine = null,
        [Description("Balayage : référence du composant balayé (R1, C2, VCC…) ou TEMP.")] string? cible = null,
        [Description("Balayage : première valeur (SI, ex. 1000 ou « 1k »).")] string? debut = null,
        [Description("Balayage : dernière valeur.")] string? fin = null,
        [Description("Balayage : nombre de points (5 par défaut).")] int? points = null,
        [Description("Balayage : lineaire ou log.")] string? echelle = null,
        [Description("Balayage : liste de valeurs à la place de début/fin (« 1k 2.2k 4.7k »).")] string? valeurs = null,
        [Description("Coins : ajouter la température mini et maxi aux combinaisons.")] bool? temperature = null,
        [Description("Coins : température mini en °C (−40 par défaut).")] double? tmin = null,
        [Description("Coins : température maxi en °C (85 par défaut).")] double? tmax = null,
        IProgress<ProgressNotificationValue>? progression = null,
        CancellationToken ct = default) =>
        GardeAsync(async () =>
        {
            var p = session.Projet_(projet); var b = SessionSilx.Banc_(p, banc);
            var c = Reglages(b.Campagne, mode, tirages, loi, graine, cible, debut, fin, points, echelle, valeurs, temperature, tmin, tmax);
            var prog = progression == null ? null : new Progress<int>(n => progression.Report(new ProgressNotificationValue { Progress = n, Message = $"{n} tirages simulés" }));
            var r = await SessionSilx.CampagneAsync(p, b.Nom, c, prog, ct);
            return CampagneResultat(p, b, r);
        });

    /// <summary>Réglages de campagne : ceux du banc, recopiés, puis remplacés par les arguments donnés.</summary>
    internal static Campagne? Reglages(Campagne du_banc, string? mode, int? tirages, string? loi, int? graine, string? cible, string? debut, string? fin,
        int? points, string? echelle, string? valeurs, bool? temperature, double? tmin, double? tmax)
    {
        bool rien = mode == null && tirages == null && loi == null && graine == null && cible == null && debut == null && fin == null && points == null
            && echelle == null && valeurs == null && temperature == null && tmin == null && tmax == null;
        if (rien) return null;
        var c = new Campagne
        {
            Mode = du_banc.Mode, Cible = du_banc.Cible, Debut = du_banc.Debut, Fin = du_banc.Fin, Points = du_banc.Points, Echelle = du_banc.Echelle, Liste = du_banc.Liste,
            Tirages = du_banc.Tirages, Loi = du_banc.Loi, Graine = du_banc.Graine, CoinsTemperature = du_banc.CoinsTemperature, TMin = du_banc.TMin, TMax = du_banc.TMax,
        };
        if (mode != null)
            c.Mode = mode.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_') switch
            {
                "monte_carlo" or "montecarlo" or "mc" => ModeCampagne.MonteCarlo,
                "balayage" or "parametrique" or "paramétrique" or "sweep" => ModeCampagne.Balayage,
                "coins" or "corners" or "pire_cas" => ModeCampagne.Coins,
                _ => throw new ErreurSilx($"Mode « {mode} » inconnu : monte_carlo, balayage ou coins."),
            };
        if (tirages is { } t) { if (t < 1 || t > 100_000) throw new ErreurSilx("Nombre de tirages entre 1 et 100 000."); c.Tirages = t; }
        if (loi != null)
            c.Loi = loi.Trim().ToLowerInvariant() switch
            {
                "gauss" or "gaussienne" or "normale" => LoiTirage.Gauss,
                "uniforme" or "uniform" => LoiTirage.Uniforme,
                _ => throw new ErreurSilx($"Loi « {loi} » inconnue : gauss ou uniforme."),
            };
        if (graine is { } g) c.Graine = g;
        if (cible != null) c.Cible = cible.Trim();
        double Nombre(string s, string champ) => SpiceNumber.ParseOrNull(s) ?? throw new ErreurSilx($"{champ} : « {s} » n'est pas un nombre (ex. 1000, 4.7k, 100n).");
        if (debut != null) c.Debut = Nombre(debut, "debut");
        if (fin != null) c.Fin = Nombre(fin, "fin");
        if (points is { } n) { if (n < 1 || n > 1000) throw new ErreurSilx("Nombre de points entre 1 et 1000."); c.Points = n; }
        if (echelle != null)
            c.Echelle = echelle.Trim().ToLowerInvariant() switch
            {
                "lineaire" or "linéaire" or "lin" or "linear" => EchelleBalayage.Lineaire,
                "log" or "logarithmique" or "dec" => EchelleBalayage.Logarithmique,
                "liste" or "list" => EchelleBalayage.Liste,
                _ => throw new ErreurSilx($"Échelle « {echelle} » inconnue : lineaire, log ou liste."),
            };
        if (valeurs != null) { c.Liste = valeurs; c.Echelle = EchelleBalayage.Liste; }
        if (temperature is { } tp) c.CoinsTemperature = tp;
        if (tmin is { } a) c.TMin = a;
        if (tmax is { } z) c.TMax = z;
        return c;
    }

    // ── résultats ──

    [McpServerTool(Name = "get_test_summary", ReadOnly = true, Idempotent = true)]
    [Description("Bilan des derniers résultats du projet, sans resimuler : verdict global, verdict et contraintes non respectées par banc, résumé de la dernière campagne. Un banc jamais simulé est signalé « non simulé ».")]
    public string GetTestSummary([Description(DescProjet)] string? projet = null) =>
        Garde(() => ResumeTests(session.Projet_(projet)));

    [McpServerTool(Name = "get_worst_case", ReadOnly = true, Idempotent = true)]
    [Description("Pire cas de la dernière campagne d'un banc (le tirage qui franchit le plus de contraintes, puis à la plus petite marge) : valeurs des composants, température, mesures et verdicts. Peut aussi renvoyer les N tirages les plus défavorables.")]
    public string GetWorstCase(
        [Description(DescProjet)] string? projet = null,
        [Description(DescBanc)] string? banc = null,
        [Description("Nombre de tirages les plus défavorables à renvoyer (1 par défaut, 20 au plus).")] int nombre = 1) =>
        Garde(() =>
        {
            var p = session.Projet_(projet); var b = SessionSilx.Banc_(p, banc);
            if (!p.Campagnes.TryGetValue(b, out var r)) throw new ErreurSilx($"Aucune campagne lancée sur le banc « {b.Nom} » : utilisez run_campaign.");
            var fusion = b.Fusion(p.Etude.Schema);
            var pires = r.Tirages.Where(t => t.Erreur == null).OrderByDescending(t => t.Echecs).ThenBy(t => t.MargeRelative).Take(Math.Clamp(nombre, 1, 20))
                .Select(t => Tirage(t, fusion)).ToList();
            return new { banc = b.Nom, mode = Mode(r.Mode), tirages = r.Tirages.Count, conformes = r.Conformes, pires_cas = pires };
        });

    [McpServerTool(Name = "get_raw_curve", ReadOnly = true, Idempotent = true)]
    [Description("Points d'une courbe de la dernière simulation nominale d'un banc : v(t) en transitoire, gain en dB en AC, caractéristique de transfert en DC (abscisse : la source balayée) ; tension de nœud (« OUT ») ou courant de broche (« I(R1) »). La courbe est réduite à max_points en gardant pics et fronts.")]
    public string GetRawCurve(
        [Description("Signal : nom de nœud (OUT, V(OUT)) ou courant (I(R1), I(Q1:1)). Voir list_signals.")] string signal,
        [Description(DescProjet)] string? projet = null,
        [Description(DescBanc)] string? banc = null,
        [Description("Nombre maximal de points renvoyés (500 par défaut).")] int max_points = 500,
        [Description("Début de la plage en x (s ou Hz). Facultatif.")] double? x_min = null,
        [Description("Fin de la plage en x (s ou Hz). Facultatif.")] double? x_max = null) =>
        Garde(() =>
        {
            var p = session.Projet_(projet); var b = SessionSilx.Banc_(p, banc);
            var (x, y, ux, uy, n) = SessionSilx.Courbe(p, b.Nom, signal, max_points, x_min, x_max);
            return new CourbeVue(b.Nom, Signaux.Nom(SessionSilx.NormaliserSignal(signal), b.Fusion(p.Etude.Schema)), ux, uy, n, x.Length, x, y);
        });

    [McpServerTool(Name = "get_operating_point", ReadOnly = true, Idempotent = true)]
    [Description("Point de fonctionnement continu (.op) : tension de chaque nœud et courant de chaque composant (entrant par la broche 1 ; collecteur ou drain pour un transistor). Circuit seul (alimentations du circuit), ou avec un banc branché, ses sources à leur valeur continue.")]
    public Task<string> GetOperatingPoint(
        [Description(DescProjet)] string? projet = null,
        [Description("Banc à brancher (nom ou numéro). Facultatif : circuit seul.")] string? banc = null,
        CancellationToken ct = default) =>
        GardeAsync(async () =>
        {
            var p = session.Projet_(projet);
            var r = await SessionSilx.PointAsync(p, banc, ct);
            if (r.Erreur != null) throw new ErreurSilx(r.Erreur);
            return new PointFonctionnementVue(string.IsNullOrWhiteSpace(banc) ? null : SessionSilx.Banc_(p, banc).Nom,
                r.Tensions.Where(kv => kv.Key != "0").OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
                r.Courants.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value), null);
        });

    [McpServerTool(Name = "get_noise", ReadOnly = true, Idempotent = true)]
    [Description("Bruit d'un nœud sur la plage d'un banc fréquentiel (analyse .noise de ngspice) : densités spectrales en sortie et ramenée à l'entrée (V/√Hz), bruits totaux intégrés sur la plage (V efficaces) et densité à 1 kHz. Le banc doit avoir une source petit signal (AC) comme entrée.")]
    public Task<string> GetNoise(
        [Description("Nœud observé (OUT, V(OUT)…).")] string noeud,
        [Description(DescProjet)] string? projet = null,
        [Description(DescBanc)] string? banc = null,
        [Description("Nombre maximal de points de densité renvoyés (200 par défaut).")] int max_points = 200,
        CancellationToken ct = default) =>
        GardeAsync(async () =>
        {
            var p = session.Projet_(projet);
            var (b, c) = await SessionSilx.BruitAsync(p, banc, noeud, ct);
            int n = c.F.Length, pas = Math.Max(1, (int)Math.Ceiling(n / (double)Math.Clamp(max_points, 2, 20_000)));
            var idx = Enumerable.Range(0, n).Where(i => i % pas == 0 || i == n - 1).ToArray();
            double? Fini(double v) => double.IsFinite(v) ? v : null;
            var src = Bruit.Entree(b)!;
            return new BruitVue(b.Nom, c.Noeud, src.Ref, src.Kind == ComponentKind.CurrentSource ? "sortie V/√Hz, entrée A/√Hz" : "V/√Hz",
                Fini(c.TotalSortie), Fini(c.TotalEntree), c.SortieA(1e3), idx.Length,
                idx.Select(i => c.F[i]).ToArray(), idx.Select(i => c.Sortie[i]).ToArray(), idx.Select(i => c.Entree[i]).ToArray());
        });

    // ── modification ──

    [McpServerTool(Name = "set_component_value", Destructive = false)]
    [Description("Change la valeur (et éventuellement la tolérance) d'un composant, en mémoire : circuit sous test par défaut, ou élément d'un banc. Les résultats précédents sont effacés ; relancez run_simulation. Le fichier n'est modifié qu'avec save_project.")]
    public Task<string> SetComponentValue(
        [Description("Référence du composant (R1, C2, V1…).")] string reference,
        [Description("Nouvelle valeur : nombre en notation ingénieur (4.7k, 100n) pour R, C, L ; texte SPICE pour une source (DC 12, AC 1, PULSE(…)) ou un paramètre de modèle.")] string? valeur = null,
        [Description(DescProjet)] string? projet = null,
        [Description("Banc qui porte le composant (source, charge). Facultatif : circuit sous test.")] string? banc = null,
        [Description("Tolérance en % utilisée par Monte-Carlo et coins. Facultatif.")] double? tolerance = null,
        CancellationToken ct = default) =>
        GardeAsync(async () =>
        {
            if (valeur == null && tolerance == null) throw new ErreurSilx("Indiquez une valeur ou une tolérance.");
            var p = session.Projet_(projet);
            var (k, ancienne, lieu) = await SessionSilx.ChangerValeurAsync(p, reference, valeur!, banc, tolerance, ct);
            return (object)new { composant = k.Ref, lieu, ancienne_valeur = ancienne, valeur = k.Value, tolerance = k.Tolerance, projet_modifie = true };
        });
}
