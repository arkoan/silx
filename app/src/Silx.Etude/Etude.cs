namespace Silx.Etude;

public enum AnalyseKind { Transitoire, Ac }

public sealed class ReglagesTransitoire { public double Pas { get; set; } = 10e-6; public double Duree { get; set; } = 10e-3; }
public sealed class ReglagesAc { public double FDebut { get; set; } = 1; public double FFin { get; set; } = 1e6; public int PointsParDecade { get; set; } = 50; }

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
    /// <summary>Source qui fixe le type du banc (null s'il n'y en a pas).</summary>
    public PlacedComponent? PremierStimulus => Calque.Components.FirstOrDefault(k => k.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource);
    public IEnumerable<CritereGabarit> CriteresActifs => Gabarits.Where(g => g.Info.Analyse == Type);
    public IEnumerable<GabaritTrace> TracesActives => GabaritsTraces.Where(g => g.Analyse == Type);
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
        s.Components.Add(new() { Ref = "C1", Kind = ComponentKind.Capacitor, Position = new(12, 8), Rotation = 1, Value = "1u" });
        s.Wires.Add(new(new(8, 8), new(12, 8)));
        s.Wires.Add(new(new(12, 12), new(4, 12)));
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(8, 12) });
        s.Components.Add(new() { Ref = "NET1", Kind = ComponentKind.Label, Position = new(4, 8), Value = "IN" });
        s.Components.Add(new() { Ref = "NET2", Kind = ComponentKind.Label, Position = new(12, 8), Value = "OUT" });
        var e = new Etude { Schema = s };

        var echelon = new Banc { Nom = "Échelon 1 V" };
        echelon.Calque.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(4, 8), Rotation = 1, Value = "PULSE(0 1 0.5m 1u 1u 20m 40m)" });
        echelon.Gabarits.Add(new() { Mesure = TypeMesure.TempsDeMontee, Noeud = "OUT", Max = 2.5e-3 });
        echelon.Gabarits.Add(new() { Mesure = TypeMesure.Depassement, Noeud = "OUT", Max = 5 });
        echelon.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Transitoire, Noeud = "OUT", Limite = Silx.Mesures.Gabarits.Limite.Haute, Points = [(0, 1.1), (10e-3, 1.1)] });
        echelon.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Transitoire, Noeud = "OUT", Limite = Silx.Mesures.Gabarits.Limite.Basse, Points = [(2e-3, 0), (3e-3, 0.8), (6e-3, 0.95), (10e-3, 0.95)] });
        e.Bancs.Add(echelon);

        var bode = new Banc { Nom = "Réponse en fréquence", Type = AnalyseKind.Ac };
        bode.Calque.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(4, 8), Rotation = 1, Value = "AC 1" });
        bode.Calque.Components.Add(new() { Ref = "RL", Kind = ComponentKind.Resistor, Position = new(16, 8), Rotation = 1, Value = "100k" });
        bode.Calque.Wires.Add(new(new(12, 8), new(16, 8)));
        bode.Calque.Wires.Add(new(new(16, 12), new(12, 12)));
        bode.Gabarits.Add(new() { Mesure = TypeMesure.BandePassante3dB, Noeud = "OUT", Min = 150 });
        bode.GabaritsTraces.Add(new() { Analyse = AnalyseKind.Ac, Noeud = "OUT", Limite = Silx.Mesures.Gabarits.Limite.Haute, Points = [(1e3, -12), (1e5, -35), (1e6, -55)] });
        e.Bancs.Add(bode);
        return e;
    }
}
