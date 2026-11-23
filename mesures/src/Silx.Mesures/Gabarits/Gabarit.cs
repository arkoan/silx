namespace Silx.Mesures.Gabarits;

/// <summary>Une mesure typée sur un type d'entrée (courbe temporelle, réponse fréquentielle…). Liste ouverte : implémenter pour étendre.</summary>
public interface IMesure<in TEntree>
{
    string Nom { get; }
    string Unite { get; }
    double Mesurer(TEntree entree);
}

public sealed class Mesure<TEntree>(string nom, string unite, Func<TEntree, double> calcul) : IMesure<TEntree>
{
    public string Nom { get; } = nom;
    public string Unite { get; } = unite;
    public double Mesurer(TEntree entree) => calcul(entree);
}

/// <summary>Bornes inclusives ; null = non borné de ce côté.</summary>
public readonly record struct Bornes(double? Min, double? Max)
{
    public static Bornes AuMoins(double min) => new(min, null);
    public static Bornes AuPlus(double max) => new(null, max);
    public static Bornes Entre(double min, double max) => new(min, max);

    public bool Contient(double v) => (Min is null || v >= Min) && (Max is null || v <= Max);

    public override string ToString() => (Min, Max) switch
    {
        (double a, double b) => $"[{a}; {b}]",
        (double a, null) => $">= {a}",
        (null, double b) => $"<= {b}",
        _ => "non borné",
    };
}

public enum Verdict { Pass, Fail, NonEvaluable }

public sealed record ResultatGabarit(string Nom, Verdict Verdict, double? Valeur, string Unite, Bornes Bornes, string? Detail);

/// <summary>Critère quantifié : une mesure + des bornes. L'évaluation est déterministe.</summary>
public sealed class Gabarit<TEntree>
{
    public Gabarit(string nom, IMesure<TEntree> mesure, Bornes bornes)
    {
        if (bornes.Min is null && bornes.Max is null) throw new ArgumentException("Gabarit sans borne.");
        if (bornes.Min > bornes.Max) throw new ArgumentException("Min > Max.");
        if (bornes.Min is { } a && !double.IsFinite(a) || bornes.Max is { } b && !double.IsFinite(b))
            throw new ArgumentException("Borne non finie.");
        Nom = nom; Mesure = mesure; Bornes = bornes;
    }

    public string Nom { get; }
    public IMesure<TEntree> Mesure { get; }
    public Bornes Bornes { get; }

    /// <summary>Une mesure impossible ou NaN donne NonEvaluable (jamais Pass). Les entrées invalides lèvent.</summary>
    public ResultatGabarit Evaluer(TEntree entree)
    {
        double v;
        try { v = Mesure.Mesurer(entree); }
        catch (MesureImpossibleException e)
        {
            return new(Nom, Verdict.NonEvaluable, null, Mesure.Unite, Bornes, e.Message);
        }
        if (double.IsNaN(v))
            return new(Nom, Verdict.NonEvaluable, null, Mesure.Unite, Bornes, "Mesure NaN.");
        return new(Nom, Bornes.Contient(v) ? Verdict.Pass : Verdict.Fail, v, Mesure.Unite, Bornes, null);
    }
}

/// <summary>Ensemble de gabarits : pass global seulement si tous passent.</summary>
public static class Gabarits
{
    public static IReadOnlyList<ResultatGabarit> Evaluer<T>(IEnumerable<Gabarit<T>> gabarits, T entree)
        => gabarits.Select(g => g.Evaluer(entree)).ToList();

    public static Verdict Global(IEnumerable<ResultatGabarit> r)
    {
        var l = r.ToList();
        if (l.Any(x => x.Verdict == Verdict.Fail)) return Verdict.Fail;
        if (l.Count == 0 || l.Any(x => x.Verdict == Verdict.NonEvaluable)) return Verdict.NonEvaluable;
        return Verdict.Pass;
    }
}
