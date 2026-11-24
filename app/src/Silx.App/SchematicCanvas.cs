using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Silx.Etude;
namespace Silx.App;

public enum Tool { Select, Wire, Place }

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
    public Tool Tool { get => _tool; set { _tool = value; _wireStart = null; ToolChanged?.Invoke(); InvalidateVisual(); } }
    public ComponentKind PlaceKind { get; private set; }
    /// <summary>Le schéma a changé (topologie, valeurs).</summary>
    public event Action? Changed;
    public event Action? SelectionChanged;
    public event Action? ToolChanged;

    Tool _tool = Tool.Select;
    readonly Stack<Schematic> _undo = new(), _redo = new();
    double _zoom = 2.6; Point _pan = new(60, -60);
    GridPoint _cursor; GridPoint? _wireStart; bool _panning; Point _panLast; bool _inside;
    PlacedComponent? _selected; Wire? _selectedWire; PlacedComponent? _dragging; GridPoint _dragOffset; bool _dragMoved;
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
        Schematic = s; _underlay = underlay; _undo.Clear(); _redo.Clear(); _wireStart = null; Select(null); Changed?.Invoke(); InvalidateVisual();
    }
    public bool IsLayer => _underlay != null;
    IEnumerable<PlacedComponent> AllComponents => _underlay == null ? Schematic.Components : _underlay.Components.Concat(Schematic.Components);
    IEnumerable<Wire> AllWires => _underlay == null ? Schematic.Wires : _underlay.Wires.Concat(Schematic.Wires);
    string NextRef(ComponentKind k)
    {
        var merged = new Schematic { Components = AllComponents.ToList() };
        return merged.NextRef(k);
    }
    public void BeginPlace(ComponentKind k) { PlaceKind = k; Tool = Tool.Place; Focus(); }

    // ---- transformations ----
    Point ToScreen(GridPoint p) => new(p.X * Grid * _zoom + _pan.X, p.Y * Grid * _zoom + _pan.Y);
    GridPoint Snap(Point s) => new((int)Math.Round((s.X - _pan.X) / (Grid * _zoom)), (int)Math.Round((s.Y - _pan.Y) / (Grid * _zoom)));

    // ---- undo/redo : HYPOTHÈSE v1 = instantanés complets (suffisant pour quelques centaines d'éléments) ----
    void Commit() { _undo.Push(Schematic.Clone()); _redo.Clear(); }
    void Done() { Changed?.Invoke(); InvalidateVisual(); }
    public bool CanUndo => _undo.Count > 0;
    public void Undo() { if (_undo.Count == 0) return; _redo.Push(Schematic.Clone()); Restore(_undo.Pop()); }
    public void Redo() { if (_redo.Count == 0) return; _undo.Push(Schematic.Clone()); Restore(_redo.Pop()); }
    void Restore(Schematic s)
    {
        Schematic.Components.Clear(); Schematic.Components.AddRange(s.Components);
        Schematic.Wires.Clear(); Schematic.Wires.AddRange(s.Wires);
        Select(null); Done();
    }

    public PlacedComponent? Selected => _selected;
    void Select(PlacedComponent? c, Wire? w = null) { _selected = c; _selectedWire = w; SelectionChanged?.Invoke(); InvalidateVisual(); }

    public void RotateSelected()
    {
        if (_selected == null) { _ghostRotation = (_ghostRotation + 1) % 4; InvalidateVisual(); return; }
        Commit(); _selected.Rotation = (_selected.Rotation + 1) % 4; Done();
    }
    public void DeleteSelected()
    {
        if (_selected != null) { Commit(); Schematic.Components.Remove(_selected); Select(null); Done(); }
        else if (_selectedWire != null) { Commit(); Schematic.Wires.Remove(_selectedWire); Select(null); Done(); }
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
                var hit = Schematic.Components.LastOrDefault(c => HitTest(c, pos));
                var wire = hit == null ? Schematic.Wires.LastOrDefault(w => HitWire(w, pos)) : null;
                Select(hit, wire);
                if (hit != null) { _dragging = hit; _dragMoved = false; _dragOffset = new(g.X - hit.Position.X, g.Y - hit.Position.Y); Commit(); }
                break;
            case Tool.Place:
                Commit();
                var comp = new PlacedComponent { Ref = NextRef(PlaceKind), Kind = PlaceKind, Position = g, Rotation = _ghostRotation, Value = Schematic.DefaultValue(PlaceKind) };
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
        var pos = e.GetPosition(this); _inside = true;
        if (_panning) { _pan += pos - _panLast; _panLast = pos; }
        _cursor = Snap(pos);
        if (_dragging != null)
        {
            var np = new GridPoint(_cursor.X - _dragOffset.X, _cursor.Y - _dragOffset.Y);
            if (!np.Equals(_dragging.Position)) { _dragging.Position = np; _dragMoved = true; }
        }
        InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e) { _inside = false; InvalidateVisual(); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _panning = false;
        if (_dragging != null)
        {
            if (!_dragMoved) _undo.Pop(); // simple clic : pas d'entrée d'historique
            _dragging = null; Done();
        }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var pos = e.GetPosition(this); var before = (pos - _pan) / _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), 0.4, 6);
        _pan = pos - before * _zoom; InvalidateVisual();
    }
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
        foreach (var w in Schematic.Wires) ctx.DrawLine(w == _selectedWire ? new Pen(Accent, lw + 1) : wirePen, ToScreen(w.A), ToScreen(w.B));

        // Jonctions : point plein là où au moins 3 éléments se rejoignent (circuit et banc confondus).
        var allWires = AllWires.ToList();
        var ends = allWires.SelectMany(w => new[] { w.A, w.B }).Concat(AllComponents.SelectMany(c => c.Pins)).ToList();
        foreach (var p in ends.Distinct())
        {
            int n = ends.Count(q => q.Equals(p)) + allWires.Count(w => OnSeg(p, w)) * 2;
            if (n >= 3) ctx.DrawEllipse(layer ? BenchBrush : WireBrush, null, ToScreen(p), 3.2 * _zoom / 2 + 1, 3.2 * _zoom / 2 + 1);
        }

        foreach (var c in Schematic.Components) DrawComponent(ctx, c, c == _selected ? Accent : layer ? BenchBrush : c.Kind == ComponentKind.Label ? LabelBrush : Ink, 1);
        if (Caption.Length > 0)
        {
            var t = new FormattedText(Caption, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 12, layer ? BenchBrush : Muted);
            var box = new Rect(10, 10, t.Width + 16, t.Height + 8);
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse(layer ? "#f3effe" : "#f3f4f6")), new Pen(layer ? BenchBrush : Line, 1), box, 4, 4);
            ctx.DrawText(t, new Point(18, 14));
        }

        if (_inside && _tool == Tool.Place)
            DrawComponent(ctx, new PlacedComponent { Ref = Schematic.Prefix(PlaceKind) + "?", Kind = PlaceKind, Position = _cursor, Rotation = _ghostRotation, Value = "" }, Accent, 0.45);
        if (_wireStart is { } s)
        {
            var dash = new Pen(WireBrush, lw, new DashStyle([4, 3], 0));
            var a = s; var g = _cursor;
            if (a.X != g.X && a.Y != g.Y) { var m = new GridPoint(g.X, a.Y); ctx.DrawLine(dash, ToScreen(a), ToScreen(m)); ctx.DrawLine(dash, ToScreen(m), ToScreen(g)); }
            else ctx.DrawLine(dash, ToScreen(a), ToScreen(g));
        }
        if (_inside && _tool != Tool.Select)
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
        var valT = Text(Short(c.Value), 10, Muted);
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
