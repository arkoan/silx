namespace Silx.Etude;

public enum AnalyseKind { Transitoire, Ac }

public sealed class ReglagesTransitoire
{
    public double Pas { get; set; } = 10e-6; public double Duree { get; set; } = 10e-3;
    /// <summary>
    /// Pas automatique (défaut) : ngspice choisit son pas (fin aux fronts des stimuli, où il pose des points d'arrêt,
    /// large ailleurs) et tous ses points sont gardés, sans rééchantillonnage. Sinon le pas maximal est <see cref="Pas"/>.
    /// </summary>
    public bool Auto { get; set; } = true;
}
public sealed class ReglagesAc
{
    public double FDebut { get; set; } = 1; public double FFin { get; set; } = 1e6; public int PointsParDecade { get; set; } = 50;
    /// <summary>Balayage linéaire (bande étroite, ex. résonance d'un quartz) : <see cref="PointsParDecade"/> est alors le nombre total de points.</summary>
    public bool Lineaire { get; set; }
    /// <summary>
    /// Échantillonnage automatique (défaut) : balayage grossier sur la plage, puis sous-balayages fins là où le gain ou la
    /// phase varient vite ou dévient de leur tendance (résonances, coupures). Sinon : réglage manuel (décade ou linéaire).
    /// </summary>
    public bool Auto { get; set; } = true;
}

public enum TypeMesure { Depassement, TempsDeMontee, TempsEtablissement, ValeurFinale, BandePassante3dB, GainDc, MargeDePhase, MargeDeGain }

public static class TypesMesure
{
    public sealed record Info(TypeMesure Type, string Nom, string Unite, AnalyseKind Analyse);

    public static IReadOnlyList<Info> Tous { get; } =
    [
        new(TypeMesure.Depassement, "Dépassement", "%", AnalyseKind.Transitoire),
        new(TypeMesure.TempsDeMontee, "Temps de montée 10–90 %", "s", AnalyseKind.Transitoire),
        new(TypeMesure.TempsEtablissement, "Temps d'établissement 2 %", "s", AnalyseKind.Transitoire),
        new(TypeMesure.ValeurFinale, "Valeur finale", "V", AnalyseKind.Transitoire),
        new(TypeMesure.BandePassante3dB, "Bande passante −3 dB", "Hz", AnalyseKind.Ac),
        new(TypeMesure.GainDc, "Gain basse fréquence", "dB", AnalyseKind.Ac),
        new(TypeMesure.MargeDePhase, "Marge de phase (gain de boucle)", "°", AnalyseKind.Ac),
        new(TypeMesure.MargeDeGain, "Marge de gain (gain de boucle)", "dB", AnalyseKind.Ac),
    ];

    public static Info Get(TypeMesure t) => Tous.First(x => x.Type == t);
}

/// <summary>Un critère de gabarit tel que l'utilisateur le saisit : mesure + nœud observé + bornes.</summary>
public sealed class CritereGabarit
{
    public TypeMesure Mesure { get; set; }
    public string Noeud { get; set; } = "OUT";
    public double? Min { get; set; }
    public double? Max { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public TypesMesure.Info Info => TypesMesure.Get(Mesure);
}

/// <summary>Gabarit dessiné sur une courbe : x en secondes (transitoire) ou en Hz (AC) ; y en V ou en dB.</summary>
public sealed class GabaritTrace
{
    public AnalyseKind Analyse { get; set; }
    public string Noeud { get; set; } = "OUT";
    public Silx.Mesures.Gabarits.Limite Limite { get; set; }
    public List<(double X, double Y)> Points { get; set; } = new();
    /// <summary>
    /// Gabarit temporel accroché au front du stimulus : quand le retard du front change, les points suivent.
    /// Les points restent en temps absolu ; <see cref="Ancrage"/> est l'instant du front pour lequel ils ont été posés.
    /// </summary>
    public bool Relatif { get; set; }
    public double Ancrage { get; set; }
    public string Nom => $"Limite {(Limite == Silx.Mesures.Gabarits.Limite.Haute ? "haute" : "basse")} sur {(Analyse == AnalyseKind.Ac ? "|V(" + Noeud + ")| dB" : "V(" + Noeud + ")")}";
    public string Unite => Analyse == AnalyseKind.Ac ? "dB" : "V";
}

/// <summary>
/// Banc de test : calque posé sur le circuit sous test, comme les appareils d'un labo branchés sur une carte.
/// Il porte les sources (stimuli, alimentations), charges et sondes, les réglages d'analyse et les gabarits.
/// Ses éléments se connectent au circuit par coïncidence de broches ou par étiquette de nœud de même nom.
/// </summary>
public sealed class Banc
{
    public string Nom { get; set; } = "Banc";
    /// <summary>Nature du banc : temporel (.tran) ou fréquentiel (.ac). Seules les contraintes de ce type sont évaluées.</summary>
    public AnalyseKind Type
    {
        // Le premier stimulus posé décide : petit signal (AC) → fréquentiel, sinon temporel. Sans source : valeur choisie.
        get => Calque.Components.FirstOrDefault(k => k.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource) is { } src
            ? Stimulus.Lire(src.Value).Analyse : _type;
        set => _type = value;
    }
    AnalyseKind _type = AnalyseKind.Transitoire;
    /// <summary>Type retenu quand le banc n'a encore aucun stimulus (enregistré dans le projet).</summary>
    public AnalyseKind TypeParDefaut { get => _type; set => _type = value; }
    /// <summary>Couleur propre du banc (« #rrggbb ») ; null = couleur des bancs choisie dans les paramètres.</summary>
    public string? Couleur { get; set; }
    /// <summary>Campagne lancée avec « Simuler » : balayage, Monte-Carlo ou coins (Aucune = valeur nominale seule).</summary>
    public Campagne Campagne { get; set; } = new();
    /// <summary>Source qui fixe le type du banc (null s'il n'y en a pas).</summary>
    public PlacedComponent? PremierStimulus => Calque.Components.FirstOrDefault(k => k.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource);
    public IEnumerable<CritereGabarit> CriteresActifs => Gabarits.Where(g => g.Info.Analyse == Type);
    public IEnumerable<GabaritTrace> TracesActives => GabaritsTraces.Where(g => g.Analyse == Type);

