using Silx.Etude;
using Silx.Mesures.Gabarits;
namespace Silx.Mcp;

/// <summary>
/// Vues renvoyées au client MCP (sérialisées en JSON) : des objets simples, avec les valeurs en unités SI et leur
/// écriture ingénieur (« 2.2m »), pour qu'un modèle de langage les lise sans ambiguïté.
/// </summary>
public static class Descriptions
{
    public sealed record ProjetResume(string Id, string Nom, string? Chemin, bool Modifie, int Composants, IReadOnlyList<string> Bancs);

    public sealed record ComposantVue(string Ref, string Type, string Valeur, double? Tolerance, string? Modele, IReadOnlyList<string> Noeuds);
    public sealed record ProjetVue(string Id, string Nom, string? Chemin, bool Modifie, IReadOnlyList<ComposantVue> Circuit, IReadOnlyList<string> NoeudsNommes,
        IReadOnlyList<string> Verifications, IReadOnlyList<BancResume> Bancs);
    public sealed record BancResume(int Numero, string Nom, string Analyse, int Criteres, int GabaritsDessines, string Campagne);

    public sealed record CritereVue(string Mesure, string Nom, string Signal, double? Min, double? Max, string Unite);
    public sealed record GabaritVue(string Nom, string Signal, string Limite, IReadOnlyList<double[]> Points, string UniteX, string UniteY, bool RelatifAuFront);
    public sealed record CampagneVue(string Mode, string? Cible = null, double? Debut = null, double? Fin = null, int? Points = null, string? Echelle = null, string? Liste = null,
        int? Tirages = null, string? Loi = null, int? Graine = null, bool? Temperature = null, double? TMin = null, double? TMax = null, IReadOnlyList<string>? ComposantsDisperses = null);
    public sealed record BancVue(int Numero, string Nom, string Analyse, Dictionary<string, object> Reglages, IReadOnlyList<ComposantVue> ElementsDuBanc,
        IReadOnlyList<CritereVue> Criteres, IReadOnlyList<GabaritVue> GabaritsDessines, IReadOnlyList<IReadOnlyList<string>> Graphiques, CampagneVue Campagne);

    public sealed record MesureVue(string Id, string Nom, string Unite, string Analyse);

    public sealed record ResultatCritereVue(string Nom, string Signal, string Verdict, double? Valeur, string? ValeurTexte, double? Min, double? Max, string Unite, double? Marge, string? Detail);
    public sealed record ResultatGabaritVue(string Nom, string Verdict, double? Marge, double? XPire, double? YPire, string? Detail);
    public sealed record SimulationVue(string Banc, string Analyse, string Verdict, int Respectes, int Contraintes, IReadOnlyList<ResultatCritereVue> Criteres,
        IReadOnlyList<ResultatGabaritVue> GabaritsDessines, IReadOnlyList<string> Verifications, string? Erreur, string? JournalNgspice);

    public sealed record StatVue(string Mesure, string Signal, string Unite, int N, double? Moyenne, double? EcartType, double? Min, double? Max, double? Cpk, int Echecs);
    public sealed record TirageVue(int Numero, string Libelle, string Verdict, int Echecs, double MargeRelative, Dictionary<string, double> Valeurs, double? Temperature,
        IReadOnlyList<ResultatCritereVue> Criteres, IReadOnlyList<ResultatGabaritVue> GabaritsDessines, string? Erreur);
    public sealed record CampagneResultatVue(string Banc, string Mode, int Tirages, int Conformes, double TauxConformite, int EnErreur, IReadOnlyList<StatVue> Statistiques,
        TirageVue? PireCas, string? Avertissement);

    public sealed record ResumeTestsVue(string Projet, string Verdict, IReadOnlyList<ResumeBancVue> Bancs);
    public sealed record ResumeBancVue(string Banc, string Analyse, string Simulation, string? Verdict, int? Respectes, int? Contraintes, IReadOnlyList<string> Echecs,
        string? Erreur, string? Campagne);

    public sealed record CourbeVue(string Banc, string Signal, string UniteX, string UniteY, int PointsSimules, int PointsRendus, double[] X, double[] Y);

