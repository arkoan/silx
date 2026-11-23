using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Silx.Core;
namespace Silx.Editor;

public enum Tool { Select, Wire, Resistor, Capacitor, Inductor, VoltageSource, Ground }

/// <summary>Canevas de schéma : grille, snap, zoom/pan, placement, fils, rotation, suppression, undo/redo.</summary>
public sealed class SchematicCanvas : Control
{
    public const int Grid = 12;             // pixels par pas de grille à zoom 1
    public Schematic Schematic { get; private set; } = new();
    public Tool Tool { get; set; } = Tool.Select;
    public event Action? Changed;

    readonly Stack<Schematic> _undo = new(), _redo = new();
    double _zoom = 1.5; Point _pan = new(40, 40);
    GridPoint _cursor; GridPoint? _wireStart; bool _panning; Point _panLast;
    PlacedComponent? _selected; PlacedComponent? _dragging; GridPoint _dragOffset;
    int _ghostRotation;

    public SchematicCanvas() { Focusable = true; ClipToBounds = true; }

    public void Load(Schematic s) { Schematic = s; _undo.Clear(); _redo.Clear(); _selected = null; Changed?.Invoke(); InvalidateVisual(); }

    // ---- transformations ----
    Point ToScreen(GridPoint p) => new(p.X * Grid * _zoom + _pan.X, p.Y * Grid * _zoom + _pan.Y);
    GridPoint Snap(Point s) => new((int)Math.Round((s.X - _pan.X) / (Grid * _zoom)), (int)Math.Round((s.Y - _pan.Y) / (Grid * _zoom)));

    // ---- undo/redo : HYPOTHÈSE v1 = instantanés complets (suffisant pour des schémas de quelques centaines d'éléments) ----
    void Commit() { _undo.Push(Schematic.Clone()); _redo.Clear(); Changed?.Invoke(); InvalidateVisual(); }
    public void Undo() { if (_undo.Count == 0) return; _redo.Push(Schematic.Clone()); Restore(_undo.Pop()); }
    public void Redo() { if (_redo.Count == 0) return; _undo.Push(Schematic.Clone()); Restore(_redo.Pop()); }
    void Restore(Schematic s)
    {
        Schematic.Components.Clear(); Schematic.Components.AddRange(s.Components);
        Schematic.Wires.Clear(); Schematic.Wires.AddRange(s.Wires);
        _selected = null; Changed?.Invoke(); InvalidateVisual();
    }
    public void RotateSelected() { if (_selected == null) { _ghostRotation = (_ghostRotation + 1) % 4; InvalidateVisual(); return; } Commit(); _selected.Rotation = (_selected.Rotation + 1) % 4; Changed?.Invoke(); InvalidateVisual(); }
    public void DeleteSelected() { if (_selected == null) return; Commit(); Schematic.Components.Remove(_selected); _selected = null; Changed?.Invoke(); InvalidateVisual(); }
    public PlacedComponent? Selected => _selected;
    public void EditSelectedValue(string v) { if (_selected == null) return; Commit(); _selected.Value = v; Changed?.Invoke(); InvalidateVisual(); }