    /// <summary>
    /// Instant du front du stimulus : retard (td) de la première source en échelon / impulsion du banc, sinon d'un sinus ;
    /// 0 sans stimulus temporel.
    /// </summary>
    public double InstantFront
    {
        get
        {
            var sources = Calque.Components.Where(k => k.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource).Select(k => Stimulus.Lire(k.Value)).ToList();
            var s = sources.FirstOrDefault(x => x.Forme == FormeStimulus.Impulsion) ?? sources.FirstOrDefault(x => x.Forme == FormeStimulus.Sinus);
            return s == null ? 0 : s["td"];
        }
    }

    /// <summary>Décale les gabarits relatifs au front si le front a bougé depuis leur pose. Renvoie vrai si un gabarit a bougé.</summary>
    public bool RecalerGabarits()
    {
        double t0 = InstantFront; bool bouge = false;
        foreach (var g in GabaritsTraces.Where(g => g.Relatif && g.Analyse == AnalyseKind.Transitoire))
        {
            double d = t0 - g.Ancrage;
            if (Math.Abs(d) < 1e-15) continue;
            for (int i = 0; i < g.Points.Count; i++) g.Points[i] = (g.Points[i].X + d, g.Points[i].Y);
            g.Ancrage = t0; bouge = true;
        }
        return bouge;
    }
    public Schematic Calque { get; set; } = new();
    public ReglagesTransitoire Transitoire { get; } = new();
    public ReglagesAc Ac { get; } = new();
    public List<CritereGabarit> Gabarits { get; } = new();
    public List<GabaritTrace> GabaritsTraces { get; } = new();
    /// <summary>Graphiques empilés : pour chacun, les nœuds sondés (tension par rapport à la masse).</summary>
    public List<List<string>> Graphiques { get; } = new();

    /// <summary>Schéma simulé : circuit sous test + éléments du banc.</summary>
    public Schematic Fusion(Schematic circuit) => new()
    {
        Components = circuit.Components.Select(c => c.Clone()).Concat(Calque.Components.Select(c => c.Clone())).ToList(),
        Wires = circuit.Wires.Concat(Calque.Wires).ToList(),
    };
}

/// <summary>Une étude = le circuit sous test (sans stimuli) + ses bancs de test.</summary>
public sealed class Etude
{
    public Schematic Schema { get; set; } = new();
    public List<Banc> Bancs { get; } = new();

    /// <summary>Filtre RC passe-bas de démonstration avec deux bancs : échelon et réponse en fréquence.</summary>
    public static Etude Demo()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(4, 8), Value = "1k" });
        s.Components.Add(new() { Ref = "C1", Kind = ComponentKind.Capacitor, Position = new(11, 8), Rotation = 1, Value = "1u" });
        s.Wires.Add(new(new(7, 8), new(11, 8)));
        s.Wires.Add(new(new(11, 11), new(4, 11)));
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(7, 11) });
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(4, 8), Value = "IN" });
        s.Components.Add(new() { Ref = "NET2", Kind = ComponentKind.Label, Position = new(11, 8), Value = "OUT" });
        var e = new Etude { Schema = s };

        var echelon = new Banc { Nom = "Échelon 1 V" };
        echelon.Calque.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(4, 8), Rotation = 1, Value = "PULSE(0 1 0.5m 1u 1u 20m 40m)" });
        echelon.Gabarits.Add(new() { Mesure = TypeMesure.TempsDeMontee, Noeud = "OUT", Max = 2.5e-3 });
        echelon.Gabarits.Add(new() { Mesure = TypeMesure.Depassement, Noeud = "OUT", Max = 5 });
        echelon.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Transitoire, Noeud = "OUT", Limite = Silx.Mesures.Gabarits.Limite.Haute, Points = [(0, 1.1), (10e-3, 1.1)] });   // absolu : plafond sur toute la durée
        echelon.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Transitoire, Noeud = "OUT", Limite = Silx.Mesures.Gabarits.Limite.Basse, Points = [(2e-3, 0), (3e-3, 0.8), (6e-3, 0.95), (10e-3, 0.95)], Relatif = true, Ancrage = 0.5e-3 });   // suit le front de V1
        e.Bancs.Add(echelon);

        var bode = new Banc { Nom = "Réponse en fréquence", Type = AnalyseKind.Ac };
        bode.Calque.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(4, 8), Rotation = 1, Value = "AC 1" });
        bode.Calque.Components.Add(new() { Ref = "RL", Kind = ComponentKind.Resistor, Position = new(15, 8), Rotation = 1, Value = "100k" });
        bode.Calque.Wires.Add(new(new(11, 8), new(15, 8)));
        bode.Calque.Wires.Add(new(new(15, 11), new(11, 11)));
        bode.Gabarits.Add(new() { Mesure = TypeMesure.BandePassante3dB, Noeud = "OUT", Min = 150 });
        bode.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Ac, Noeud = "OUT", Limite = Silx.Mesures.Gabarits.Limite.Haute, Points = [(1e3, -12), (1e5, -35), (1e6, -55)] });
        e.Bancs.Add(bode);
        return e;
    }
}
