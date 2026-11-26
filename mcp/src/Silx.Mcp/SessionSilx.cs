using System.Collections.Concurrent;
using Silx.Etude;
using Silx.Mesures.Gabarits;
namespace Silx.Mcp;

/// <summary>Erreur destinée au client MCP : message clair en français, renvoyé tel quel comme erreur d'outil.</summary>
public sealed class ErreurSilx(string message) : Exception(message);

/// <summary>Un projet ouvert par le serveur, avec les derniers résultats de chaque banc (simulation nominale, campagne).</summary>
public sealed class ProjetCharge(string id, string nom, string? chemin, Etude.Etude etude)
{
    public string Id { get; } = id;
    public string Nom { get; } = nom;
    /// <summary>Fichier .silx d'origine ; null pour une démo (à enregistrer sous un chemin choisi).</summary>
    public string? Chemin { get; set; } = chemin;
    public Etude.Etude Etude { get; } = etude;
    /// <summary>Modifié depuis l'ouverture ou le dernier enregistrement.</summary>
    public bool Modifie { get; set; }
    public Dictionary<Banc, ResultatEtude> Simulations { get; } = new();
    public Dictionary<Banc, ResultatCampagne> Campagnes { get; } = new();
    /// <summary>Une seule opération à la fois par projet (simulation, campagne, modification).</summary>
    public SemaphoreSlim Verrou { get; } = new(1, 1);
}

/// <summary>
/// État du serveur : projets ouverts (par identifiant court) et logique des outils, indépendante du protocole MCP
/// (les outils de <see cref="OutilsSilx"/> ne font que l'appeler, les tests aussi).
/// </summary>
public sealed class SessionSilx
{
    readonly ConcurrentDictionary<string, ProjetCharge> _projets = new(StringComparer.OrdinalIgnoreCase);
    int _compteur;

    public IReadOnlyCollection<ProjetCharge> Projets => _projets.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();

    // ── projets ──