    // ---- entrées ----
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var pt = e.GetCurrentPoint(this); var pos = pt.Position; var g = Snap(pos);
        if (pt.Properties.IsMiddleButtonPressed || pt.Properties.IsRightButtonPressed) { _panning = true; _panLast = pos; return; }
        if (Tool == Tool.Wire)
        {
            if (_wireStart is null) _wireStart = g;
            else
            {
                var a = _wireStart.Value;
                // HYPOTHÈSE : fils en L (horizontal puis vertical) pour garder un schéma orthogonal.
                var segs = new List<Wire>();
                if (a.X != g.X && a.Y != g.Y) { var m = new GridPoint(g.X, a.Y); segs.Add(new(a, m)); segs.Add(new(m, g)); }
                else if (!a.Equals(g)) segs.Add(new(a, g));
                if (segs.Count > 0) { Commit(); Schematic.Wires.AddRange(segs); }
                _wireStart = e.ClickCount > 1 ? null : g; // double-clic = fin de tracé
            }
        }
        else if (Tool == Tool.Select)
        {
            _selected = Schematic.Components.LastOrDefault(c => HitTest(c, pos));
            if (_selected != null) { _dragging = _selected; _dragOffset = new(g.X - _selected.Position.X, g.Y - _selected.Position.Y); _undo.Push(Schematic.Clone()); _redo.Clear(); }
            Changed?.Invoke();
        }
        else
        {
            var kind = Tool switch { Tool.Resistor => ComponentKind.Resistor, Tool.Capacitor => ComponentKind.Capacitor, Tool.Inductor => ComponentKind.Inductor, Tool.VoltageSource => ComponentKind.VoltageSource, _ => ComponentKind.Ground };
            Commit();
            Schematic.Components.Add(new() { Ref = Schematic.NextRef(kind), Kind = kind, Position = g, Rotation = _ghostRotation, Value = Schematic.DefaultValue(kind) });
            Changed?.Invoke();
        }
        InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (_panning) { _pan += pos - _panLast; _panLast = pos; }
        _cursor = Snap(pos);
        if (_dragging != null) { _dragging.Position = new(_cursor.X - _dragOffset.X, _cursor.Y - _dragOffset.Y); }
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _panning = false;
        if (_dragging != null) { _dragging = null; Changed?.Invoke(); }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var pos = e.GetPosition(this); var before = (pos - _pan) / _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.3, 6);
        _pan = pos - before * _zoom; InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.R) RotateSelected();
        else if (e.Key is Key.Delete or Key.Back) DeleteSelected();
        else if (e.Key == Key.Escape) { _wireStart = null; Tool = Tool.Select; }
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control)) Undo();
        else if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control)) Redo();
        InvalidateVisual();
    }

    bool HitTest(PlacedComponent c, Point p)
    {
        var pins = c.Pins; var a = ToScreen(pins[0]); var b = pins.Count > 1 ? ToScreen(pins[1]) : a;
        var r = new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)).Inflate(Grid * _zoom * 0.8);
        return r.Contains(p);
    }

    // ---- rendu ----
    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        var step = Grid * _zoom;
        var dot = new SolidColorBrush(Color.FromRgb(200, 200, 205));
        double x0 = _pan.X % step, y0 = _pan.Y % step;
        for (double x = x0; x < Bounds.Width; x += step) for (double y = y0; y < Bounds.Height; y += step)
            ctx.FillRectangle(dot, new Rect(x - 0.7, y - 0.7, 1.4, 1.4));

        var wirePen = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 60)), 2 * _zoom / 1.5);
        foreach (var w in Schematic.Wires) ctx.DrawLine(wirePen, ToScreen(w.A), ToScreen(w.B));

        var conn = Connectivity.Build(Schematic);
        foreach (var c in Schematic.Components) DrawComponent(ctx, c, c == _selected, conn);
        // Jonctions : point plein quand ≥3 éléments se rejoignent est une amélioration ; ici, points aux extrémités de fils.
        foreach (var p in Schematic.Wires.SelectMany(w => new[] { w.A, w.B }).GroupBy(p => p).Where(g => g.Count() >= 3).Select(g => g.Key))
            ctx.DrawEllipse(wirePen.Brush, null, ToScreen(p), 3.5, 3.5);

        if (_wireStart is { } s) ctx.DrawLine(new Pen(Brushes.Gray, 1, DashStyle.Dash), ToScreen(s), ToScreen(_cursor));
        ctx.DrawEllipse(null, new Pen(Brushes.OrangeRed, 1.5), ToScreen(_cursor), 4, 4);
    }

    static readonly Typeface Face = new("Inter, Segoe UI, Arial");
    void DrawComponent(DrawingContext ctx, PlacedComponent c, bool sel, Connectivity conn)
    {
        var pen = new Pen(sel ? Brushes.DodgerBlue : Brushes.Black, 2 * _zoom / 1.5);
        var pins = c.Pins; var a = ToScreen(pins[0]);
        if (c.Kind == ComponentKind.Ground)
        {
            // symbole masse tourné selon c.Rotation : on le dessine vers "le bas" avant rotation
            var d = _zoom * Grid;
            var rot = Matrix.CreateRotation(c.Rotation * Math.PI / 2);
            Point R(double x, double y) { var v = rot.Transform(new Point(x, y)); return new(a.X + v.X, a.Y + v.Y); }
            ctx.DrawLine(pen, R(0, 0), R(0, d * .6));
            ctx.DrawLine(pen, R(-d * .6, d * .6), R(d * .6, d * .6));
            ctx.DrawLine(pen, R(-d * .4, d * .9), R(d * .4, d * .9));
            ctx.DrawLine(pen, R(-d * .2, d * 1.2), R(d * .2, d * 1.2));
            return;
        }
        var b = ToScreen(pins[1]);
        var dir = b - a; var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y); var u = new Point(dir.X / len, dir.Y / len); var n = new Point(-u.Y, u.X);
        Point At(double t, double off = 0) => new(a.X + u.X * len * t + n.X * off, a.Y + u.Y * len * t + n.Y * off);
        double h = Grid * _zoom * 0.7;
        switch (c.Kind)
        {
            case ComponentKind.Resistor:
                {
                    ctx.DrawLine(pen, At(0), At(.2)); ctx.DrawLine(pen, At(.8), At(1));
                    var pts = new List<Point> { At(.2) };
                    for (int i = 0; i < 6; i++) pts.Add(At(.2 + .6 * (i + .5) / 6, i % 2 == 0 ? h * .6 : -h * .6));
                    pts.Add(At(.8));
                    for (int i = 0; i < pts.Count - 1; i++) ctx.DrawLine(pen, pts[i], pts[i + 1]);
                    break;
                }
            case ComponentKind.Capacitor:
                ctx.DrawLine(pen, At(0), At(.45)); ctx.DrawLine(pen, At(.55), At(1));
                ctx.DrawLine(pen, At(.45, h), At(.45, -h)); ctx.DrawLine(pen, At(.55, h), At(.55, -h)); break;
            case ComponentKind.Inductor:
                {
                    ctx.DrawLine(pen, At(0), At(.2)); ctx.DrawLine(pen, At(.8), At(1));
                    for (int i = 0; i < 4; i++) { var m = At(.2 + .6 * (i + .5) / 4, -h * .6); ctx.DrawEllipse(null, pen, m, len * .6 / 8, h * .6); }
                    break;
                }
            case ComponentKind.VoltageSource:
                ctx.DrawLine(pen, At(0), At(.3)); ctx.DrawLine(pen, At(.7), At(1));
                ctx.DrawEllipse(null, pen, At(.5), len * .2, len * .2);
                var t = new FormattedText("+", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11 * _zoom / 1.5, Brushes.Black);
                ctx.DrawText(t, new Point(At(.15, -h * .9).X - 3, At(.15, -h * .9).Y - 7)); break;
        }
        ctx.DrawEllipse(Brushes.Black, null, a, 2.5, 2.5); ctx.DrawEllipse(Brushes.Black, null, b, 2.5, 2.5);
        var label = new FormattedText($"{c.Ref}\n{c.Value}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 10.5 * _zoom / 1.5, Brushes.DarkSlateGray);
        var lp = At(.5, h * 1.3); ctx.DrawText(label, new Point(lp.X - 14, lp.Y));
    }
}
