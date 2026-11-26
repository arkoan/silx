using Avalonia;
using Avalonia.Media;
using Silx.Etude;
namespace Silx.App;

/// <summary>Dessin des symboles (partagé par le canevas et les icônes de la palette).</summary>
public static class Symboles
{
    /// <param name="a">broche 1 (écran)</param><param name="b">broche 2 (écran), ignorée pour masse et étiquette</param>
    /// <param name="d">taille d'un pas de grille en pixels</param>
    public static void Draw(DrawingContext ctx, ComponentKind kind, int rotation, string value, Point a, Point b, double d, IBrush brush, Pen pen,
        Func<string, double, IBrush, FontWeight, FormattedText> text)
    {
        if (kind == ComponentKind.Ground)
        {
            var rot = Matrix.CreateRotation(rotation * Math.PI / 2);
            Point R(double x, double y) { var v = rot.Transform(new Point(x, y)); return new(a.X + v.X, a.Y + v.Y); }
            ctx.DrawLine(pen, R(0, 0), R(0, d * .5));
            ctx.DrawLine(pen, R(-d * .6, d * .5), R(d * .6, d * .5));
            ctx.DrawLine(pen, R(-d * .38, d * .8), R(d * .38, d * .8));
            ctx.DrawLine(pen, R(-d * .16, d * 1.1), R(d * .16, d * 1.1));
            return;
        }
        if (kind == ComponentKind.Label)
        {
            var t = text(value.Length > 0 ? value : "?", 11, brush, FontWeight.SemiBold);
            var top = new Point(a.X, a.Y - d * .9);
            ctx.DrawLine(pen, a, top);
            var box = new Rect(top.X - 2, top.Y - t.Height - 2, t.Width + 10, t.Height + 4);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(box.Left, box.Bottom), true);
                g.LineTo(new Point(box.Left, box.Top)); g.LineTo(new Point(box.Right - 5, box.Top));
                g.LineTo(new Point(box.Right, box.Center.Y)); g.LineTo(new Point(box.Right - 5, box.Bottom)); g.EndFigure(true);
            }
            ctx.DrawGeometry(Ui.LabelFill, new Pen(brush, 1), geo);
            ctx.DrawText(t, new Point(box.Left + 3, box.Top + 2));
            ctx.DrawEllipse(brush, null, a, 2.5, 2.5);
            return;
        }
        var dir = b - a; var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y); var u = new Point(dir.X / len, dir.Y / len); var n = new Point(-u.Y, u.X);
        Point At(double t, double off = 0) => new(a.X + u.X * len * t + n.X * off, a.Y + u.Y * len * t + n.Y * off);
        double h = d * 0.7;
        switch (kind)
        {
            case ComponentKind.Resistor:
                {
                    // Symbole IEC : rectangle de proportions 1 × 2,5 (1,5 pas × 0,6 pas) sur un dipôle de 3 pas.
                    ctx.DrawLine(pen, At(0), At(.25)); ctx.DrawLine(pen, At(.75), At(1));
                    var poly = new[] { At(.25, h * .43), At(.75, h * .43), At(.75, -h * .43), At(.25, -h * .43), At(.25, h * .43) };
                    for (int i = 0; i < 4; i++) ctx.DrawLine(pen, poly[i], poly[i + 1]);
                    break;
                }
            case ComponentKind.Capacitor:
                ctx.DrawLine(pen, At(0), At(.44)); ctx.DrawLine(pen, At(.56), At(1));
                ctx.DrawLine(pen, At(.44, h), At(.44, -h)); ctx.DrawLine(pen, At(.56, h), At(.56, -h)); break;
            case ComponentKind.Inductor:
                {
                    ctx.DrawLine(pen, At(0), At(.2)); ctx.DrawLine(pen, At(.8), At(1));
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(At(.2), false);
                        for (int i = 0; i < 4; i++)
                            g.ArcTo(At(.2 + .15 * (i + 1)), new Size(len * .075, len * .075), 0, false, SweepDirection.Clockwise);
                        g.EndFigure(false);
                    }
                    ctx.DrawGeometry(null, pen, geo); break;
                }
            case ComponentKind.VoltageSource:
            case ComponentKind.CurrentSource:
                {
                    ctx.DrawLine(pen, At(0), At(.3)); ctx.DrawLine(pen, At(.7), At(1));
                    ctx.DrawEllipse(null, pen, At(.5), len * .2, len * .2);
                    if (kind == ComponentKind.VoltageSource)
                    {
                        var s = len * .05;
                        ctx.DrawLine(pen, At(.38, -s), At(.38, s)); ctx.DrawLine(pen, At(.38 - .05, 0), At(.38 + .05, 0)); // +
                        ctx.DrawLine(pen, At(.62, -s), At(.62, s)); // −
                    }
                    else
                    {
                        // Courant conventionnel de la broche 1 vers la broche 2 à travers la source (convention SPICE).
                        ctx.DrawLine(pen, At(.38), At(.62)); ctx.DrawLine(pen, At(.62), At(.55, len * .05)); ctx.DrawLine(pen, At(.62), At(.55, -len * .05));
                    }
                    break;
                }
            case ComponentKind.Diode:
                {
                    ctx.DrawLine(pen, At(0), At(.38)); ctx.DrawLine(pen, At(.62), At(1));
                    var geo = new StreamGeometry();
                    using (var g = geo.Open()) { g.BeginFigure(At(.38, h * .55), true); g.LineTo(At(.62)); g.LineTo(At(.38, -h * .55)); g.EndFigure(true); }
                    ctx.DrawGeometry(brush, pen, geo);
                    ctx.DrawLine(pen, At(.62, h * .55), At(.62, -h * .55)); break;
                }
        }
    }

    /// <summary>Repère d'un symbole importé : pas de grille (réels, repère du symbole) → écran, rotation comprise.</summary>
    public static Func<double, double, Point> Repere(Point origine, int rotation, double d, bool miroir = false) => (x, y) =>
    {
        if (miroir) x = -x;
        for (int i = 0; i < ((rotation % 4) + 4) % 4; i++) (x, y) = (-y, x);
        return new Point(origine.X + x * d, origine.Y + y * d);
    };

    /// <summary>
    /// Dessine le symbole d'un composant SPICE importé : primitives, corps de boîte avec ses traits de broche et noms,
    /// points de broche. <paramref name="nomsBroches"/> affiche le nom de la broche du modèle à côté de chaque broche (éditeur).
    /// </summary>
    public static void DessinerSymbole(DrawingContext ctx, Silx.Etude.Symbole s, Func<double, double, Point> map, double d, IBrush brush, Pen pen,
        Func<string, double, IBrush, FontWeight, FormattedText> text, bool nomsBroches = false)
    {
        foreach (var t in s.Traits)
            switch (t.Forme)
            {
                case FormePrimitive.Trait: ctx.DrawLine(pen, map(t.X1, t.Y1), map(t.X2, t.Y2)); break;
                case FormePrimitive.Fleche:
                    {
                        var a = map(t.X1, t.Y1); var b = map(t.X2, t.Y2);
                        ctx.DrawLine(pen, a, b);
                        var v = b - a; var l = Math.Sqrt(v.X * v.X + v.Y * v.Y); if (l < 1e-6) break;
                        var u = new Point(v.X / l, v.Y / l); var n = new Point(-u.Y, u.X); var h = Math.Min(d * 0.45, l * 0.35); var m = new Point(b.X - u.X * h * 0.2, b.Y - u.Y * h * 0.2);
                        var geo = new StreamGeometry();
                        using (var g = geo.Open())
                        {
                            g.BeginFigure(m, true);
                            g.LineTo(new Point(m.X - u.X * h + n.X * h * .45, m.Y - u.Y * h + n.Y * h * .45));
                            g.LineTo(new Point(m.X - u.X * h - n.X * h * .45, m.Y - u.Y * h - n.Y * h * .45)); g.EndFigure(true);
                        }
                        ctx.DrawGeometry(brush, null, geo); break;
                    }
                case FormePrimitive.Cercle: ctx.DrawEllipse(null, pen, map(t.X1, t.Y1), t.X2 * d, t.X2 * d); break;
                case FormePrimitive.Arc:
                    {
                        var pts = t.PointsArc().Select(q => map(q.X, q.Y)).ToList();
                        for (int i = 1; i < pts.Count; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
                        break;
                    }
                case FormePrimitive.Rectangle: ctx.DrawRectangle(null, pen, new Rect(map(t.X1, t.Y1), map(t.X2, t.Y2)).Normalize()); break;
                case FormePrimitive.Polygone when t.Points is { Count: > 1 } pts:
                    {
                        var geo = new StreamGeometry();
                        using (var g = geo.Open()) { g.BeginFigure(map(pts[0][0], pts[0][1]), true); foreach (var p in pts.Skip(1)) g.LineTo(map(p[0], p[1])); g.EndFigure(true); }
                        ctx.DrawGeometry(t.Plein ? brush : null, pen, geo); break;
                    }
                case FormePrimitive.Texte when t.Texte != null:
                    { var ft = text(t.Texte, 9, brush, FontWeight.Normal); ctx.DrawText(ft, map(t.X1, t.Y1)); break; }
            }
        if (s.Corps is { } c)
        {
            ctx.DrawRectangle(Ui.SymbolFill, pen, new Rect(map(c[0], c[1]), map(c[2], c[3])).Normalize());
            foreach (var b in s.Broches)
            {
                // Trait de la broche jusqu'au bord du corps, et nom à l'intérieur.
                double ex = Math.Clamp(b.X, c[0], c[2]), ey = Math.Clamp(b.Y, c[1], c[3]);
                bool dedans = b.X > c[0] && b.X < c[2] && b.Y > c[1] && b.Y < c[3];
                if (!dedans) ctx.DrawLine(pen, map(b.X, b.Y), map(ex, ey));
                if (nomsBroches || (b.BrocheModele == null && string.IsNullOrWhiteSpace(b.Nom))) continue;
                var nom = !string.IsNullOrWhiteSpace(b.Nom) ? b.Nom.Trim() : b.BrocheModele!;
                var ft = text(nom, 8.5, brush, FontWeight.Normal);
                var pe = map(ex, ey);
                double cx = (c[0] + c[2]) / 2, cy = (c[1] + c[3]) / 2;
                var vers = map(ex + Math.Sign(cx - ex) * 0.35, ey + Math.Sign(cy - ey) * 0.35);
                // Placement : côté intérieur du point d'entrée, texte centré verticalement sur la broche.
                double tx = vers.X < pe.X - 0.5 ? vers.X - ft.Width : vers.X > pe.X + 0.5 ? vers.X : vers.X - ft.Width / 2;
                double ty = vers.Y < pe.Y - 0.5 ? vers.Y - ft.Height : vers.Y > pe.Y + 0.5 ? vers.Y : vers.Y - ft.Height / 2;
                ctx.DrawText(ft, new Point(tx, ty));
            }
        }
        foreach (var b in s.Broches) ctx.DrawEllipse(brush, null, map(b.X, b.Y), 2, 2);
    }

    /// <summary>Contrôle qui affiche un symbole centré (icônes de la palette).</summary>
    public sealed class Icone : Avalonia.Controls.Control
    {
        public required ComponentKind Kind { get; init; }
        /// <summary>Symbole d'un composant SPICE importé (Kind = Spice).</summary>
        public Silx.Etude.Symbole? Symbole { get; set; }
        public IBrush Brush { get; set; } = Ui.Ink;
        public override void Render(DrawingContext ctx)
        {
            var w = Bounds.Width; var h = Bounds.Height; var pen = new Pen(Brush, 1.5);
            FormattedText T(string s, double size, IBrush br, FontWeight fw) => new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, fw), 9, br);
            if (Kind == ComponentKind.Spice && Symbole is { } sy)
            {
                var (x0, y0, x1, y1) = sy.Encombrement();
                var dd = Math.Min(w / Math.Max(x1 - x0, 1), h / Math.Max(y1 - y0, 1)) * 0.9;
                var o = new Point(w / 2 - (x0 + x1) / 2 * dd, h / 2 - (y0 + y1) / 2 * dd);
                DessinerSymbole(ctx, sy, Repere(o, 0, dd), dd, Brush, new Pen(Brush, 1.2), T, nomsBroches: true);
                return;
            }
            switch (Kind)
            {
                case ComponentKind.Ground: Draw(ctx, Kind, 0, "", new Point(w / 2, h * .2), default, h * .45, Brush, pen, T); break;
                case ComponentKind.Label: Draw(ctx, Kind, 0, "IN", new Point(w * .3, h * .82), default, h * .5, Brush, new Pen(Brush, 1.2), T); break;
                default:
                    var m = Math.Min(w, h * 2);
                    Draw(ctx, Kind, 0, "", new Point(w / 2 - m * .45, h / 2), new Point(w / 2 + m * .45, h / 2), Math.Min(h * .5, m * .4), Brush, pen, T); break;
            }
        }
    }
}
