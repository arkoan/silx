using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Silx.Etude;
namespace Silx.App;

public enum Tool { Select, Wire, Place, ZoomWindow, Probe }

/// <summary>Canevas de schéma : grille, snap, zoom/pan, placement avec aperçu, fils, rotation, suppression, undo/redo.</summary>
public sealed class SchematicCanvas : Control
{
    public const int Grid = 12;             // pixels par pas de grille à zoom 1
    /// <summary>Schéma en cours d'édition : le circuit sous test, ou le calque du banc actif.</summary>
    public Schematic Schematic { get; private set; } = new();
    /// <summary>Circuit sous test affiché sous le calque (atténué, non éditable) quand un banc est actif.</summary>
    Schematic? _underlay;
    /// <summary>Texte affiché en haut à gauche du canevas.</summary>
    public string Caption { get; set; } = "";
    public Tool Tool
    {
        get => _tool;
        set
        {
            _tool = value; _wireStart = null;
            // Curseur de souris propre à chaque mode : on voit d'un coup d'œil ce que fera le prochain clic.
            Cursor = value switch { Tool.Select => Cursor.Default, Tool.Probe => new Cursor(StandardCursorType.Hand), _ => new Cursor(StandardCursorType.Cross) };
            ToolChanged?.Invoke(); InvalidateVisual();
        }
    }
    /// <summary>Nom lisible du composant en cours de placement (puce de mode).</summary>
    public string NomPlacement { get; set; } = "";

    /// <summary>Texte de la puce de mode (vide en sélection).</summary>
    public string TexteMode => _tool switch
    {
        Tool.Wire => "Fil — clic pour chaque coin, double-clic ou arrivée sur une broche pour finir · Échap",
        Tool.Place => $"Placement : {NomPlacement} — Maj+clic pour en poser plusieurs · ⌘R pivoter · Échap",
        Tool.Probe => "Sonde — cliquez un fil ou une broche pour tracer sa tension · Échap",
        Tool.ZoomWindow => "Zoom fenêtre — tracez un rectangle · Échap",
        _ => ""
    };
    public ComponentKind PlaceKind { get; private set; }
    /// <summary>Le schéma a changé (topologie, valeurs).</summary>
    public event Action? Changed;
    public event Action? SelectionChanged;
    public event Action? ToolChanged;
    /// <summary>Sonde posée : nom du nœud (équipotentiel) cliqué.</summary>
    public event Action<string>? Probed;
    /// <summary>Double-clic sur un composant (outil Sélection) : édition sur place.</summary>
    public event Action<PlacedComponent>? DoubleClic;
    /// <summary>Calques des bancs de test posés sur le circuit édité : quand une broche du circuit bouge, leurs fils suivent
    /// (et l'annulation les restaure avec le circuit).</summary>
    public IReadOnlyList<Schematic> CalquesLies { get; set; } = [];
    /// <summary>Faux si le dernier déplacement a dû changer des connexions (un fil étiré touche une autre broche).</summary>
    public bool DernierDeplacementSur { get; private set; } = true;
    /// <summary>Définition du composant SPICE à placer (outil Place, PlaceKind = Spice).</summary>
    public DefinitionComposant? PlaceDefinition { get; private set; }
    Schematic? _dragOrigin; GridPoint _dragStart;
    string? _probeNode;

    Tool _tool = Tool.Select;
    readonly Stack<(Schematic S, List<List<Wire>> Calques)> _undo = new(), _redo = new();
    double _zoom = 2.6; Point _pan = new(60, -60);
    GridPoint _cursor; GridPoint? _wireStart; bool _panning; Point _panLast; bool _inside;
    PlacedComponent? _selected; Wire? _selectedWire; PlacedComponent? _dragging; GridPoint _dragOffset; bool _dragMoved;
    // Sélection multiple : composants et fils ; rectangle de sélection tracé depuis le fond.
    readonly HashSet<PlacedComponent> _multi = new(); readonly HashSet<Wire> _multiWires = new();
    Point? _boxStart; bool _boxAjout; List<Wire>? _dragFilsOrigine;
    List<PlacedComponent>? _dragGroup;
    int _ghostRotation;

    static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1c1f26"));
    static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2563eb"));
    static readonly IBrush WireBrush = new SolidColorBrush(Color.Parse("#1f6f43"));
    static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#b45309"));
    static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#5b6170"));
    static readonly IBrush GridDot = new SolidColorBrush(Color.Parse("#c9ccd3"));
    public static readonly IBrush BenchBrush = new SolidColorBrush(Color.Parse("#7c3aed"));
    static readonly IBrush Line = new SolidColorBrush(Color.Parse("#dfe2e8"));

    public SchematicCanvas() { Focusable = true; ClipToBounds = true; }