    public ProjetCharge Ouvrir(string chemin)
    {
        if (string.IsNullOrWhiteSpace(chemin)) throw new ErreurSilx("Indiquez le chemin d'un projet Silx (.silx).");
        var plein = Path.GetFullPath(Environment.ExpandEnvironmentVariables(chemin.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), chemin.TrimStart('~', '/', '\\')) : chemin));
        if (!string.Equals(Path.GetExtension(plein), Projet.Extension, StringComparison.OrdinalIgnoreCase))
            throw new ErreurSilx($"« {chemin} » n'est pas un projet Silx (extension {Projet.Extension} attendue).");
        if (!File.Exists(plein)) throw new ErreurSilx($"Projet introuvable : {plein}");
        // Déjà ouvert : on le relit (le fichier a pu changer depuis l'application) en gardant le même identifiant.
        var deja = _projets.Values.FirstOrDefault(p => p.Chemin != null && string.Equals(p.Chemin, plein, StringComparison.Ordinal));
        Etude.Etude e;
        try { e = Projet.Ouvrir(plein); }
        catch (InvalidDataException ex) { throw new ErreurSilx(ex.Message); }
        var p = new ProjetCharge(deja?.Id ?? NouvelId(), Path.GetFileNameWithoutExtension(plein), plein, e);
        _projets[p.Id] = p;
        return p;
    }

    public static IReadOnlyList<string> NomsDemos => ["Filtre RC", .. Demos.Toutes.Select(d => d.Nom)];

    public ProjetCharge OuvrirDemo(string nom)
    {
        Func<Etude.Etude>? creer = string.Equals(nom?.Trim(), "Filtre RC", StringComparison.OrdinalIgnoreCase) ? Etude.Etude.Demo
            : Demos.Toutes.FirstOrDefault(d => string.Equals(d.Nom, nom?.Trim(), StringComparison.OrdinalIgnoreCase)).Creer;
        if (creer == null) throw new ErreurSilx($"Démo « {nom} » inconnue. Démos disponibles : {string.Join(", ", NomsDemos)}.");
        var p = new ProjetCharge(NouvelId(), NomsDemos.First(n => string.Equals(n, nom!.Trim(), StringComparison.OrdinalIgnoreCase)), null, creer());
        _projets[p.Id] = p;
        return p;
    }

    string NouvelId() => "p" + Interlocked.Increment(ref _compteur);

    public ProjetCharge Projet_(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            // Un seul projet ouvert : il est pris par défaut, pour alléger les appels.
            if (_projets.Count == 1) return _projets.Values.First();
            throw new ErreurSilx(_projets.IsEmpty ? "Aucun projet ouvert : utilisez open_project ou open_demo." : "Plusieurs projets ouverts : précisez l'identifiant (voir list_projects).");
        }
        if (_projets.TryGetValue(id.Trim(), out var p)) return p;
        var parNom = _projets.Values.Where(x => string.Equals(x.Nom, id.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (parNom.Count == 1) return parNom[0];
        throw new ErreurSilx($"Projet « {id} » non ouvert. Projets ouverts : {(_projets.IsEmpty ? "aucun" : string.Join(", ", Projets.Select(x => $"{x.Id} ({x.Nom})")))}.");
    }

    public bool Fermer(string? id) => _projets.TryRemove(Projet_(id).Id, out _);

    /// <summary>Banc par nom (sans tenir compte de la casse) ou par numéro (à partir de 1).</summary>
    public static Banc Banc_(ProjetCharge p, string? banc)
    {
        var bancs = p.Etude.Bancs;
        if (bancs.Count == 0) throw new ErreurSilx($"Le projet « {p.Nom} » n'a aucun banc de test.");
        if (string.IsNullOrWhiteSpace(banc))
        {
            if (bancs.Count == 1) return bancs[0];
            throw new ErreurSilx($"Précisez le banc : {string.Join(", ", bancs.Select((b, i) => $"{i + 1} « {b.Nom} »"))}.");
        }
        var b0 = bancs.FirstOrDefault(b => string.Equals(b.Nom.Trim(), banc.Trim(), StringComparison.OrdinalIgnoreCase));
        if (b0 != null) return b0;
        if (int.TryParse(banc, out var n) && n >= 1 && n <= bancs.Count) return bancs[n - 1];
        throw new ErreurSilx($"Banc « {banc} » introuvable. Bancs du projet : {string.Join(", ", bancs.Select((b, i) => $"{i + 1} « {b.Nom} »"))}.");
    }

    public static void Enregistrer(ProjetCharge p, string? chemin, bool ecraser)
    {
        string cible;
        if (string.IsNullOrWhiteSpace(chemin))
        {
            cible = p.Chemin ?? throw new ErreurSilx("Projet de démonstration : indiquez le chemin du fichier .silx à créer.");
            if (!ecraser) throw new ErreurSilx($"Enregistrer remplacerait {cible} : confirmez avec ecraser = true, ou donnez un autre chemin.");
        }
        else
        {
            cible = Path.GetFullPath(chemin);
            if (!string.Equals(Path.GetExtension(cible), Projet.Extension, StringComparison.OrdinalIgnoreCase)) cible += Projet.Extension;
            if (File.Exists(cible) && !ecraser) throw new ErreurSilx($"{cible} existe déjà : confirmez avec ecraser = true, ou donnez un autre chemin.");
            var dossier = Path.GetDirectoryName(cible);
            if (dossier != null && !Directory.Exists(dossier)) throw new ErreurSilx($"Dossier introuvable : {dossier}");
        }
        try { Projet.Enregistrer(p.Etude, cible); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new ErreurSilx($"Enregistrement impossible : {ex.Message}"); }
        p.Chemin = cible; p.Modifie = false;
    }

    // ── simulation ──

    /// <summary>Simulation nominale d'un banc, ou de tous les bancs en parallèle (non-régression).</summary>
    public static async Task<IReadOnlyList<(Banc Banc, ResultatEtude Resultat)>> SimulerAsync(ProjetCharge p, string? banc, CancellationToken ct)
    {
        var bancs = string.IsNullOrWhiteSpace(banc) ? p.Etude.Bancs.ToList() : [Banc_(p, banc)];
        if (bancs.Count == 0) throw new ErreurSilx($"Le projet « {p.Nom} » n'a aucun banc de test.");
        await p.Verrou.WaitAsync(ct);
        try
        {
            var r = await Task.WhenAll(bancs.Select(async b => (b, await Verification.RunAsync(p.Etude, b, [], ct))));
            foreach (var (b, res) in r) p.Simulations[b] = res;
            return r;
        }
        finally { p.Verrou.Release(); }
    }

    /// <summary>
    /// Campagne d'un banc (balayage, Monte-Carlo, coins). Les réglages donnés remplacent ceux du banc pour ce lancement
    /// seulement ; sans réglage, la campagne enregistrée dans le banc est lancée.
    /// </summary>
    public static async Task<ResultatCampagne> CampagneAsync(ProjetCharge p, string? banc, Campagne? reglages, IProgress<int>? progression, CancellationToken ct)
    {
        var b = Banc_(p, banc);
        var c = reglages ?? b.Campagne;
        if (c.Mode == ModeCampagne.Aucune)
            throw new ErreurSilx($"Le banc « {b.Nom} » n'a pas de campagne réglée : précisez le mode (monte_carlo, balayage, coins).");
        await p.Verrou.WaitAsync(ct);
        var avant = b.Campagne;
        try
        {
            b.Campagne = c;
            var signaux = b.Graphiques.SelectMany(g => g).Concat(b.CriteresActifs.Select(x => x.Noeud)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var r = await Campagnes.RunAsync(p.Etude, b, signaux, progression, ct);
            p.Campagnes[b] = r;
            return r;
        }
        finally { b.Campagne = avant; p.Verrou.Release(); }
    }

    // ── modification ──

    /// <summary>Change la valeur d'un composant du circuit (ou du banc donné). Les résultats précédents sont oubliés.</summary>
    public static async Task<(PlacedComponent Composant, string Ancienne, string Lieu)> ChangerValeurAsync(ProjetCharge p, string reference, string valeur, string? banc, double? tolerance, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new ErreurSilx("Indiquez la référence du composant (R1, C2, V1…).");
        await p.Verrou.WaitAsync(ct);
        try
        {
            Schematic s; string lieu;
            if (string.IsNullOrWhiteSpace(banc)) { s = p.Etude.Schema; lieu = "circuit sous test"; }
            else { var b = Banc_(p, banc); s = b.Calque; lieu = $"banc « {b.Nom} »"; }
            var k = s.Components.FirstOrDefault(c => c.IsElectrical && string.Equals(c.Ref, reference.Trim(), StringComparison.OrdinalIgnoreCase));
            if (k == null)
            {
                // Aide : le composant est peut-être dans un banc (source, charge) plutôt que dans le circuit.
                var ailleurs = p.Etude.Bancs.FirstOrDefault(b => b.Calque.Components.Any(c => string.Equals(c.Ref, reference.Trim(), StringComparison.OrdinalIgnoreCase)));
                throw new ErreurSilx($"{reference} introuvable dans le {lieu}." + (banc == null && ailleurs != null ? $" Il fait partie du banc « {ailleurs.Nom} » : précisez banc." : ""));
            }
            var ancienne = k.Value;
            if (valeur != null)
            {
                var v = valeur.Trim();
                if (v.Length == 0) throw new ErreurSilx("Valeur vide.");
                if (k.Kind is ComponentKind.Resistor or ComponentKind.Capacitor or ComponentKind.Inductor)
                {
                    if (SpiceNumber.ParseOrNull(v) is not { } x || x <= 0) throw new ErreurSilx($"« {valeur} » n'est pas une valeur positive (ex. 4.7k, 100n, 10u).");
                    v = SpiceNumber.NormaliserValeur(v);
                }
                k.Value = v;
            }
            if (tolerance is { } t)
            {
                if (t < 0 || t > 100) throw new ErreurSilx("Tolérance en % entre 0 et 100.");
                k.Tolerance = t;
            }
            p.Modifie = true;
            p.Simulations.Clear(); p.Campagnes.Clear();
            return (k, ancienne, lieu);
        }
        finally { p.Verrou.Release(); }
    }

    // ── courbes ──

    /// <summary>Courbe d'un signal tirée de la dernière simulation nominale du banc, réduite à <paramref name="maxPoints"/>.</summary>
    public static (double[] X, double[] Y, string UniteX, string UniteY, int PointsSimules) Courbe(ProjetCharge p, string? banc, string signal, int maxPoints, double? xMin, double? xMax)
    {
        var b = Banc_(p, banc);
        if (!p.Simulations.TryGetValue(b, out var r)) throw new ErreurSilx($"Le banc « {b.Nom} » n'a pas encore été simulé : lancez run_simulation.");
        if (r.Erreur != null) throw new ErreurSilx($"La dernière simulation du banc « {b.Nom} » a échoué : {r.Erreur}");
        if (!r.Resultats.TryGetValue(b.Type, out var raw)) throw new ErreurSilx("Résultats absents pour ce banc.");
        var fusion = b.Fusion(p.Etude.Schema);
        var nom = NormaliserSignal(signal);
        if (!Signaux.Disponible(raw, nom, fusion))
            throw new ErreurSilx($"Signal « {signal} » absent des résultats. Signaux disponibles : {string.Join(", ", SignauxDisponibles(p, b).Take(60))}.");
        var (x, y) = Verification.Courbe(b.Type, raw, nom, fusion);
        int n0 = x.Length;
        var idx = Enumerable.Range(0, x.Length).Where(i => (xMin is not { } a || x[i] >= a) && (xMax is not { } z || x[i] <= z)).ToArray();
        maxPoints = Math.Clamp(maxPoints, 2, 20_000);
        if (idx.Length > maxPoints) idx = Reduire(x, y, idx, maxPoints);
        return (idx.Select(i => x[i]).ToArray(), idx.Select(i => y[i]).ToArray(), Descriptions.UniteX(b), b.Type == AnalyseKind.Ac ? "dB" : Signaux.Unite(nom), n0);
    }

    /// <summary>Point de fonctionnement (.op) du circuit seul, ou avec un banc branché (stimuli à leur valeur continue).</summary>
    public static async Task<ResultatOp> PointAsync(ProjetCharge p, string? banc, CancellationToken ct)
    {
        var b = string.IsNullOrWhiteSpace(banc) ? null : Banc_(p, banc);
        await p.Verrou.WaitAsync(ct);
        try { return await PointFonctionnement.CalculerAsync(p.Etude, b, ct); }
        finally { p.Verrou.Release(); }
    }

    /// <summary>Bruit d'un nœud sur la plage d'un banc fréquentiel (densités en sortie et ramenée à l'entrée, totaux).</summary>
    public static async Task<(Banc Banc, CourbeBruit Courbe)> BruitAsync(ProjetCharge p, string? banc, string noeud, CancellationToken ct)
    {
        var b = Banc_(p, banc);
        var n = NormaliserSignal(noeud);
        if (Signaux.EstCourant(n)) throw new ErreurSilx("Le bruit se calcule sur une tension de nœud (OUT, IN…).");
        var conn = Connectivity.Build(b.Fusion(p.Etude.Schema));
        var vrai = conn.AllNodes.FirstOrDefault(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))
            ?? throw new ErreurSilx($"Nœud « {noeud} » absent du banc « {b.Nom} ». Nœuds : {string.Join(", ", conn.NamedNodes)}.");
        await p.Verrou.WaitAsync(ct);
        try
        {
            var (courbes, err) = await Bruit.CalculerAsync(p.Etude, b, [vrai], ct);
            if (err != null) throw new ErreurSilx(err);
            return (b, courbes.TryGetValue(vrai, out var c) ? c : throw new ErreurSilx($"Bruit de « {vrai} » non calculé."));
        }
        finally { p.Verrou.Release(); }
    }

    /// <summary>« V(OUT) » ou « v(out) » → « OUT » ; les courants « I(R1:2) » restent tels quels.</summary>
    public static string NormaliserSignal(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length > 3 && (s[0] is 'V' or 'v') && s[1] == '(' && s[^1] == ')') return s[2..^1].Trim();
        if (s.Length > 3 && s[0] == 'i' && s[1] == '(') return "I" + s[1..];
        return s;
    }

    /// <summary>
    /// Réduction qui garde l'allure : par paquet, le premier point, le minimum et le maximum (les pics, fronts et
    /// résonances restent visibles), plus le dernier point de la plage.
    /// </summary>
    static int[] Reduire(double[] x, double[] y, int[] idx, int max)
    {
        int paquets = Math.Max(1, (max - 1) / 3);
        double taille = idx.Length / (double)paquets;
        var garde = new SortedSet<int>();
        for (int k = 0; k < paquets; k++)
        {
            int a = (int)(k * taille), z = Math.Min(idx.Length, (int)((k + 1) * taille));
            if (a >= z) continue;
            int imin = a, imax = a;
            for (int i = a; i < z; i++) { if (y[idx[i]] < y[idx[imin]]) imin = i; if (y[idx[i]] > y[idx[imax]]) imax = i; }
            garde.Add(idx[a]); garde.Add(idx[imin]); garde.Add(idx[imax]);
        }
        garde.Add(idx[^1]);
        return garde.ToArray();
    }

    /// <summary>Signaux qu'on peut demander : nœuds nommés et internes, courants des composants (dernière simulation).</summary>
    public static IReadOnlyList<string> SignauxDisponibles(ProjetCharge p, Banc b)
    {
        var fusion = b.Fusion(p.Etude.Schema);
        var conn = Connectivity.Build(fusion);
        var l = conn.NamedNodes.Concat(conn.AllNodes).Where(n => n != "0").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (p.Simulations.TryGetValue(b, out var r) && r.Resultats.TryGetValue(b.Type, out var raw))
        {
            l = l.Where(n => Signaux.Disponible(raw, n, fusion)).ToList();
            foreach (var k in fusion.Components.Where(k => k.IsElectrical))
                for (int i = 0; i < k.Pins.Count; i++)
                {
                    var s = Signaux.Courant(k, i);
                    if (Signaux.Disponible(raw, s, fusion)) l.Add(s);
                }
        }
        return l;
    }
}
