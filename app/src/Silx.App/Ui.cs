using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Silx.Mesures.Gabarits;
namespace Silx.App;

/// <summary>Petits constructeurs de contrôles et palette de couleurs de l'application.</summary>
static class Ui
{
    // Échelle de texte unique : titres 14, onglets 13, texte courant 12.5, aides 11.5.
    public const double Titre = 14, Onglet = 13, Texte = 12.5, Aide = 11.5;
    // Couleurs : un seul bleu d'accent (sélection, actions, mode) ; violet réservé aux bancs ; orange pour les noms de nœuds.
    public static readonly IBrush Window = new SolidColorBrush(Color.Parse("#eef0f3"));
    public static readonly IBrush Panel = new SolidColorBrush(Color.Parse("#f7f8fa"));
    public static readonly IBrush Line = new SolidColorBrush(Color.Parse("#dfe2e8"));
    public static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1c1f26"));
    public static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#5b6170"));
    public static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#2563eb"));
    public static readonly IBrush ActiveBg = new SolidColorBrush(Color.Parse("#dbe6fe"));
    public static readonly IBrush BenchBrush = SchematicCanvas.BenchBrush;
    public static readonly IBrush BenchBg = new SolidColorBrush(Color.Parse("#f3effe"));
    public static readonly IBrush PassBrush = new SolidColorBrush(Color.Parse("#15803d"));
    public static readonly IBrush FailBrush = new SolidColorBrush(Color.Parse("#b91c1c"));
    public static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#b45309"));
    /// <summary>Fond des cartes, champs, schéma et courbes (blanc en mode clair).</summary>
    public static readonly IBrush Surface = new SolidColorBrush(Colors.White);
    public static readonly IBrush GridDot = new SolidColorBrush(Color.Parse("#c9ccd3"));
    public static readonly IBrush Wire = new SolidColorBrush(Color.Parse("#1f6f43"));
    public static readonly IBrush NodeLabel = new SolidColorBrush(Color.Parse("#b45309"));
    public static readonly IBrush SymbolFill = new SolidColorBrush(Color.Parse("#fffdf5"));
    public static readonly IBrush LabelFill = new SolidColorBrush(Color.Parse("#fff7ed"));
    public static bool Sombre { get; private set; }

    /// <summary>Palette claire ou sombre : les pinceaux partagés sont recolorés, les vues suivent sans reconstruction.</summary>
    public static void AppliquerTheme(bool sombre)
    {
        Sombre = sombre;
        void S(IBrush b, string clair, string fonce) => ((SolidColorBrush)b).Color = Color.Parse(sombre ? fonce : clair);
        S(Window, "#eef0f3", "#15171c"); S(Panel, "#f7f8fa", "#1c1f25"); S(Line, "#dfe2e8", "#343944");
        S(Ink, "#1c1f26", "#e4e7ec"); S(Muted, "#5b6170", "#9aa1ae"); S(AccentBrush, "#2563eb", "#4f8ef7");
        S(ActiveBg, "#dbe6fe", "#1d3461"); S(WarnBrush, "#b45309", "#f59e0b"); S(Surface, "#ffffff", "#23262d");
        S(GridDot, "#c9ccd3", "#3d424d"); S(Wire, "#1f6f43", "#3fbf7f"); S(NodeLabel, "#b45309", "#f5a524");
        S(SymbolFill, "#fffdf5", "#2b2a24"); S(LabelFill, "#fff7ed", "#3a2a17");
    }

    /// <summary>Fond clair (ou sombre) teinté d'une couleur : pastille active, étiquette de calque.</summary>
    public static IBrush Teinte(IBrush couleur, double force = 0.09)
    {
        var c = (couleur as ISolidColorBrush)?.Color ?? Colors.Gray; var f = ((ISolidColorBrush)Surface).Color;
        byte M(byte a, byte b) => (byte)(b + (a - b) * force);
        return new SolidColorBrush(Color.FromRgb(M(c.R, f.R), M(c.G, f.G), M(c.B, f.B)));
    }

    public static Button ToolButton(string text, string tip, Action a)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 5), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(b, tip); b.Click += (_, _) => a();
        return b;
    }
    public static void SetActive(Button b, bool on) { b.Background = on ? ActiveBg : Brushes.Transparent; b.Foreground = on ? AccentBrush : Ink; }
    public static Control Separator() => new Border { Width = 1, Height = 22, Background = Line, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center };
    public static Control Rule() => new Border { Height = 1, Background = Line, Margin = new Thickness(0, 10) };
    public static TextBlock Title(string t) => new() { Text = t, FontSize = Titre, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };
    public static TextBlock SubTitle(string t) => new() { Text = t, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0) };
    public static TextBlock Section(string t) => new() { Text = t.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = Muted, LetterSpacing = 0.6 };
    public static TextBlock Label(string t) => new() { Text = t, FontSize = Aide, Foreground = Muted };
    public static TextBlock Hint(string t) => new() { Text = t, FontSize = Aide, Foreground = Muted, TextWrapping = TextWrapping.Wrap };

    public static Control Labeled(string label, Control c)
    {
        var s = new StackPanel { Spacing = 2 }; s.Children.Add(Label(label)); s.Children.Add(c); return s;
    }

    /// <summary>Champ texte libellé, validé par Entrée ou perte de focus.</summary>
    public static Control Field(string label, string value, Action<string> commit, string? watermark = null)
    {
        var t = new TextBox { Text = value, PlaceholderText = watermark, FontSize = 12 };
        t.KeyDown += (_, e) => { if (e.Key == Key.Enter) commit(t.Text ?? ""); };
        t.LostFocus += (_, _) => commit(t.Text ?? "");
        return Labeled(label, t);
    }

    public static Control Badge(Verdict? v)
    {
        var (text, bg) = v switch
        {
            Verdict.Pass => ("PASS", PassBrush), Verdict.Fail => ("FAIL", FailBrush),
            Verdict.NonEvaluable => ("N/É", WarnBrush), _ => ("—", Muted)
        };
        var b = new Border { Background = bg, CornerRadius = new CornerRadius(3), Padding = new Thickness(7, 2), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeight.Bold } };
        if (v == Verdict.NonEvaluable) ToolTip.SetTip(b, "Non évaluable : la mesure n'a pas pu être faite (voir le détail)");
        return b;
    }
}