    // ── conversions ──

    public static string Analyse(AnalyseKind k) => k switch
    {
        AnalyseKind.Transitoire => "transitoire",
        AnalyseKind.Ac => "ac",
        _ => k.ToString().ToLowerInvariant(),
    };

    public static string Verdict_(Verdict v) => v switch { Verdict.Pass => "PASS", Verdict.Fail => "FAIL", _ => "NON_EVALUABLE" };

    public static string Mode(ModeCampagne m) => m switch
    {
        ModeCampagne.MonteCarlo => "monte_carlo", ModeCampagne.Balayage => "balayage", ModeCampagne.Coins => "coins", _ => "aucune"
    };

    static string Type(PlacedComponent k) => k.Kind switch
    {
        ComponentKind.Resistor => "résistance", ComponentKind.Capacitor => "condensateur", ComponentKind.Inductor => "inductance",
        ComponentKind.VoltageSource => "source de tension", ComponentKind.CurrentSource => "source de courant", ComponentKind.Diode => "diode",
        ComponentKind.Spice => k.Definition?.Lettre switch
        {
            "Q" => "transistor bipolaire", "M" => "MOSFET", "J" => "JFET", "D" => "diode", "X" => "sous-circuit SPICE", "A" => "composant logique XSPICE", _ => "composant SPICE"
        },
        _ => k.Kind.ToString(),
    };

    public static ProjetResume Resume(ProjetCharge p) =>
        new(p.Id, p.Nom, p.Chemin, p.Modifie, p.Etude.Schema.Components.Count(k => k.IsElectrical), p.Etude.Bancs.Select(b => b.Nom).ToList());

    static IReadOnlyList<ComposantVue> Composants(Schematic s, Connectivity conn) =>
        s.Components.Where(k => k.IsElectrical).OrderBy(k => k.Ref, StringComparer.Ordinal).Select(k => new ComposantVue(
            k.Ref, Type(k), k.Value, k.Tolerance ?? Campagnes.Tolerance(k), k.Definition?.Nom,
            k.Pins.Select(pt => conn.NodeOf.TryGetValue(pt, out var n) ? n : "?").ToList())).ToList();

    public static ProjetVue Projet(ProjetCharge p)
    {
        var s = p.Etude.Schema;
        var conn = Connectivity.Build(s);
        var erc = Erc.Check(s, conn, circuitSeul: true);
        return new(p.Id, p.Nom, p.Chemin, p.Modifie, Composants(s, conn), conn.NamedNodes.ToList(),
            erc.Select(i => $"{(i.Severity == Severity.Error ? "Erreur" : "Avertissement")} : {i.Message}").ToList(),
            p.Etude.Bancs.Select((b, i) => new BancResume(i + 1, b.Nom, Analyse(b.Type), b.CriteresActifs.Count(), b.TracesActives.Count(), Mode(b.Campagne.Mode))).ToList());
    }

    public static BancVue Banc(ProjetCharge p, Banc b)
    {
        var fusion = b.Fusion(p.Etude.Schema);
        var conn = Connectivity.Build(fusion);
        var reglages = new Dictionary<string, object>();
        if (b.Type == AnalyseKind.Transitoire)
        {
            reglages["duree_s"] = b.Transitoire.Duree;
            reglages["pas_automatique"] = b.Transitoire.Auto;
            if (!b.Transitoire.Auto) reglages["pas_max_s"] = b.Transitoire.Pas;
        }
        else if (b.Type == AnalyseKind.Ac)
        {
            reglages["f_debut_hz"] = b.Ac.FDebut; reglages["f_fin_hz"] = b.Ac.FFin;
            reglages["echantillonnage_automatique"] = b.Ac.Auto;
            if (!b.Ac.Auto) { reglages["balayage"] = b.Ac.Lineaire ? "linéaire" : "décade"; reglages[b.Ac.Lineaire ? "points" : "points_par_decade"] = b.Ac.PointsParDecade; }
        }
        bool ac = b.Type == AnalyseKind.Ac;
        return new(p.Etude.Bancs.IndexOf(b) + 1, b.Nom, Analyse(b.Type), reglages, Composants(b.Calque, conn),
            b.CriteresActifs.Select(c => new CritereVue(c.Mesure.ToString(), c.Info.Nom, Signaux.Nom(c.Noeud, fusion), c.Min, c.Max, UniteCritere(c))).ToList(),
            b.TracesActives.Select(g => new GabaritVue(g.Nom, Signaux.Nom(g.Noeud, fusion), g.Limite == Limite.Haute ? "haute" : "basse",
                g.Points.Select(pt => new[] { pt.X, pt.Y }).ToList(), ac ? "Hz" : "s", g.Unite, g.Relatif)).ToList(),
            b.Graphiques.Select(g => (IReadOnlyList<string>)g.Select(n => Signaux.Nom(n, fusion)).ToList()).ToList(),
            Campagne(b.Campagne, fusion));
    }