    public void Load(Schematic s, Schematic? underlay = null)
    {
        Schematic = s; _underlay = underlay; CalquesLies = []; _undo.Clear(); _redo.Clear(); _wireStart = null; Select(null); Changed?.Invoke(); InvalidateVisual();
    }
    public bool IsLayer => _underlay != null;

    public Point ScreenOf(GridPoint p) => ToScreen(p);
    Schematic Merged() => new() { Components = AllComponents.ToList(), Wires = AllWires.ToList() };

    /// <summary>Nœud sous le pointeur : broche (à 0,6 pas près) ou fil.</summary>
    public string? NodeAt(Point pos)
    {
        var m = Merged(); var conn = Connectivity.Build(m);
        var d = Grid * _zoom * 0.6;
        foreach (var c in m.Components.Where(c => c.IsElectrical || c.Kind == ComponentKind.Label))
            foreach (var p in c.Pins)
            {
                var sp = ToScreen(p);
                if (Math.Abs(sp.X - pos.X) < d && Math.Abs(sp.Y - pos.Y) < d && conn.NodeOf.TryGetValue(p, out var n)) return n;
            }
        foreach (var w in m.Wires)
            if (HitWire(w, pos) && conn.NodeOf.TryGetValue(w.A, out var n)) return n;
        return null;
    }

