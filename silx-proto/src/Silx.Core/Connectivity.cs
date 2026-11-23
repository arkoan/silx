namespace Silx.Core;

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
    /// <summary>Nombre d'éléments (broches + extrémités de fil) rattachés à chaque nœud, pour détecter les nœuds flottants.</summary>
    public Dictionary<string, int> PinCount { get; } = new();

    public static Connectivity Build(Schematic s)
    {
        var c = new Connectivity();
        foreach (var w in s.Wires) c.Union(w.A, w.B);

        // HYPOTHÈSE : une extrémité de fil ou une broche posée sur l'intérieur d'un autre fil (segment axe-aligné) y est connectée (jonction en T).
        var pts = s.Wires.SelectMany(w => new[] { w.A, w.B }).Concat(s.Components.SelectMany(k => k.Pins)).Distinct().ToList();
        foreach (var p in pts)
            foreach (var w in s.Wires)
                if (OnSegment(p, w)) c.Union(p, w.A);
        // Broches coïncidant avec une extrémité : même point de grille => déjà même clé.

        foreach (var k in s.Components.Where(k => k.Kind == ComponentKind.Ground))
            foreach (var p in k.Pins) c.Find(p);

        var groundRoot = s.Components.Where(k => k.Kind == ComponentKind.Ground)
            .SelectMany(k => k.Pins).Select(c.Find).Cast<GridPoint?>().FirstOrDefault();
        var allGround = s.Components.Where(k => k.Kind == ComponentKind.Ground).SelectMany(k => k.Pins).ToList();
        // Tous les GND sont le même nœud "0" (HYPOTHÈSE : pas de masses distinctes en v1).
        for (int i = 1; i < allGround.Count; i++) c.Union(allGround[i], allGround[0]);

        var names = new Dictionary<GridPoint, string>();
        int n = 1;
        foreach (var comp in s.Components.Where(k => k.Kind != ComponentKind.Ground).OrderBy(k => k.Ref, StringComparer.Ordinal))
            foreach (var p in comp.Pins)
            {
                var root = c.Find(p);
                if (!names.ContainsKey(root))
                    names[root] = allGround.Count > 0 && c.Find(allGround[0]).Equals(root) ? "0" : $"N{n++:000}";
            }
        if (allGround.Count > 0) names[c.Find(allGround[0])] = "0";

        foreach (var p in c._parent.Keys.ToList())
            if (names.TryGetValue(c.Find(p), out var nm)) c.NodeOf[p] = nm;
        foreach (var comp in s.Components.Where(k => k.Kind != ComponentKind.Ground))
            foreach (var p in comp.Pins)
            { var nm = c.NodeOf[p]; c.PinCount[nm] = c.PinCount.GetValueOrDefault(nm) + 1; }
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