    static string UniteCritere(CritereGabarit c) => Signaux.EstCourant(c.Noeud) && c.Info.Unite == "V" ? "A" : c.Info.Unite;

    public static CampagneVue Campagne(Campagne c, Schematic fusion) => c.Mode switch
    {
        ModeCampagne.Balayage => c.Echelle == EchelleBalayage.Liste
            ? new("balayage", Cible: c.Cible, Echelle: "liste", Liste: c.Liste)
            : new("balayage", Cible: c.Cible, Debut: c.Debut, Fin: c.Fin, Points: c.Points, Echelle: c.Echelle == EchelleBalayage.Logarithmique ? "log" : "lineaire"),
        ModeCampagne.MonteCarlo => new("monte_carlo", Tirages: c.Tirages, Loi: c.Loi == LoiTirage.Uniforme ? "uniforme" : "gauss", Graine: c.Graine,
            ComposantsDisperses: Disperses(fusion)),
        ModeCampagne.Coins => new("coins", Temperature: c.CoinsTemperature, TMin: c.CoinsTemperature ? c.TMin : null, TMax: c.CoinsTemperature ? c.TMax : null,
            ComposantsDisperses: Disperses(fusion)),
        _ => new("aucune"),
    };

    static IReadOnlyList<string> Disperses(Schematic fusion) =>
        fusion.Components.Where(k => Campagnes.Tolerance(k) != null).OrderBy(k => k.Ref, StringComparer.Ordinal)
            .Select(k => $"{k.Ref} = {k.Value} ± {Campagnes.Tolerance(k):0.##} %").ToList();

    public static ResultatCritereVue Critere(CritereGabarit c, ResultatGabarit r, Schematic? fusion)
    {
        double? marge = r.Valeur is not { } v ? null
            : Math.Min(c.Min is { } a ? v - a : double.PositiveInfinity, c.Max is { } z ? z - v : double.PositiveInfinity) is var m && double.IsFinite(m) ? m : null;
        return new(r.Nom, Signaux.Nom(c.Noeud, fusion), Verdict_(r.Verdict), r.Valeur, r.Valeur is { } x ? SpiceNumber.Format(x, r.Unite) : null,
            c.Min, c.Max, r.Unite, marge, r.Detail);
    }

    public static ResultatGabaritVue Gabarit(ResultatGabaritGraphique r) => new(r.Nom, Verdict_(r.Verdict), r.Marge, r.XPire, r.YPire, r.Detail);

    public static SimulationVue Simulation(ProjetCharge p, Banc b, ResultatEtude r, bool journal)
    {
        var fusion = b.Fusion(p.Etude.Schema);
        string verdict = r.Erreur != null ? "ERREUR" : r.Nombre == 0 ? "SANS_CONTRAINTE" : Verdict_(r.Global);
        var j = r.Journal.Trim();
        // Journal ngspice : renvoyé en cas d'erreur (ou sur demande), tronqué pour ne pas noyer le client.
        string? texte = (journal || r.Erreur != null) && j.Length > 0 ? (j.Length > 4000 ? "…" + j[^4000..] : j) : null;
        return new(b.Nom, Analyse(b.Type), verdict, r.Respectes, r.Nombre,
            r.Criteres.Select(c => Critere(c.Critere, c.Resultat, fusion)).ToList(),
            r.Traces.Select(t => Gabarit(t.Resultat)).ToList(),
            r.Erc.Select(i => $"{(i.Severity == Severity.Error ? "Erreur" : "Avertissement")} : {i.Message}").ToList(),
            r.Erreur, texte);
    }

