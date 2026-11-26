using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SkiaSharp;
namespace Silx.App;

/// <summary>
/// Export du schéma et des courbes en PDF (vectoriel, une page par vue), SVG (vectoriel) ou PNG (2× pour l'impression
/// et les documents). Le schéma est cadré en entier ; les courbes reprennent les graphiques du banc tels qu'affichés.
/// L'export se fait toujours en thème clair.
/// </summary>
public sealed partial class MainWindow
{
    public enum ContenuExport { Schema, Courbes, Tout }
    public const int LargeurExport = 1600, HauteurSchema = 1000, HauteurCourbe = 420;
    public Rect DernierExportSchema { get; private set; }

    /// <summary>Bouton « Exporter » : ce qu'on exporte, puis le fichier (le format suit l'extension choisie).</summary>
    void ProposerExport(Control ancre)
    {
        var sp = new StackPanel { Spacing = 2, Margin = new Thickness(4), Width = 230 };
        sp.Children.Add(Ui.Title("Exporter en PDF, SVG ou PNG"));
        var fly = new Flyout { Content = sp, Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var (c, nom) in new[] { (ContenuExport.Schema, "Le schéma"), (ContenuExport.Courbes, "Les courbes du banc"), (ContenuExport.Tout, "Le schéma et les courbes") })
        {
            if (c != ContenuExport.Schema && _banc == null) continue;
            var b = new Button { Content = nom, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4), Background = Brushes.Transparent };
            var cc = c; b.Click += (_, _) => { fly.Hide(); Lancer(() => ExporterAsync(cc), "export"); };
            sp.Children.Add(b);
        }
        if (_banc == null) sp.Children.Add(Ui.Hint("Choisissez un banc pour exporter aussi ses courbes."));
        fly.ShowAt(ancre);
    }

    async Task ExporterAsync(ContenuExport c)
    {
        var nom = (_projet?.Nom ?? "Silx") + (c == ContenuExport.Courbes ? " - courbes" : c == ContenuExport.Tout ? " - schéma et courbes" : " - schéma");
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exporter", SuggestedFileName = nom + ".pdf", DefaultExtension = "pdf",
            FileTypeChoices = [new("PDF") { Patterns = ["*.pdf"] }, new("SVG") { Patterns = ["*.svg"] }, new("PNG") { Patterns = ["*.png"] }]
        });
        if (f?.TryGetLocalPath() is not { } chemin) return;
        await ExporterVersAsync(chemin, c);
        _status.Text = $"Exporté : {chemin}";
    }

    /// <summary>Écrit l'export dans <paramref name="chemin"/> ; le format suit l'extension (.pdf, .svg, .png).</summary>
    public async Task ExporterVersAsync(string chemin, ContenuExport c)
    {
        var ext = Path.GetExtension(chemin).ToLowerInvariant();
        if (ext is not (".pdf" or ".svg" or ".png")) throw new ArgumentException($"Format non pris en charge : « {ext} » (PDF, SVG ou PNG).");
        bool schema = c != ContenuExport.Courbes, courbes = c != ContenuExport.Schema && _banc != null && _panes.Count > 0;
        bool sombre = Ui.Sombre;
        if (sombre) { Ui.AppliquerTheme(false); RefreshPlot(keepLimits: true); }   // documents : toujours en clair
        try
        {
            var vues = new List<(int H, Func<SKCanvas, Task> Dessiner)>();
            // PNG : le schéma est rendu à part en haute définition puis collé (le rendu Avalonia ignore la mise à l'échelle du canevas).
            if (schema) vues.Add((HauteurSchema, cv => DessinerSchema(cv, LargeurExport, HauteurSchema, ext == ".png" ? 2 : 0)));
            if (courbes) foreach (var p in _panes.ToList()) { var pp = p; vues.Add((HauteurCourbe, cv => { pp.Plot.Plot.Render(cv, LargeurExport, HauteurCourbe); return Task.CompletedTask; })); }
            if (vues.Count == 0) throw new InvalidOperationException("Rien à exporter.");
            await using var fs = File.Create(chemin);
            if (ext == ".pdf")
            {
                // Une page par vue (schéma, puis chaque graphique), en points PDF.
                using var doc = SKDocument.CreatePdf(fs, new SKDocumentPdfMetadata { Title = Path.GetFileNameWithoutExtension(chemin), Creator = "Silx", Producer = "Silx" });
                foreach (var (h, dessiner) in vues) { var cv = doc.BeginPage(LargeurExport, h); await dessiner(cv); doc.EndPage(); }
                doc.Close();
            }
            else
            {
                int total = vues.Sum(v => v.H);
                if (ext == ".svg")
                {
                    using (var cv = SKSvgCanvas.Create(new SKRect(0, 0, LargeurExport, total), fs)) await Empiler(cv, vues);
                }
                else
                {
                    const float echelle = 2;   // PNG net à l'impression
                    using var surf = SKSurface.Create(new SKImageInfo((int)(LargeurExport * echelle), (int)(total * echelle)));
                    surf.Canvas.Clear(SKColors.White); surf.Canvas.Scale(echelle);
                    await Empiler(surf.Canvas, vues);
                    using var img = surf.Snapshot(); using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                    data.SaveTo(fs);
                }
            }
        }
        finally { if (sombre) { Ui.AppliquerTheme(true); RefreshPlot(keepLimits: true); } }
    }

    static async Task Empiler(SKCanvas cv, List<(int H, Func<SKCanvas, Task> Dessiner)> vues)
    {
        int y = 0;
        foreach (var (h, dessiner) in vues)
        {
            cv.Save(); cv.Translate(0, y); cv.ClipRect(new SKRect(0, 0, LargeurExport, h)); await dessiner(cv); cv.Restore();
            y += h;
        }
    }

    /// <summary>Schéma entier (circuit, et calque du banc affiché par-dessus) cadré dans <paramref name="l"/>×<paramref name="h"/>.</summary>
    async Task DessinerSchema(SKCanvas cv, int l, int h, float matriciel = 0)
    {
        if (matriciel > 0)
        {
            // Haute définition : rendu Avalonia dans un bitmap à 96 × matriciel ppp, puis collé sur la page.
            var vue = CreerVueSchema(l, h);
            using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(l * matriciel), (int)(h * matriciel)), new Vector(96 * matriciel, 96 * matriciel));
            rtb.Render(vue);
            using var ms = new MemoryStream(); rtb.Save(ms); ms.Position = 0;
            using var img = SKImage.FromEncodedData(ms);
            cv.DrawImage(img, new SKRect(0, 0, l, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            return;
        }
        await DessinerSchema(cv, l, h, 0, l);
    }

    /// <summary>Vue hors écran du schéma (circuit, et calque du banc affiché), cadrée pour occuper la page.</summary>
    SchematicCanvas CreerVueSchema(int l, int h)
    {
        var vue = new SchematicCanvas { Width = l, Height = h };
        if (_banc != null) vue.Load(_banc.Calque, _etude.Schema); else vue.Load(_etude.Schema);
        if (_banc != null) { vue.Caption = $"Banc « {_banc.Nom} »"; vue.CouleurCalque = CouleurDe(_banc); }
        vue.Measure(new Size(l, h)); vue.Arrange(new Rect(0, 0, l, h));
        vue.FitAll(zoomMax: 6);
        return vue;
    }

    /// <summary>Rendu vectoriel du schéma sur <paramref name="cv"/> (pixels <paramref name="l"/>×<paramref name="h"/>, mise en page pour <paramref name="logique"/> de large).</summary>
    async Task DessinerSchema(SKCanvas cv, int l, int h, int _, int logique)
    {
        double k = (double)l / logique; int lw = logique, lh = (int)(h / k);
        l = lw; h = lh;
        var vue = CreerVueSchema(l, h);
        cv.DrawRect(new SKRect(0, 0, l, h), new SKPaint { Color = SKColors.White });
        await Avalonia.Skia.Helpers.DrawingContextHelper.RenderAsync(cv, vue, new Rect(0, 0, l, h), new Vector(96 * k, 96 * k));
        DernierExportSchema = vue.Bounds;
    }
}
