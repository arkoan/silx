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
            ctx.DrawGeometry(new SolidColorBrush(Color.Parse("#fff7ed")), new Pen(brush, 1), geo);
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
                    // Symbole IEC : rectangle.
                    ctx.DrawLine(pen, At(0), At(.22)); ctx.DrawLine(pen, At(.78), At(1));
                    var poly = new[] { At(.22, h * .38), At(.78, h * .38), At(.78, -h * .38), At(.22, -h * .38), At(.22, h * .38) };
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

    /// <summary>Contrôle qui affiche un symbole centré (icônes de la palette).</summary>
    public sealed class Icone : Avalonia.Controls.Control
    {
        public required ComponentKind Kind { get; init; }
        public IBrush Brush { get; set; } = new SolidColorBrush(Color.Parse("#1c1f26"));
        public override void Render(DrawingContext ctx)
        {
            var w = Bounds.Width; var h = Bounds.Height; var pen = new Pen(Brush, 1.5);
            FormattedText T(string s, double size, IBrush br, FontWeight fw) => new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, fw), 9, br);
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
