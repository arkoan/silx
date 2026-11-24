namespace Silx.Etude;

/// <summary>
/// Fils élastiques : quand des broches bougent, les fils qui y sont attachés suivent en restant horizontaux ou verticaux.
/// Fonction pure (voir docs/specs/fils_elastiques_composants_spice.md §1).
/// </summary>
public static class Elastique
{
    /// <param name="fils">Fils avant déplacement.</param>
    /// <param name="deplacements">Ancienne position → nouvelle position de chaque broche qui bouge.</param>
    /// <param name="pointsFixes">Broches qui ne bougent pas (composants restés en place) : un coin posé dessus ne glisse pas.</param>
    public static List<Wire> Suivre(IEnumerable<Wire> fils, IReadOnlyDictionary<GridPoint, GridPoint> deplacements, IReadOnlySet<GridPoint>? pointsFixes = null, long coudeCoteBroche = 0)
        => Calculer(fils, deplacements, pointsFixes, coudeCoteBroche).Fils;

    /// <param name="coudeCoteBroche">Masque : bit k à 1 = le k-ième fil attaché fait son coude du côté de la broche.</param>
    static (List<Wire> Fils, int Attaches) Calculer(IEnumerable<Wire> fils, IReadOnlyDictionary<GridPoint, GridPoint> deplacements, IReadOnlySet<GridPoint>? pointsFixes, long coudeCoteBroche)
    {
        pointsFixes ??= new HashSet<GridPoint>();
        var moved = deplacements.Where(kv => !kv.Key.Equals(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var list = Orthogonaliser(fils).ToList();
        if (moved.Count == 0) return (Normaliser(list, pointsFixes.Concat(deplacements.Values).ToHashSet()), 0);

        // 1. Découpe des T : un fil qui passe par une broche déplacée est coupé à cet endroit.
        // Une broche fixe au milieu d'un fil coupe aussi ce fil, pour qu'elle reste reliée si le fil se décale.
        foreach (var p in moved.Keys.Concat(pointsFixes))
            for (int i = 0; i < list.Count; i++)
                if (Interieur(p, list[i])) { var w = list[i]; list[i] = new(w.A, p); list.Insert(i + 1, new(p, w.B)); i++; }

        // 2. Fermeture : un groupe de fils reliés entre eux, sans broche fixe, dont toutes les broches entraînées le sont
        //    du même vecteur, est translaté en bloc (fil en U sous un composant, liaison entre deux composants déplacés ensemble).
        var groupe = Enumerable.Range(0, list.Count).ToArray();
        int G(int i) => groupe[i] == i ? i : groupe[i] = G(groupe[i]);
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                if (Touche(list[i], list[j])) groupe[G(i)] = G(j);
        foreach (var g in Enumerable.Range(0, list.Count).GroupBy(G))
        {
            var ws = g.Select(i => list[i]).ToList();
            var pts = ws.SelectMany(w => new[] { w.A, w.B }).Distinct().ToList();
            if (pointsFixes.Any(f => pts.Contains(f) || ws.Any(w => Interieur(f, w)))) continue;
            var deltas = pts.Where(moved.ContainsKey).Select(o => (moved[o].X - o.X, moved[o].Y - o.Y)).Distinct().ToList();
            if (deltas.Count != 1) continue;
            foreach (var q in pts) moved.TryAdd(q, new(q.X + deltas[0].Item1, q.Y + deltas[0].Item2));
        }

        var a = list.Select(w => w.A).ToArray();
        var b = list.Select(w => w.B).ToArray();
        var extra = new List<Wire>();
        int k = 0;

        // Degré de chaque point (extrémités de fils) dans l'état d'origine.
        var degre = list.SelectMany(w => new[] { w.A, w.B }).GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());

        for (int i = 0; i < list.Count; i++)
        {
            bool ma = moved.ContainsKey(list[i].A), mb = moved.ContainsKey(list[i].B);
            if (ma && mb) { a[i] = moved[list[i].A]; b[i] = moved[list[i].B]; continue; }
            if (!ma && !mb) continue;
            var p = ma ? list[i].A : list[i].B; var q = ma ? list[i].B : list[i].A; var np = moved[p];
            bool horizontal = p.Y == q.Y && p.X != q.X;
            var dPerp = horizontal ? new GridPoint(0, np.Y - p.Y) : new GridPoint(np.X - p.X, 0);
            bool variante = (dPerp.X != 0 || dPerp.Y != 0) && ((coudeCoteBroche >> Math.Min(k, 62)) & 1) == 1;
            if (dPerp.X != 0 || dPerp.Y != 0) k++;
            if (variante)
            {
                // Variante : le fil reste sur sa ligne, le coude est fait du côté de la broche qui bouge.
                var pAxe = new GridPoint(np.X - dPerp.X, np.Y - dPerp.Y);
                if (ma) a[i] = pAxe; else b[i] = pAxe;
                extra.Add(new(pAxe, np));
                continue;
            }
            var nq = new GridPoint(q.X + dPerp.X, q.Y + dPerp.Y);
            if (ma) { a[i] = np; b[i] = nq; } else { b[i] = np; a[i] = nq; }
            if (nq.Equals(q)) continue;

            // 3. Le coin Q glisse le long du fil voisin s'il est seul, perpendiculaire, et que Q n'est ni broche ni jonction.
            int voisin = -1;
            if (degre.GetValueOrDefault(q) == 2 && !pointsFixes.Contains(q) && !list.Any(w => Interieur(q, w)))
                for (int j = 0; j < list.Count; j++)
                    if (j != i && (list[j].A.Equals(q) || list[j].B.Equals(q))) voisin = j;
            if (voisin >= 0)
            {
                var w = list[voisin];
                bool perp = (w.A.Y == w.B.Y) != horizontal;
                bool libre = !moved.ContainsKey(w.A) && !moved.ContainsKey(w.B);
                if (perp && libre)
                {
                    if (w.A.Equals(q)) a[voisin] = nq; else b[voisin] = nq;
                    continue;
                }
            }
            extra.Add(new(q, nq)); // coude : préserve jonctions et broches fixes
        }

        // Broche fixe posée sur une broche qui part : un fil en L les garde reliées.
        foreach (var (from, to) in moved)
            if (pointsFixes.Contains(from)) extra.AddRange(Orthogonaliser([new Wire(from, to)]));

        var result = list.Select((_, i) => new Wire(a[i], b[i])).Concat(extra).ToList();
        return (Normaliser(Orthogonaliser(result).ToList(), pointsFixes.Concat(moved.Values).ToHashSet()), k);
    }

    /// <summary>Remplace tout segment oblique par un L (horizontal puis vertical).</summary>
    public static IEnumerable<Wire> Orthogonaliser(IEnumerable<Wire> fils)
    {
        foreach (var w in fils)
        {
            if (w.A.X == w.B.X || w.A.Y == w.B.Y) { yield return w; continue; }
            var m = new GridPoint(w.B.X, w.A.Y);
            yield return new(w.A, m); yield return new(m, w.B);
        }
    }

    /// <summary>Supprime longueurs nulles et doublons ; fusionne deux segments alignés consécutifs sans broche ni jonction entre eux.</summary>
    public static List<Wire> Normaliser(List<Wire> fils, IReadOnlySet<GridPoint> broches)
    {
        var l = new List<Wire>();
        var vus = new HashSet<(GridPoint, GridPoint)>();
        foreach (var w in fils)
        {
            if (w.A.Equals(w.B)) continue;
            var key = Cle(w);
            if (vus.Add(key)) l.Add(w);
        }
        bool change = true;
        while (change)
        {
            change = false;
            var degre = l.SelectMany(w => new[] { w.A, w.B }).GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
            foreach (var (p, n) in degre)
            {
                if (n != 2 || broches.Contains(p) || l.Any(w => Interieur(p, w))) continue;
                var two = l.Where(w => w.A.Equals(p) || w.B.Equals(p)).ToList();
                var o1 = two[0].A.Equals(p) ? two[0].B : two[0].A;
                var o2 = two[1].A.Equals(p) ? two[1].B : two[1].A;
                bool aligne = (o1.X == p.X && o2.X == p.X) || (o1.Y == p.Y && o2.Y == p.Y);
                // Ne fusionne que si p est entre o1 et o2 (pas de repli sur soi qui masquerait un recouvrement).
                bool entre = aligne && (Math.Sign(o1.X - p.X) * Math.Sign(o2.X - p.X) <= 0) && (Math.Sign(o1.Y - p.Y) * Math.Sign(o2.Y - p.Y) <= 0);
                if (!entre) continue;
                l.Remove(two[0]); l.Remove(two[1]);
                if (!o1.Equals(o2)) l.Add(new(o1, o2));
                change = true; break;
            }
        }
        // Dédoublonnage final.
        return l.GroupBy(Cle).Select(g => g.First()).Where(w => !w.A.Equals(w.B)).ToList();
    }

    static bool Touche(Wire u, Wire v)
        => u.A.Equals(v.A) || u.A.Equals(v.B) || u.B.Equals(v.A) || u.B.Equals(v.B)
        || Interieur(u.A, v) || Interieur(u.B, v) || Interieur(v.A, u) || Interieur(v.B, u);

    static (GridPoint, GridPoint) Cle(Wire w)
        => (w.A.X, w.A.Y).CompareTo((w.B.X, w.B.Y)) <= 0 ? (w.A, w.B) : (w.B, w.A);

    /// <summary>Vrai si p est strictement à l'intérieur du segment orthogonal w.</summary>
    public static bool Interieur(GridPoint p, Wire w)
        => (w.A.X == w.B.X && p.X == w.A.X && p.Y > Math.Min(w.A.Y, w.B.Y) && p.Y < Math.Max(w.A.Y, w.B.Y))
        || (w.A.Y == w.B.Y && p.Y == w.A.Y && p.X > Math.Min(w.A.X, w.B.X) && p.X < Math.Max(w.A.X, w.B.X));

    /// <summary>
    /// Déplace des composants d'un schéma (par translation) et fait suivre ses fils.
    /// Les fils sélectionnés (dans <paramref name="filsEntraines"/>) sont translatés avec eux. Renvoie faux si les connexions ont changé.
    /// </summary>
    public static bool Deplacer(Schematic s, IReadOnlyCollection<PlacedComponent> composants, GridPoint delta, IReadOnlyCollection<Wire>? filsEntraines = null)
    {
        // Masses et étiquettes posées sur une broche déplacée l'accompagnent.
        var pins0 = composants.SelectMany(c => c.Pins).ToHashSet();
        composants = composants.Concat(s.Components.Where(k => !composants.Contains(k) && k.Kind is ComponentKind.Ground or ComponentKind.Label && pins0.Contains(k.Pins[0]))).ToList();
        var moves = new Dictionary<GridPoint, GridPoint>();
        foreach (var c in composants) foreach (var p in c.Pins) moves[p] = new(p.X + delta.X, p.Y + delta.Y);
        if (filsEntraines != null) foreach (var w in filsEntraines) foreach (var p in new[] { w.A, w.B }) moves[p] = new(p.X + delta.X, p.Y + delta.Y);
        var fixes = s.Components.Except(composants).SelectMany(c => c.Pins).ToHashSet();
        var avant = s.Clone();
        foreach (var c in composants) c.Position = new(c.Position.X + delta.X, c.Position.Y + delta.Y);
        return Appliquer(s, avant, moves, fixes);
    }

    /// <summary>Pivote un composant d'un quart de tour ; ses fils suivent. Renvoie faux si les connexions ont changé.</summary>
    public static bool Pivoter(Schematic s, PlacedComponent c)
    {
        var avant = s.Clone();
        var pins0 = c.Pins.ToList();
        c.Rotation = (c.Rotation + 1) % 4;
        var apres = c.Pins;
        var moves = new Dictionary<GridPoint, GridPoint>();
        for (int i = 0; i < pins0.Count; i++) moves[pins0[i]] = apres[i];
        var fixes = s.Components.Where(k => k != c).SelectMany(k => k.Pins).ToHashSet();
        return Appliquer(s, avant, moves, fixes);
    }

    /// <summary>
    /// Fait suivre un calque (banc de test) quand des broches du circuit sous test bougent : ses fils suivent,
    /// et un élément du calque posé directement sur une broche déplacée y est relié par un fil en L.
    /// </summary>
    public static void SuivreCalque(Schematic calque, IReadOnlyDictionary<GridPoint, GridPoint> deplacements)
    {
        var fixes = calque.Components.SelectMany(c => c.Pins).ToHashSet();
        var w = Suivre(calque.Wires, deplacements, fixes);
        calque.Wires.Clear(); calque.Wires.AddRange(w);
    }

    /// <summary>
    /// Applique les fils élastiques à <paramref name="s"/> (composants déjà déplacés) : variante « coude au coin » d'abord,
    /// puis « coude côté broche » si la première change les connexions. Renvoie faux si les deux les changent.
    /// </summary>
    static bool Appliquer(Schematic s, Schematic avant, Dictionary<GridPoint, GridPoint> moves, HashSet<GridPoint> fixes)
    {
        // Chaque fil décalé peut faire son coude au coin (par défaut) ou du côté de la broche : on garde la première
        // combinaison qui conserve les connexions (au plus 64 essais), sinon la combinaison par défaut.
        var (w0, n) = Calculer(avant.Wires, moves, fixes, 0);
        long essais = Math.Min(1L << Math.Min(n, 62), 64);
        var ordre = new List<long> { 0, (1L << Math.Min(n, 62)) - 1 };
        for (long m = 1; m < essais; m++) ordre.Add(m);
        var orig = Partition(avant);
        foreach (var m in ordre.Distinct())
        {
            var w = m == 0 ? w0 : Calculer(avant.Wires, moves, fixes, m).Fils;
            s.Wires.Clear(); s.Wires.AddRange(w);
            if (orig.SetEquals(Partition(s))) return true;
        }
        s.Wires.Clear(); s.Wires.AddRange(w0);
        return false;
    }

    /// <summary>Partition des broches électriques en nœuds, pour comparer la connectivité avant/après une édition.</summary>
    public static HashSet<string> Partition(Schematic s)
    {
        var c = Connectivity.Build(s);
        return s.Components.SelectMany(k => k.Pins.Select((p, i) => (k, i, p)))
            .GroupBy(x => c.NodeOf.TryGetValue(x.p, out var n) ? n : "?")
            .Select(g => string.Join(",", g.Select(x => $"{x.k.Ref}.{x.i}").OrderBy(x => x, StringComparer.Ordinal)))
            .ToHashSet();
    }

    public static bool MemesConnexions(Schematic avant, Schematic apres) => Partition(avant).SetEquals(Partition(apres));
}
