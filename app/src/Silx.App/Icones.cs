using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
namespace Silx.App;

/// <summary>Icônes au trait (grille 24×24), dessinées pour Silx.</summary>
static class Icones
{
    public const string Nouveau = "M14 3H6v18h12V7z M14 3v4h4 M12 10v7 M8.5 13.5h7";
    public const string Exemple = "M9 3h6 M10 3v6L5 18.5A1.7 1.7 0 0 0 6.5 21h11a1.7 1.7 0 0 0 1.5-2.5L14 9V3 M7.3 15h9.4";
    public const string Selection = "M5 3l13 7.5-5.6 1.6-2.4 5.6z";
    public const string Fil = "M3 18h7V6h11 M10 18h0";
    public const string Pivoter = "M20 12a8 8 0 1 1-2.4-5.7 M20 3v5h-5";
    public const string Supprimer = "M4 7h16 M10 7V4h4v3 M6 7l1 14h10l1-14 M10 11v6 M14 11v6";
    public const string Annuler = "M9 14L4 9l5-5 M4 9h11a5 5 0 0 1 0 10h-4";
    public const string Retablir = "M15 14l5-5-5-5 M20 9H9a5 5 0 0 0 0 10h4";
    public const string PanneauGauche = "M3 4h18v16H3z M9 4v16";
    public const string PanneauDroit = "M3 4h18v16H3z M15 4v16";
    public const string Lecture = "M7 4l13 8-13 8z";
    public const string ModeSimulation = "M2 12c2.5-7 5-7 7.5 0s5 7 7.5 0c1.2-3 3-4 5-4";
    public const string ModeTest = "M3 5h18 M3 19h18 M3 13c3-6 6 2 9-1s5-3 9 0 M6 5l-2 2 M10 5l-2 2 M14 5l-2 2 M18 5l-2 2";
    public const string Temporel = "M3 19h5V5h13";
    public const string Bode = "M3 7h9c4 0 5 4 9 13";
    public const string LimiteHaute = "M3 13h6l4-4h8 M5 13l3-3 M9 13l3-3 M14 9l3-3 M18 9l3-3";
    public const string LimiteBasse = "M3 11h6l4 4h8 M6 11l-3 3 M10 12l-3 3 M14 15l-3 3 M19 15l-3 3";
    public const string Croix = "M7 7l10 10 M17 7L7 17";
    public const string Plus = "M12 5v14 M5 12h14";
    public const string Rapport = "M6 3h9l4 4v14H6z M14 3v5h5 M9 17v-3 M12 17v-6 M15 17v-4";
    public const string Explorateur = "M4 5h6 M4 5v14 M4 11h5 M4 17h5 M12 5h8 M12 11h8 M12 17h8";
    public const string Chercher = "M4 10.5a6.5 6.5 0 1 0 13 0a6.5 6.5 0 1 0-13 0 M15.5 15.5L21 21";
    public const string Aide = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0 M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .9-1 1.6v.6 M12 17.5v.01";
    public const string Reglages = "M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6z M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9l2.1 2.1 M17 17l2.1 2.1 M4.9 19.1L7 17 M17 7l2.1-2.1";
    public const string VueSchema = "M3 4h18v16H3z M7 12h3l2-3 2 6 2-3h1";
    public const string VueMixte = "M3 4h18v16H3z M3 11h18 M7 8h4 M6 17c2-4 4 2 6-2s3 1 5 0";
    public const string VueCourbes = "M3 4h18v16H3z M5 16c3-8 5 4 8-2s4-3 6-2";
    public const string Miroir = "M12 3v18 M9 7L4 12l5 5z M15 7l5 5-5 5z";
    public const string Dupliquer = "M8 8h12v12H8z M4 16V4h12";
    public const string Ouvrir = "M3 6h6l2 2h10v11H3z M3 10h18";
    public const string Enregistrer = "M5 3h11l3 3v15H5z M8 3v5h7V3 M8 21v-7h8v7";
    public const string OutilBroche = "M3 12h9 M14 12a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0-5 0";
    public const string OutilLigne = "M5 19L19 5";
    public const string OutilRectangle = "M4 6h16v12H4z";
    public const string OutilTriangle = "M6 4v16l13-8z";
    public const string OutilCercle = "M4 12a8 8 0 1 0 16 0a8 8 0 1 0-16 0";
    public const string OutilArc = "M4 17a8 8 0 0 1 16 0";
    public const string TexteModele = "M6 3h9l4 4v14H6z M9 10h7 M9 13h7 M9 16h5";
    public const string Crayon = "M4 20l4-1 11-11-3-3L5 16z M14 7l3 3";
    public const string Sonde = "M3 21l5-5 M8 16l2 2 6-6-4-4-6 6z M14 6l3-3 4 4-3 3";
    public const string Graphique = "M3 4h18v7H3z M3 13h18v7H3z M6 9l3-3 3 2 4-3 M6 18l3-2 3 1 4-3";
    public const string ZoomPlus = "M3 10.5a7.5 7.5 0 1 0 15 0a7.5 7.5 0 1 0-15 0 M16 16l5 5 M10.5 7.5v6 M7.5 10.5h6";
    public const string ZoomMoins = "M3 10.5a7.5 7.5 0 1 0 15 0a7.5 7.5 0 1 0-15 0 M16 16l5 5 M7.5 10.5h6";
    public const string ToutVoir = "M4 9V4h5 M15 4h5v5 M20 15v5h-5 M9 20H4v-5 M9 12h6 M12 9v6";
    public const string ZoomFenetre = "M3 13V3h10 M3 7v0 M12.5 15a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0-7 0 M18.5 17.5l3 3 M3 17v1 M3 21h1 M7 21h1 M17 3h1 M21 3v1 M21 7v1";
    public const string Curseurs = "M8 3v18 M16 3v18 M8 9a1.8 1.8 0 1 0 0.01 0 M16 15a1.8 1.8 0 1 0 0.01 0";

    public const string Symbole = "M7 5h10v14H7z M3 8h4 M3 12h4 M3 16h4 M17 8h4 M17 16h4";
    public const string Relier = "M10 14a4 4 0 0 0 5.6 0l3-3a4 4 0 0 0-5.6-5.6l-1 1 M14 10a4 4 0 0 0-5.6 0l-3 3a4 4 0 0 0 5.6 5.6l1-1";
    public const string AjouterBroche = "M5 4h8v16H5z M13 9h4 M19 13v6 M16 16h6";
    public const string ImporterSpice = "M14 3H6v18h12V7z M14 3v4h4 M12 10v7 M9 14l3 3 3-3";
    public const string EditerSymbole = "M4 5h9v12H4z M1 8h3 M1 14h3 M13 8h2 M14 20l6-6 2 2-6 6h-2z";

    public static Control Make(string data, double size = 18, IBrush? brush = null, bool filled = false) => new Avalonia.Controls.Shapes.Path
    {
        Data = Geometry.Parse(data), Width = size, Height = size, Stretch = Stretch.Uniform,
        Stroke = filled ? null : brush ?? Ui.Ink, Fill = filled ? brush ?? Ui.Ink : null,
        StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>Bouton-icône carré avec info-bulle (le nom et le raccourci y sont).</summary>
    public static Button Button(string data, string tip, Action click, double size = 18)
    {
        var b = new Button { Content = Make(data, size), Padding = new Thickness(7), Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, tip); ToolTip.SetShowOnDisabled(b, true);
        b.Click += (_, _) => click();
        return b;
    }
}
