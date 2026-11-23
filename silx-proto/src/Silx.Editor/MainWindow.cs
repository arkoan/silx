using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ScottPlot.Avalonia;
using Silx.Core;
namespace Silx.Editor;

public sealed class MainWindow : Window
{
    readonly SchematicCanvas _canvas = new();
    readonly TextBox _netlist = new() { IsReadOnly = true, FontFamily = "monospace", AcceptsReturn = true };
    readonly TextBox _issues = new() { IsReadOnly = true, Height = 90, AcceptsReturn = true, Foreground = Avalonia.Media.Brushes.DarkRed };
    readonly TextBox _value = new() { Width = 220, Watermark = "valeur du composant sélectionné (Entrée)" };
    readonly AvaPlot _plot = new();
    readonly TextBlock _status = new() { Margin = new Thickness(8, 4) };
    CancellationTokenSource? _cts;

    public MainWindow()
    {
        Title = "Silx — prototype éditeur de schéma"; Width = 1280; Height = 780;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6) };
        void Btn(string text, Action a) { var b = new Button { Content = text }; b.Click += (_, _) => { a(); _canvas.Focus(); }; bar.Children.Add(b); }
        Btn("Sélection", () => _canvas.Tool = Tool.Select); Btn("Fil", () => _canvas.Tool = Tool.Wire);
        Btn("R", () => _canvas.Tool = Tool.Resistor); Btn("C", () => _canvas.Tool = Tool.Capacitor); Btn("L", () => _canvas.Tool = Tool.Inductor);
        Btn("V", () => _canvas.Tool = Tool.VoltageSource); Btn("GND", () => _canvas.Tool = Tool.Ground);
        Btn("⟳ Rot (R)", () => _canvas.RotateSelected()); Btn("Suppr", () => _canvas.DeleteSelected());
        Btn("Annuler", () => _canvas.Undo()); Btn("Rétablir", () => _canvas.Redo());
        bar.Children.Add(_value);
        _value.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) _canvas.EditSelectedValue(_value.Text ?? ""); };
        Btn("▶ Simuler (.tran)", () => _ = SimulateAsync());

        var right = new Grid { RowDefinitions = new("*,Auto,2*") , Width = 520 };
        right.Children.Add(_netlist); Grid.SetRow(_netlist, 0);
        right.Children.Add(_issues); Grid.SetRow(_issues, 1);
        right.Children.Add(_plot); Grid.SetRow(_plot, 2);

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
        DockPanel.SetDock(_status, Dock.Bottom); dock.Children.Add(_status);
        DockPanel.SetDock(right, Dock.Right); dock.Children.Add(right);
        dock.Children.Add(_canvas);
        Content = dock;

        _canvas.Changed += Refresh;
        _canvas.Load(Demo.RcLowPass());
        _status.Text = "Clic gauche : placer/sélectionner · clic droit ou milieu : pan · molette : zoom · R : pivoter · Suppr · Ctrl+Z/Y · double-clic : fin de fil";
    }

    NetlistResult Current() => NetlistGenerator.Generate(_canvas.Schematic, "Silx prototype", ".tran 10u 10m");
    void Refresh()
    {
        var r = Current(); _netlist.Text = r.Text;
        _issues.Text = r.Issues.Count == 0 ? "ERC : OK" : string.Join("\n", r.Issues.Select(i => $"[{i.Severity}] {i.Message}"));
        _value.Text = _canvas.Selected?.Value ?? "";
    }

    public async Task SimulateAsync()
    {
        _cts?.Cancel(); _cts = new(); var r = Current();
        if (r.Issues.Any(i => i.Severity == Severity.Error)) { _status.Text = "Simulation bloquée : erreurs ERC"; return; }
        var c = Connectivity.Build(_canvas.Schematic);
        var nodes = c.NodeOf.Values.Where(n => n != "0").Distinct().OrderBy(n => n).ToList();
        try
        {
            _status.Text = "ngspice en cours…";
            var traces = await NgspiceRunner.RunTranAsync(r.Text, nodes, "10u 10m", _cts.Token);
            _plot.Plot.Clear();
            foreach (var t in traces) { var sc = _plot.Plot.Add.Scatter(t.X, t.Y); sc.LegendText = t.Name; sc.MarkerSize = 0; }
            _plot.Plot.Axes.Bottom.Label.Text = "t (s)"; _plot.Plot.ShowLegend(); _plot.Plot.Axes.AutoScale(); _plot.Refresh();
            _status.Text = $"OK — {traces.Count} courbe(s), {traces[0].X.Length} points";
        }
        catch (Exception ex) { _status.Text = "Erreur : " + ex.Message.Split('\n')[0]; }
    }
}

public static class Demo
{
    /// <summary>V1(créneau) → R1 → C1 → GND.</summary>
    public static Schematic RcLowPass()
    {
        var s = new Schematic();
        s.Components.Add(new() { Ref = "V1", Kind = ComponentKind.VoltageSource, Position = new(2, 8), Rotation = 1, Value = "PULSE(0 1 0 1n 1n 5m 10m)" });
        s.Components.Add(new() { Ref = "R1", Kind = ComponentKind.Resistor, Position = new(2, 8), Value = "1k" });
        s.Components.Add(new() { Ref = "C1", Kind = ComponentKind.Capacitor, Position = new(6, 8), Rotation = 1, Value = "1u" });
        s.Wires.Add(new(new(6, 12), new(2, 12)));
        s.Components.Add(new() { Ref = "GND1", Kind = ComponentKind.Ground, Position = new(2, 12) });
        return s;
    }
}