    public static CampagneResultatVue CampagneResultat(ProjetCharge p, Banc b, ResultatCampagne r)
    {
        var fusion = b.Fusion(p.Etude.Schema);
        return new(b.Nom, Mode(r.Mode), r.Tirages.Count, r.Conformes, Math.Round(r.Taux, 4), r.Tirages.Count(t => t.Erreur != null),
            r.Stats.Select(s => new StatVue(s.Critere.Info.Nom, Signaux.Nom(s.Critere.Noeud, fusion), UniteCritere(s.Critere), s.N, Fini(s.Moyenne), Fini(s.EcartType),
                Fini(s.Min), Fini(s.Max), s.Cpk is { } k ? Math.Round(k, 3) : null, s.Echecs)).ToList(),
            r.PireCas is { } pc ? Tirage(pc, fusion) : null, r.Avertissement);
    }

    static double? Fini(double v) => double.IsFinite(v) ? v : null;

    public static TirageVue Tirage(ResultatTirage t, Schematic fusion) =>
        new(t.Tirage.Index + 1, t.Tirage.Libelle, t.Erreur != null ? "ERREUR" : Verdict_(t.Global), t.Echecs, Math.Round(t.MargeRelative, 4),
            new Dictionary<string, double>(t.Tirage.Valeurs), t.Tirage.Temperature,
            t.Criteres.Select(kv => Critere(kv.Key, kv.Value, fusion)).ToList(),
            t.Traces.Select(kv => Gabarit(kv.Value)).ToList(), t.Erreur);

    public static ResumeTestsVue ResumeTests(ProjetCharge p)
    {
        var bancs = p.Etude.Bancs.Select(b =>
        {
            var campagne = p.Campagnes.TryGetValue(b, out var c)
                ? $"{Mode(c.Mode)} : {c.Conformes}/{c.Tirages.Count} conformes ({c.Taux:P1})" + (c.PireCas is { } pc ? $", pire cas « {pc.Tirage.Libelle} »" : "")
                : null;
            if (!p.Simulations.TryGetValue(b, out var r))
                return new ResumeBancVue(b.Nom, Analyse(b.Type), "non simulé", null, null, null, [], null, campagne);
            var fusion = b.Fusion(p.Etude.Schema);
            var echecs = r.Criteres.Where(x => x.Resultat.Verdict != Verdict.Pass)
                .Select(x => $"{Verdict_(x.Resultat.Verdict)} {x.Resultat.Nom} sur {Signaux.Nom(x.Critere.Noeud, fusion)}" + (x.Resultat.Valeur is { } v ? $" = {SpiceNumber.Format(v, x.Resultat.Unite)}" : "") + (x.Resultat.Detail is { } d ? $" ({d})" : ""))
                .Concat(r.Traces.Where(x => x.Resultat.Verdict != Verdict.Pass).Select(x => $"{Verdict_(x.Resultat.Verdict)} {x.Resultat.Nom}" + (x.Resultat.Detail is { } d ? $" ({d})" : "")))
                .ToList();
            string verdict = r.Erreur != null ? "ERREUR" : r.Nombre == 0 ? "SANS_CONTRAINTE" : Verdict_(r.Global);
            return new ResumeBancVue(b.Nom, Analyse(b.Type), "simulé", verdict, r.Respectes, r.Nombre, echecs, r.Erreur, campagne);
        }).ToList();
        var vs = bancs.Select(b => b.Verdict).ToList();
        string global = vs.Count == 0 ? "SANS_BANC" : vs.Any(v => v is "FAIL" or "ERREUR") ? "FAIL"
            : vs.Contains(null) ? "INCOMPLET" : vs.All(v => v is "PASS" or "SANS_CONTRAINTE") ? "PASS" : "NON_EVALUABLE";
        return new(p.Nom, global, bancs);
    }
}