    // ---- loupes ----
    public void ZoomBy(double f) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), f);
    void ZoomAt(Point pos, double f)
    {
        var before = (pos - _pan) / _zoom;
        _zoom = Math.Clamp(_zoom * f, 0.4, 8);
        _pan = pos - before * _zoom; InvalidateVisual();
    }
    /// <summary>Cadre le schéma entier (circuit et calque) dans la vue.</summary>
    public void FitAll()
    {
        var pts = AllComponents.SelectMany(c => c.Pins).Concat(AllWires.SelectMany(w => new[] { w.A, w.B })).ToList();
        if (pts.Count == 0 || Bounds.Width < 10) return;
        FitGrid(pts.Min(p => p.X) - 3, pts.Min(p => p.Y) - 4, pts.Max(p => p.X) + 5, pts.Max(p => p.Y) + 3, 3.2);
    }
    void FitGrid(double x0, double y0, double x1, double y1, double maxZoom)
    {
        double w = Math.Max(x1 - x0, 1) * Grid, h = Math.Max(y1 - y0, 1) * Grid;
        _zoom = Math.Clamp(Math.Min(Bounds.Width / w, Bounds.Height / h), 0.4, maxZoom);
        _pan = new Point((Bounds.Width - (x0 + x1) * Grid * _zoom) / 2, (Bounds.Height - (y0 + y1) * Grid * _zoom) / 2);
        InvalidateVisual();
    }
    Point? _zoomStart; Point _lastPos;
    IEnumerable<PlacedComponent> AllComponents => _underlay == null ? Schematic.Components : _underlay.Components.Concat(Schematic.Components);
    IEnumerable<Wire> AllWires => _underlay == null ? Schematic.Wires : _underlay.Wires.Concat(Schematic.Wires);
    string NextRef(ComponentKind k)
    {
        var merged = new Schematic { Components = AllComponents.ToList() };
        return k == ComponentKind.Spice && PlaceDefinition != null ? merged.NextRef(PlaceDefinition.Lettre) : merged.NextRef(k);
    }
    public void BeginPlace(ComponentKind k) { PlaceKind = k; PlaceDefinition = null; NomPlacement = NomDe(k); Tool = Tool.Place; Focus(); }
    public void BeginPlace(DefinitionComposant d) { PlaceKind = ComponentKind.Spice; PlaceDefinition = d; NomPlacement = d.Nom; Tool = Tool.Place; Focus(); }
    static string NomDe(ComponentKind k) => k switch
    {
        ComponentKind.Resistor => "résistance", ComponentKind.Capacitor => "condensateur", ComponentKind.Inductor => "inductance",
        ComponentKind.VoltageSource => "source de tension", ComponentKind.CurrentSource => "source de courant", ComponentKind.Diode => "diode",
        ComponentKind.Ground => "masse", ComponentKind.Label => "étiquette de nœud", _ => "composant"
    };

    // ---- transformations ----
    Point ToScreen(GridPoint p) => new(p.X * Grid * _zoom + _pan.X, p.Y * Grid * _zoom + _pan.Y);
    GridPoint Snap(Point s) => new((int)Math.Round((s.X - _pan.X) / (Grid * _zoom)), (int)Math.Round((s.Y - _pan.Y) / (Grid * _zoom)));

    // ---- undo/redo : HYPOTHÈSE v1 = instantanés complets (suffisant pour quelques centaines d'éléments) ----
    (Schematic, List<List<Wire>>) Snapshot() => (Schematic.Clone(), CalquesLies.Select(c => c.Wires.ToList()).ToList());
    void Commit() { _undo.Push(Snapshot()); _redo.Clear(); }
    void Done() { Changed?.Invoke(); InvalidateVisual(); }
    public bool CanUndo => _undo.Count > 0;
    public void Undo() { if (_undo.Count == 0) return; _redo.Push(Snapshot()); Restore(_undo.Pop()); }
    public void Redo() { if (_redo.Count == 0) return; _undo.Push(Snapshot()); Restore(_redo.Pop()); }
    void Restore((Schematic S, List<List<Wire>> Calques) snap)
    {
        Schematic.Components.Clear(); Schematic.Components.AddRange(snap.S.Components);
        Schematic.Wires.Clear(); Schematic.Wires.AddRange(snap.S.Wires);
        for (int i = 0; i < CalquesLies.Count && i < snap.Calques.Count; i++) { CalquesLies[i].Wires.Clear(); CalquesLies[i].Wires.AddRange(snap.Calques[i]); }
        Select(null); Done();
    }

    void SuivreCalques(IReadOnlyDictionary<GridPoint, GridPoint> moves)
    {
        if (IsLayer || moves.Count == 0) return;
        foreach (var c in CalquesLies) Elastique.SuivreCalque(c, moves);
    }

    /// <summary>Déplace un composant du schéma édité comme un glisser à la souris (fils élastiques, une entrée d'annulation).</summary>
    public bool MoveComponent(PlacedComponent c, GridPoint delta)
    {
        Commit();
        var avant = Schematic.Components.ToDictionary(k => k, k => k.Pins.ToList());
        DernierDeplacementSur = Elastique.Deplacer(Schematic, [c], delta);
        var moves = new Dictionary<GridPoint, GridPoint>();
        foreach (var (k, pins) in avant) foreach (var (a, b) in pins.Zip(k.Pins)) if (!a.Equals(b)) moves.TryAdd(a, b);
        SuivreCalques(moves); Done();
        return DernierDeplacementSur;
    }

    /// <summary>Le symbole d'une définition a changé : les fils attachés aux broches de ses instances suivent (broches repérées par leur nom de modèle).</summary>
    public void SymboleModifie(DefinitionComposant d, Symbole ancien)
    {
        Commit();
        var moves = new Dictionary<GridPoint, GridPoint>();
        foreach (var k in Schematic.Components.Where(k => k.Definition == d))
        {
            var avant = new PlacedComponent { Ref = k.Ref, Kind = k.Kind, Position = k.Position, Rotation = k.Rotation, Definition = new DefinitionComposant { Symbole = ancien } };
            var pa = avant.Pins; var pb = k.Pins;
            for (int i = 0; i < ancien.Broches.Count; i++)
            {
                var j = d.Symbole.Broches.FindIndex(b => b.BrocheModele != null && string.Equals(b.BrocheModele, ancien.Broches[i].BrocheModele, StringComparison.OrdinalIgnoreCase));
                if (j >= 0 && !pa[i].Equals(pb[j])) moves.TryAdd(pa[i], pb[j]);
            }
        }
        if (moves.Count > 0)
        {
            var fixes = Schematic.Components.Where(k => k.Definition != d).SelectMany(k => k.Pins).ToHashSet();
            var w = Elastique.Suivre(Schematic.Wires, moves, fixes);
            Schematic.Wires.Clear(); Schematic.Wires.AddRange(w);
            SuivreCalques(moves);
        }
        Done(); SelectionChanged?.Invoke();
    }

    /// <summary>Composant sélectionné seul (propriétés) ; null si rien ou plusieurs éléments.</summary>
    public PlacedComponent? Selected => _multi.Count + _multiWires.Count > 1 ? null : _selected;
    /// <summary>Composants sélectionnés (un ou plusieurs).</summary>
    public IReadOnlyCollection<PlacedComponent> Selection => _multi.Count > 0 ? _multi : _selected != null ? [_selected] : [];
    public IReadOnlyCollection<Wire> FilsSelectionnes => _multiWires.Count > 0 ? _multiWires : _selectedWire != null ? [_selectedWire] : [];
    void Select(PlacedComponent? c, Wire? w = null)
    {
        _selected = c; _selectedWire = w; _multi.Clear(); _multiWires.Clear();
        if (c != null) _multi.Add(c); if (w != null) _multiWires.Add(w);
        SelectionChanged?.Invoke(); InvalidateVisual();
    }

    /// <summary>Sélectionne les composants entièrement dans le rectangle (coordonnées écran) et les fils dont les deux bouts y sont.</summary>
    public void SelectBox(Rect r, bool ajouter = false)
    {
        if (!ajouter) { _multi.Clear(); _multiWires.Clear(); }
        foreach (var c in Schematic.Components.Where(c => c.Pins.All(p => r.Contains(ToScreen(p))))) _multi.Add(c);
        foreach (var w in Schematic.Wires.Where(w => r.Contains(ToScreen(w.A)) && r.Contains(ToScreen(w.B)))) _multiWires.Add(w);
        _selected = _multi.Count == 1 && _multiWires.Count == 0 ? _multi.First() : _multi.FirstOrDefault();
        _selectedWire = _multiWires.Count == 1 && _multi.Count == 0 ? _multiWires.First() : null;
        SelectionChanged?.Invoke(); InvalidateVisual();
    }

    void Basculer(PlacedComponent? c, Wire? w)
    {
        if (c != null && !_multi.Remove(c)) _multi.Add(c);
        if (w != null && !_multiWires.Remove(w)) _multiWires.Add(w);
        _selected = _multi.FirstOrDefault(); _selectedWire = _multi.Count == 0 && _multiWires.Count == 1 ? _multiWires.First() : null;
        SelectionChanged?.Invoke(); InvalidateVisual();
    }

    /// <summary>Déplace la sélection (fils élastiques, fils sélectionnés entraînés), une entrée d'annulation.</summary>
    public bool MoveSelection(GridPoint delta)
    {
        Commit();
        var avant = Schematic.Components.ToDictionary(k => k, k => k.Pins.ToList());
        DernierDeplacementSur = Elastique.Deplacer(Schematic, Selection.ToList(), delta, FilsSelectionnes.ToList());
        var moves = new Dictionary<GridPoint, GridPoint>();
        foreach (var (k, pins) in avant) foreach (var (a, b) in pins.Zip(k.Pins)) if (!a.Equals(b)) moves.TryAdd(a, b);
        _multiWires.Clear(); SuivreCalques(moves); Done();
        return DernierDeplacementSur;
    }

    public void RotateSelected()
    {
        if (Selection.Count == 0) { _ghostRotation = (_ghostRotation + 1) % 4; InvalidateVisual(); return; }
        Commit();
        // HYPOTHÈSE : plusieurs composants pivotent chacun sur lui-même (pas autour du centre du groupe).
        var moves = new Dictionary<GridPoint, GridPoint>(); bool sur = true;
        foreach (var c in Selection.ToList())
        {
            var avant = c.Pins.ToList();
            sur &= Elastique.Pivoter(Schematic, c);
            foreach (var (a, b) in avant.Zip(c.Pins)) if (!a.Equals(b)) moves.TryAdd(a, b);
        }
        DernierDeplacementSur = sur;
        SuivreCalques(moves);
        Done();
    }
    public void DeleteSelected()
    {
        var comps = Selection.ToList(); var fils = FilsSelectionnes.ToList();
        if (comps.Count + fils.Count == 0) return;
        Commit();
        foreach (var c in comps) Schematic.Components.Remove(c);
        foreach (var w in fils) Schematic.Wires.Remove(w);
        Select(null); Done();
    }
    public void EditSelected(string reference, string value)
    {
        if (_selected == null || (_selected.Ref == reference && _selected.Value == value)) return;
        Commit(); _selected.Ref = reference.Trim(); _selected.Value = value; Done(); SelectionChanged?.Invoke();
    }

    // ---- entrées ----
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var pt = e.GetCurrentPoint(this); var pos = pt.Position; var g = Snap(pos);
        if (pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed)
        {
            if (pt.Properties.IsRightButtonPressed && _tool != Tool.Select) { Tool = Tool.Select; return; }
            _panning = true; _panLast = pos; return;
        }
        switch (_tool)
        {
            case Tool.Probe:
                if (NodeAt(pos) is { } node && node != "0") Probed?.Invoke(node);
                break;
            case Tool.ZoomWindow:
                _zoomStart = pos; break;
            case Tool.Wire:
                if (_wireStart is null) _wireStart = g;
                else
                {
                    var a = _wireStart.Value;
                    // HYPOTHÈSE : fils en L (horizontal puis vertical) pour garder un schéma orthogonal.
                    var segs = new List<Wire>();
                    if (a.X != g.X && a.Y != g.Y) { var m = new GridPoint(g.X, a.Y); segs.Add(new(a, m)); segs.Add(new(m, g)); }
                    else if (!a.Equals(g)) segs.Add(new(a, g));
                    if (segs.Count > 0) { Commit(); Schematic.Wires.AddRange(segs); Done(); }
                    // Fin de tracé : double-clic, ou arrivée sur une broche / un fil existant.
                    _wireStart = e.ClickCount > 1 || IsConnectionPoint(g, segs) ? null : g;
                }
                break;
            case Tool.Select:
                // Un composant électrique l'emporte sur une étiquette ou une masse qui le chevauche.
                var touches = Schematic.Components.Where(c => HitTest(c, pos)).ToList();
                var hit = touches.LastOrDefault(c => c.IsElectrical) ?? touches.LastOrDefault();
                var wire = hit == null ? Schematic.Wires.LastOrDefault(w => HitWire(w, pos)) : null;
                bool maj = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if (hit == null && wire == null)
                {
                    // Fond : rectangle de sélection (Maj : ajoute à la sélection).
                    _boxStart = pos; _boxAjout = maj;
                    if (!maj) Select(null);
                    break;
                }
                if (maj) { Basculer(hit, wire); break; }
                if (e.ClickCount >= 2 && hit != null) { Select(hit); DoubleClic?.Invoke(hit); break; }   // édition sur place
                bool groupe = hit != null && _multi.Contains(hit) && _multi.Count + _multiWires.Count > 1;
                if (!groupe) Select(hit, wire);
                if (hit != null)
                {
                    _dragging = hit; _dragMoved = false; _dragOffset = new(g.X - hit.Position.X, g.Y - hit.Position.Y); _dragOrigin = Schematic.Clone(); _dragStart = hit.Position;
                    _dragGroup = groupe ? _multi.ToList() : null; _dragFilsOrigine = groupe ? _multiWires.ToList() : null; Commit();
                }
                break;
            case Tool.Place:
                Commit();
                var comp = new PlacedComponent { Ref = NextRef(PlaceKind), Kind = PlaceKind, Position = g, Rotation = _ghostRotation, Value = PlaceKind == ComponentKind.Spice ? "" : Schematic.DefaultValue(PlaceKind), Definition = PlaceKind == ComponentKind.Spice ? PlaceDefinition : null };
                Schematic.Components.Add(comp);
                Done();
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { Tool = Tool.Select; Select(comp); }
                break;
        }
        InvalidateVisual();
    }

    bool IsConnectionPoint(GridPoint g, List<Wire> justAdded)
        => AllComponents.Any(c => c.Pins.Contains(g))
        || AllWires.Except(justAdded).Any(w => w.A.Equals(g) || w.B.Equals(g) || OnSeg(g, w));

    static bool OnSeg(GridPoint p, Wire w)
        => (w.A.X == w.B.X && p.X == w.A.X && p.Y > Math.Min(w.A.Y, w.B.Y) && p.Y < Math.Max(w.A.Y, w.B.Y))
        || (w.A.Y == w.B.Y && p.Y == w.A.Y && p.X > Math.Min(w.A.X, w.B.X) && p.X < Math.Max(w.A.X, w.B.X));

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var pos = e.GetPosition(this); _inside = true; _lastPos = pos;
        if (_panning) { _pan += pos - _panLast; _panLast = pos; }
        _cursor = Snap(pos);
        if (_tool == Tool.Probe) _probeNode = NodeAt(pos);
        if (_dragging != null && _dragOrigin != null)
        {
            var np = new GridPoint(_cursor.X - _dragOffset.X, _cursor.Y - _dragOffset.Y);
            if (!np.Equals(_dragging.Position))
            {
                // Fils élastiques : on repart toujours de l'état au début du glisser, avec le déplacement total.
                for (int i = 0; i < Schematic.Components.Count && i < _dragOrigin.Components.Count; i++)
                    Schematic.Components[i].Position = _dragOrigin.Components[i].Position;
                Schematic.Wires.Clear(); Schematic.Wires.AddRange(_dragOrigin.Wires);
                var delta = new GridPoint(np.X - _dragStart.X, np.Y - _dragStart.Y);
                if (_dragGroup != null)
                {
                    // Groupe : fils sélectionnés repris de l'état d'origine (mêmes valeurs), entraînés avec le groupe.
                    DernierDeplacementSur = Elastique.Deplacer(Schematic, _dragGroup, delta, _dragFilsOrigine);
                }
                else DernierDeplacementSur = Elastique.Deplacer(Schematic, [_dragging], delta);
                _dragMoved = true;
            }
        }
        InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e) { _inside = false; InvalidateVisual(); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _panning = false;
        if (_boxStart is { } b0)
        {
            var b1 = e.GetPosition(this); _boxStart = null;
            if (Math.Abs(b1.X - b0.X) > 4 || Math.Abs(b1.Y - b0.Y) > 4) SelectBox(new Rect(b0, b1).Normalize(), _boxAjout);
            InvalidateVisual(); return;
        }
        if (_zoomStart is { } z0)
        {
            var z1 = e.GetPosition(this); _zoomStart = null;
            if (Math.Abs(z1.X - z0.X) > 8 && Math.Abs(z1.Y - z0.Y) > 8)
            {
                double gx(double x) => (x - _pan.X) / (Grid * _zoom);
                double gy(double y) => (y - _pan.Y) / (Grid * _zoom);
                FitGrid(gx(Math.Min(z0.X, z1.X)), gy(Math.Min(z0.Y, z1.Y)), gx(Math.Max(z0.X, z1.X)), gy(Math.Max(z0.Y, z1.Y)), 8);
            }
            Tool = Tool.Select; return;
        }
        if (_dragging != null)
        {
            if (!_dragMoved) _undo.Pop(); // simple clic : pas d'entrée d'historique
            else if (!IsLayer && _dragOrigin != null)
            {
                var moves = new Dictionary<GridPoint, GridPoint>();
                for (int i = 0; i < Schematic.Components.Count && i < _dragOrigin.Components.Count; i++)
                    foreach (var (a, b) in _dragOrigin.Components[i].Pins.Zip(Schematic.Components[i].Pins))
                        if (!a.Equals(b)) moves.TryAdd(a, b);
                SuivreCalques(moves);
            }
            if (_dragGroup != null) _multiWires.Clear();   // les fils entraînés ont changé de place
            _dragging = null; _dragOrigin = null; _dragGroup = null; _dragFilsOrigine = null; Done();
        }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) => ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.12 : 1 / 1.12);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (cmd && e.Key == Key.Z) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); }
        else if (cmd && e.Key == Key.Y) Redo();
        else if (cmd && e.Key == Key.R) RotateSelected();
        else if (cmd) return;
        else if (e.Key is Key.Delete or Key.Back) DeleteSelected();
        else if (e.Key == Key.Escape) { if (_wireStart != null) _wireStart = null; else Tool = Tool.Select; }
        else if (e.Key == Key.W) Tool = Tool.Wire;
        else if (e.Key == Key.P) Tool = Tool.Probe;
        else if (e.Key is Key.OemPlus or Key.Add) ZoomBy(1.25);
        else if (e.Key is Key.OemMinus or Key.Subtract) ZoomBy(1 / 1.25);
        else if (e.Key is Key.D0 or Key.NumPad0 or Key.F) FitAll();
        else if (Shortcut(e.Key) is { } k) BeginPlace(k);
        else return;
        e.Handled = true; InvalidateVisual();
    }

    public static ComponentKind? Shortcut(Key k) => k switch
    {
        Key.R => ComponentKind.Resistor, Key.C => ComponentKind.Capacitor, Key.L => ComponentKind.Inductor,
        Key.V => ComponentKind.VoltageSource, Key.I => ComponentKind.CurrentSource, Key.D => ComponentKind.Diode,
        Key.G => ComponentKind.Ground, Key.N => ComponentKind.Label, _ => null
    };

    bool HitTest(PlacedComponent c, Point p)
    {
        if (c.Kind == ComponentKind.Spice) return SpiceBounds(c).Contains(p);
        var pins = c.Pins; var a = ToScreen(pins[0]);
        if (!c.IsElectrical)
        {
            var d = Grid * _zoom;
            var box = c.Kind == ComponentKind.Ground ? new Rect(a.X - d * .7, a.Y - d * .2, d * 1.4, d * 1.5) : new Rect(a.X - d * .3, a.Y - d * 1.6, d * 4, d * 1.8);
            return box.Contains(p);
        }
        var b = ToScreen(pins[1]);
        var r = new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)).Inflate(Grid * _zoom * 0.8);
        return r.Contains(p);
    }
    /// <summary>Rectangle écran d'un composant SPICE importé (dessin et broches).</summary>
    Rect SpiceBounds(PlacedComponent c)
    {
        if (c.Definition == null) return new Rect(ToScreen(c.Position), new Size(1, 1)).Inflate(Grid * _zoom);
        var (x0, y0, x1, y1) = c.Definition.Symbole.Encombrement();
        var map = Symboles.Repere(ToScreen(c.Position), c.Rotation, Grid * _zoom);
        return new Rect(map(x0, y0), map(x1, y1)).Normalize().Inflate(Grid * _zoom * 0.3);
    }

    bool HitWire(Wire w, Point p)
    {
        var a = ToScreen(w.A); var b = ToScreen(w.B);
        return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)).Inflate(5).Contains(p);
    }

    // ---- rendu ----
    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        var step = Grid * _zoom;
        if (step >= 6)
        {
            double x0 = _pan.X % step, y0 = _pan.Y % step;
            for (double x = x0; x < Bounds.Width; x += step) for (double y = y0; y < Bounds.Height; y += step)
                ctx.FillRectangle(GridDot, new Rect(x - 0.75, y - 0.75, 1.5, 1.5));
        }

        double lw = Math.Max(1.2, 1.6 * _zoom / 2);
        var layer = _underlay != null;
        var wireBrush = layer ? BenchBrush : WireBrush;
        var wirePen = new Pen(wireBrush, lw);
        if (_underlay != null)
        {
            // Circuit sous test en dessous : atténué, non éditable.
            using (ctx.PushOpacity(0.38))
            {
                var dim = new Pen(WireBrush, lw);
                foreach (var w in _underlay.Wires) ctx.DrawLine(dim, ToScreen(w.A), ToScreen(w.B));
            }
            foreach (var c in _underlay.Components) DrawComponent(ctx, c, c.Kind == ComponentKind.Label ? LabelBrush : Ink, c.Kind == ComponentKind.Label ? 0.75 : 0.38, labels: true);
        }
        foreach (var w in Schematic.Wires) ctx.DrawLine(w == _selectedWire || _multiWires.Contains(w) ? new Pen(Accent, lw + 1) : wirePen, ToScreen(w.A), ToScreen(w.B));

        // Jonctions : point plein là où au moins 3 éléments se rejoignent (circuit et banc confondus).
        var allWires = AllWires.ToList();
        var ends = allWires.SelectMany(w => new[] { w.A, w.B }).Concat(AllComponents.SelectMany(c => c.Pins)).ToList();
        foreach (var p in ends.Distinct())
        {
            int n = ends.Count(q => q.Equals(p)) + allWires.Count(w => OnSeg(p, w)) * 2;
            if (n >= 3) ctx.DrawEllipse(layer ? BenchBrush : WireBrush, null, ToScreen(p), 3.2 * _zoom / 2 + 1, 3.2 * _zoom / 2 + 1);
        }

        if (_tool == Tool.Probe && _probeNode is { } pn && pn != "0" && _inside)
        {
            // Équipotentiel survolé : fils et broches surlignés.
            var conn = Connectivity.Build(Merged());
            var hi = new Pen(new SolidColorBrush(Color.Parse("#f59e0b"), 0.55), lw + 6) { LineCap = PenLineCap.Round };
            foreach (var w in AllWires.Where(w => conn.NodeOf.TryGetValue(w.A, out var n) && n == pn)) ctx.DrawLine(hi, ToScreen(w.A), ToScreen(w.B));
            foreach (var p in AllComponents.SelectMany(c => c.Pins).Where(p => conn.NodeOf.TryGetValue(p, out var n) && n == pn))
                ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#f59e0b"), 0.7), null, ToScreen(p), 6, 6);
        }
        foreach (var c in Schematic.Components) DrawComponent(ctx, c, c == _selected || _multi.Contains(c) ? Accent : layer ? BenchBrush : c.Kind == ComponentKind.Label ? LabelBrush : Ink, 1);
        if (_tool == Tool.Probe && _inside)
        {
            // Pointe de sonde + nom du nœud visé.
            var c0 = _lastPos; var tip = new Pen(new SolidColorBrush(Color.Parse("#b45309")), 2);
            ctx.DrawLine(tip, c0, new Point(c0.X + 10, c0.Y - 10)); ctx.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#b45309")), 5), new Point(c0.X + 10, c0.Y - 10), new Point(c0.X + 20, c0.Y - 20));
            if (_probeNode is { } pn2)
            {
                var t = new FormattedText(pn2 == "0" ? "masse" : $"V({pn2})", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 12, Brushes.White);
                var r = new Rect(c0.X + 22, c0.Y - 34, t.Width + 10, t.Height + 4);
                ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#b45309")), null, r, 3, 3); ctx.DrawText(t, new Point(r.X + 5, r.Y + 2));
            }
        }
        if (_dragging != null && !DernierDeplacementSur)
        {
            var t = new FormattedText("⚠ Ce déplacement modifie des connexions (un fil étiré touche une autre broche)", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 12, Brushes.White);
            var r = new Rect(10, Bounds.Height - t.Height - 18, t.Width + 16, t.Height + 8);
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#b91c1c")), null, r, 4, 4); ctx.DrawText(t, new Point(r.X + 8, r.Y + 4));
        }
        // Banc actif : cadre violet autour du canevas et nom du banc dans un coin (au lieu d'un long bandeau).
        if (layer) ctx.DrawRectangle(null, new Pen(BenchBrush, 3), new Rect(1.5, 1.5, Bounds.Width - 3, Bounds.Height - 3));
        double yChip = 10;
        if (Caption.Length > 0)
        {
            var t = new FormattedText(Caption, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 12, layer ? BenchBrush : Muted);
            var box = new Rect(10, yChip, t.Width + 16, t.Height + 8);
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse(layer ? "#f3effe" : "#f3f4f6")), new Pen(layer ? BenchBrush : Line, 1), box, 4, 4);
            ctx.DrawText(t, new Point(18, yChip + 4));
            yChip = box.Bottom + 6;
        }
        if (TexteMode is { Length: > 0 } mode)
        {
            // Puce de mode : le mode actif est toujours écrit, avec la façon d'en sortir.
            var t = new FormattedText(mode, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 12, Brushes.White);
            var box = new Rect(10, yChip, t.Width + 16, t.Height + 8);
            ctx.DrawRectangle(Accent, null, box, 4, 4);
            ctx.DrawText(t, new Point(18, yChip + 4));
        }

        if (_inside && _tool == Tool.Place)
            DrawComponent(ctx, new PlacedComponent { Ref = Schematic.Prefix(PlaceKind) + "?", Kind = PlaceKind, Position = _cursor, Rotation = _ghostRotation, Value = "", Definition = PlaceDefinition }, Accent, 0.45);
        if (_wireStart is { } s)
        {
            var dash = new Pen(WireBrush, lw, new DashStyle([4, 3], 0));
            var a = s; var g = _cursor;
            if (a.X != g.X && a.Y != g.Y) { var m = new GridPoint(g.X, a.Y); ctx.DrawLine(dash, ToScreen(a), ToScreen(m)); ctx.DrawLine(dash, ToScreen(m), ToScreen(g)); }
            else ctx.DrawLine(dash, ToScreen(a), ToScreen(g));
        }
        if (_boxStart is { } bs && _inside)
        {
            var r = new Rect(bs, _lastPos).Normalize();
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#2563eb"), 0.06), new Pen(Accent, 1, new DashStyle([4, 3], 0)), r);
        }
        if (_zoomStart is { } zs && _inside)
        {
            var r = new Rect(zs, _lastPos).Normalize();
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#2563eb"), 0.08), new Pen(Accent, 1, new DashStyle([4, 3], 0)), r);
        }
        if (_inside && _tool is Tool.Wire or Tool.Place)
        {
            var c = ToScreen(_cursor); var pen = new Pen(Accent, 1);
            ctx.DrawLine(pen, new(c.X - 6, c.Y), new(c.X + 6, c.Y)); ctx.DrawLine(pen, new(c.X, c.Y - 6), new(c.X, c.Y + 6));
        }
    }

    FormattedText Text(string s, double size, IBrush b, FontWeight w = FontWeight.Normal)
        => new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, w), size * _zoom / 2, b);

    void DrawComponent(DrawingContext ctx, PlacedComponent c, IBrush brush, double opacity, bool labels = false)
    {
        using var _ = ctx.PushOpacity(opacity);
        var pen = new Pen(brush, Math.Max(1.2, 1.6 * _zoom / 2));
        if (c.Kind == ComponentKind.Spice)
        {
            if (c.Definition is { } def)
                Symboles.DessinerSymbole(ctx, def.Symbole, Symboles.Repere(ToScreen(c.Position), c.Rotation, _zoom * Grid), _zoom * Grid, brush, pen, Text);
            if (opacity < 1 && !labels) return;
            var r = SpiceBounds(c);
            var rt = Text(c.Ref, 11, brush, FontWeight.SemiBold);
            var vt = Text(Short(c.Definition?.Modele + (string.IsNullOrWhiteSpace(c.Value) ? "" : " " + c.Value)), 10, Muted);
            ctx.DrawText(rt, new Point(r.Right + 2, r.Center.Y - rt.Height));
            ctx.DrawText(vt, new Point(r.Right + 2, r.Center.Y));
            return;
        }
        var pins = c.Pins; var a = ToScreen(pins[0]); var d = _zoom * Grid;
        var b = c.IsElectrical ? ToScreen(pins[1]) : a;
        Symboles.Draw(ctx, c.Kind, c.Rotation, c.Value, a, b, d, brush, pen, Text);
        if (!c.IsElectrical) return;
        var dir = b - a; var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y); var u = new Point(dir.X / len, dir.Y / len); var n = new Point(-u.Y, u.X);
        Point At(double t, double off = 0) => new(a.X + u.X * len * t + n.X * off, a.Y + u.Y * len * t + n.Y * off);
        double h = d * 0.7;
        ctx.DrawEllipse(brush, null, a, 2, 2); ctx.DrawEllipse(brush, null, b, 2, 2);
        if (opacity < 1 && !labels) return;
        var refT = Text(c.Ref, 11, brush, FontWeight.SemiBold);
        var valT = Text(Short(c.Kind is ComponentKind.VoltageSource or ComponentKind.CurrentSource ? Stimulus.Lire(c.Value).Resume(c.Kind == ComponentKind.CurrentSource) : c.Value), 10, Muted);
        bool vertical = Math.Abs(u.Y) > 0.5;
        if (vertical)
        {
            var p = At(.5); var x = p.X + Math.Max(h * 1.1, len * .26);
            ctx.DrawText(refT, new Point(x, p.Y - refT.Height));
            ctx.DrawText(valT, new Point(x, p.Y));
        }
        else
        {
            var p = At(.5); var y = p.Y + h * .7;
            ctx.DrawText(refT, new Point(p.X - refT.Width / 2, y));
            ctx.DrawText(valT, new Point(p.X - valT.Width / 2, y + refT.Height));
        }
    }

    static string Short(string v) => v.Length > 26 ? v[..25] + "…" : v;
}
