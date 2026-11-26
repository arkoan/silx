using SkiaSharp;

// Icône Silx : carré arrondi bleu en dégradé, trace d'oscilloscope (échelon amorti) blanche et grille discrète.
// Usage : dotnet run -- sortie.png [taille]
var sortie = args.Length > 0 ? args[0] : "icone.png";
int n = args.Length > 1 ? int.Parse(args[1]) : 1024;
using var surface = SKSurface.Create(new SKImageInfo(n, n, SKColorType.Rgba8888, SKAlphaType.Premul));
var c = surface.Canvas; c.Clear(SKColors.Transparent);
float m = n * 0.09f, r = n * 0.2f;   // marge et arrondi façon macOS
var cadre = new SKRect(m, m, n - m, n - m);
using (var fond = new SKPaint { IsAntialias = true, Shader = SKShader.CreateLinearGradient(new(m, m), new(n - m, n - m),
           [new SKColor(0x25, 0x63, 0xeb), new SKColor(0x7c, 0x3a, 0xed)], SKShaderTileMode.Clamp) })
    c.DrawRoundRect(cadre, r, r, fond);
using (var grille = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 40), StrokeWidth = n * 0.006f, Style = SKPaintStyle.Stroke })
    for (int i = 1; i < 5; i++)
    {
        float x = m + (n - 2 * m) * i / 5f, y = m + (n - 2 * m) * i / 5f;
        c.DrawLine(x, m + r * 0.4f, x, n - m - r * 0.4f, grille); c.DrawLine(m + r * 0.4f, y, n - m - r * 0.4f, y, grille);
    }
// Réponse d'un circuit du second ordre à un échelon : 1 − e^(−ζωt)·cos(ωt).
using var trace = new SKPath();
float x0 = m + n * 0.08f, x1 = n - m - n * 0.08f, yBas = n * 0.70f, yHaut = n * 0.36f;
for (int i = 0; i <= 400; i++)
{
    double t = i / 400.0, v = t < 0.12 ? 0 : 1 - Math.Exp(-5 * (t - 0.12)) * Math.Cos(22 * (t - 0.12));
    float x = x0 + (float)t * (x1 - x0), y = yBas - (float)v * (yBas - yHaut);
    if (i == 0) trace.MoveTo(x, y); else trace.LineTo(x, y);
}
using (var p = new SKPaint { IsAntialias = true, Color = SKColors.White, StrokeWidth = n * 0.035f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round })
    c.DrawPath(trace, p);
using var img = surface.Snapshot(); using var data = img.Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes(sortie, data.ToArray());
Console.WriteLine($"{sortie} ({n} px)");
