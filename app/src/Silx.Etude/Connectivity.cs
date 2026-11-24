namespace Silx.Etude;

/// <summary>Résolution des nœuds électriques à partir de la géométrie (union-find sur points de grille).</summary>
public sealed class Connectivity
{
    readonly Dictionary<GridPoint, GridPoint> _parent = new();
    GridPoint Find(GridPoint p)
    {
        if (!_parent.TryGetValue(p, out var par)) { _parent[p] = p; return p; }
        if (par.Equals(p)) return p;
        var r = Find(par); _parent[p] = r; return r;
    }
    void Union(GridPoint a, GridPoint b) { var ra = Find(a); var rb = Find(b); if (!ra.Equals(rb)) _parent[ra] = rb; }

    /// <summary>Nom de nœud SPICE par point de grille connecté. "0" pour la masse.</summary>
    public Dictionary<GridPoint, string> NodeOf { get; } = new();
    /// <summary>Nombre de broches de composants électriques rattachées à chaque nœud, pour détecter les nœuds flottants.</summary>
    public Dictionary<string, int> PinCount { get; } = new();
    /// <summary>Noms de nœuds donnés par l'utilisateur (étiquettes), dans l'ordre alphabétique.</summary>
    public IReadOnlyList<string> NamedNodes { get; private set; } = [];
    /// <summary>Tous les nœuds hors masse : étiquettes d'abord, puis N001…</summary>
    public IReadOnlyList<string> AllNodes { get; private set; } = [];

    public static Connectivity Build(Schematic s)
    {
        var c = new Connectivity();
        foreach (var w in s.Wires) c.Union(w.A, w.B);

        // HYPOTHÈSE : une extrémité de fil ou une broche posée sur l'intérieur d'un autre fil y est connectée (jonction en T).
        var pts = s.Wires.SelectMany(w => new[] { w.A, w.B }).Concat(s.Components.SelectMany(k => k.Pins)).Distinct().ToList();
        foreach (var p in pts)
            foreach (var w in s.Wires)
                if (OnSegment(p, w)) c.Union(p, w.A);
        foreach (var p in pts) c.Find(p);

        // Tous les GND sont le même nœud "0" (HYPOTHÈSE : pas de masses distinctes en v1).
        var allGround = s.Components.Where(k => k.Kind == ComponentKind.Ground).SelectMany(k => k.Pins).ToList();
        for (int i = 1; i < allGround.Count; i++) c.Union(allGround[i], allGround[0]);

        // Deux étiquettes de même nom relient leurs nœuds, même sans fil.
        var labels = s.Components.Where(k => k.Kind == ComponentKind.Label && !string.IsNullOrWhiteSpace(k.Value)).ToList();
        foreach (var g in labels.GroupBy(l => l.Value.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var first = g.First().Pins[0];
            foreach (var l in g.Skip(1)) c.Union(l.Pins[0], first);
        }

        var names = new Dictionary<GridPoint, string>();
        if (allGround.Count > 0) names[c.Find(allGround[0])] = "0";
        foreach (var l in labels.OrderBy(l => l.Value, StringComparer.OrdinalIgnoreCase))
            names.TryAdd(c.Find(l.Pins[0]), l.Value.Trim());
        int n = 1;
        foreach (var comp in s.Components.Where(k => k.IsElectrical).OrderBy(k => k.Ref, StringComparer.Ordinal))
            foreach (var p in comp.Pins)
            {
                var root = c.Find(p);
                if (!names.ContainsKey(root)) names[root] = $"N{n++:000}";
            }

        foreach (var p in c._parent.Keys.ToList())
            if (names.TryGetValue(c.Find(p), out var nm)) c.NodeOf[p] = nm;
        foreach (var comp in s.Components.Where(k => k.IsElectrical))
            foreach (var p in comp.Pins)
            { var nm = c.NodeOf[p]; c.PinCount[nm] = c.PinCount.GetValueOrDefault(nm) + 1; }

        var used = c.PinCount.Keys.Where(k => k != "0").ToHashSet();
        c.NamedNodes = labels.Select(l => l.Value.Trim()).Where(used.Contains).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        c.AllNodes = c.NamedNodes.Concat(used.Except(c.NamedNodes).OrderBy(x => x, StringComparer.Ordinal)).ToList();
        return c;
    }

    static bool OnSegment(GridPoint p, Wire w)
    {
        if (p.Equals(w.A) || p.Equals(w.B)) return false;
        long cross = (long)(w.B.X - w.A.X) * (p.Y - w.A.Y) - (long)(w.B.Y - w.A.Y) * (p.X - w.A.X);
        if (cross != 0) return false;
        return p.X >= Math.Min(w.A.X, w.B.X) && p.X <= Math.Max(w.A.X, w.B.X)
            && p.Y >= Math.Min(w.A.Y, w.B.Y) && p.Y <= Math.Max(w.A.Y, w.B.Y);
    }
}
