// Bancs de performance des calculs de simulation et de la gestion des threads (Silx.Core).
// Usage : dotnet run -c Release -- [--rapide] [--sortie <dossier>] [--par-max N] [taille] [threads] [openmp] [campagne] [annulation]
// Sans scénario : tous. Les résultats sont écrits en Markdown (lisible) et JSON (comparaison entre machines).
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Silx.Core.Circuits;
using Silx.Core.Netlist;
using Silx.Core.Simulation;
using Silx.Perf;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

bool rapide = args.Contains("--rapide");
string sortie = ArgValue("--sortie") ?? "resultats";
int coeurs = Environment.ProcessorCount;
int parMax = int.Parse(ArgValue("--par-max") ?? (2 * coeurs).ToString());
var scenarios = args.Where(a => !a.StartsWith("--") && a != ArgValue("--sortie") && a != ArgValue("--par-max")).ToList();
if (scenarios.Count == 0) scenarios = ["taille", "threads", "openmp", "campagne", "annulation"];

string? ArgValue(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

var md = new StringBuilder();
var json = new Dictionary<string, object>();
void Out(string line = "") { Console.WriteLine(line); md.AppendLine(line); }

// ---------------------------------------------------------------- machine
string ngVersion = NgspiceVersion();
var machine = new Dictionary<string, object>
{
    ["os"] = RuntimeInformation.OSDescription,
    ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
    ["coeurs_logiques"] = coeurs,
    ["cpu"] = CpuName(),
    ["memoire_go"] = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1e9, 1),
    ["dotnet"] = RuntimeInformation.FrameworkDescription,
    ["ngspice"] = ngVersion,
    ["date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
    ["mode"] = rapide ? "rapide" : "complet",
};
json["machine"] = machine;
Out($"# Bancs de performance Silx — {machine["date"]}");
Out();
Out($"- Machine : {machine["cpu"]}, {coeurs} cœurs logiques, {machine["memoire_go"]} Go, {machine["os"]} ({machine["arch"]})");
Out($"- Logiciels : {machine["dotnet"]}, {ngVersion}");
Out($"- Mode : {machine["mode"]}");
Out();

var work = Path.Combine(Path.GetTempPath(), "silx-perf-" + Environment.ProcessId);
Directory.CreateDirectory(work);
NgspiceRunner Runner(int? nt = 1) => new() { WorkRoot = work, NumThreads = nt };

try
{
    foreach (var s in scenarios)
    {
        switch (s)
        {
            case "taille": await Taille(); break;
            case "threads": await Threads(); break;
            case "openmp": await OpenMp(); break;
            case "campagne": await Campagne(); break;
            case "annulation": await Annulation(); break;
            default: Console.Error.WriteLine($"Scénario inconnu : {s}"); break;
        }
    }
}
finally { try { Directory.Delete(work, true); } catch { } }

Directory.CreateDirectory(sortie);
string stamp = $"{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmm}";
File.WriteAllText(Path.Combine(sortie, $"perf-{stamp}.md"), md.ToString());
File.WriteAllText(Path.Combine(sortie, $"perf-{stamp}.json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"\nRésultats écrits dans {Path.GetFullPath(sortie)}/perf-{stamp}.(md|json)");

// ================================================================ scénarios

// 1) Temps d'un run seul selon la taille du circuit : solveur vs coût fixe, mémoire de ngspice.
async Task Taille()
{
    Out("## 1. Temps d'un run selon la taille du circuit (1 run à la fois)");
    Out();
    Out("Temps médian sur 3 runs. « Analyse » = temps du solveur ngspice ; « Fixe » = lancement, netlist, lecture .raw.");
    Out();
    Out("| Famille | Taille | Points | Run (ms) | Analyse (ms) | Fixe (ms) | Mém. ngspice (Mo) |");
    Out("|---|---:|---:|---:|---:|---:|---:|");
    var rows = new List<object>();
    var runner = Runner();
    await runner.RunAsync(Circuits.RcSimple(1, 0));   // préchauffage (cache disque, JIT)
    foreach (var f in Circuits.Familles)
    {
        foreach (int n in rapide ? f.TaillesRapides : f.Tailles)
        {
            var c = f.Creer(n);
            var walls = new List<double>(); var ana = new List<double>(); int pts = 0;
            int reps = rapide ? 1 : 3;
            for (int r = 0; r < reps; r++)
            {
                var sw = Stopwatch.StartNew();
                var res = await runner.RunAsync(c);
                walls.Add(sw.Elapsed.TotalMilliseconds);
                ana.Add(AnalysisMs(res.Log) ?? double.NaN);
                pts = res.Raw.PointCount;
                if (walls[^1] > 20_000) break;   // inutile de répéter un run très long
            }
            double w = Median(walls), a = Median(ana);
            double? mem = PeakMemoryMb(NetlistWriter.Write(c));
            Out($"| {f.Nom} | {n} {f.Unite} | {pts} | {w:F1} | {a:F1} | {w - a:F1} | {(mem is { } m ? m.ToString("F0") : "n/d")} |");
            rows.Add(new { famille = f.Nom, taille = n, points = pts, run_ms = w, analyse_ms = a, fixe_ms = w - a, memoire_mo = mem });
            if (w > 60_000) { Out($"| {f.Nom} | (tailles suivantes ignorées : > 60 s) | | | | | |"); break; }
        }
    }
    Out();
    json["taille"] = rows;
}

// 2) Mise à l'échelle avec le nombre de runs en parallèle (gestion des threads de ParallelSimulator).
async Task Threads()
{
    Out("## 2. Débit selon le nombre de runs en parallèle");
    Out();
    Out($"Lot de runs indépendants (variantes de valeurs) exécuté par `ParallelSimulator`, 1 thread OpenMP par run. " +
        $"Accélération = débit / débit à 1 ; efficacité = accélération / parallélisme. CPU = temps CPU des process ngspice / durée (cœurs réellement occupés).");
    Out();
    var pars = new SortedSet<int> { 1, 2, coeurs, 2 * coeurs };
    for (int p = 4; p < 2 * coeurs; p *= 2) pars.Add(p);
    pars.RemoveWhere(p => p > parMax);
    var cas = new (string fam, int taille)[] { ("rc", 1), ("echelle_rc", 1000), ("bjt", 10), ("cmos", 50), ("xspice", 16) };
    var rows = new List<object>();
    var runner = Runner();
    foreach (var (fam, taille) in cas)
    {
        var f = Circuits.Famille(fam);
        // Calibrage : taille du lot pour ~8 s (3 s en rapide) au parallélisme 1.
        var t1 = new List<double>();
        for (int i = 0; i < 3; i++) { var sw = Stopwatch.StartNew(); await runner.RunAsync(f.Creer(taille, i)); t1.Add(sw.Elapsed.TotalMilliseconds); }
        int lot = (int)Math.Clamp((rapide ? 3000 : 8000) / Median(t1), 2 * pars.Max, 2000);
        var circuits = Enumerable.Range(0, lot).Select(i => f.Creer(taille, i)).ToList();
        Out($"**{f.Description} ({taille} {f.Unite}), lot de {lot} runs**");
        Out();
        Out("| Parallélisme | Durée (s) | Débit (runs/s) | Accélération | Efficacité | CPU (cœurs) | Latence p50 (ms) | p95 (ms) |");
        Out("|---:|---:|---:|---:|---:|---:|---:|---:|");
        double debit1 = 0;
        foreach (int p in pars)
        {
            var (wall, lat, cpu, ok) = await Lot(new ParallelSimulator(runner, p), circuits);
            double debit = ok / wall;
            if (p == 1) debit1 = debit;
            double acc = debit / debit1;
            Out($"| {p} | {wall:F2} | {debit:F1} | ×{acc:F2} | {acc / p:P0} | {(cpu is { } c ? (c / wall).ToString("F2") : "n/d")} | {Pct(lat, 50):F1} | {Pct(lat, 95):F1} |");
            rows.Add(new { famille = fam, taille, lot, parallelisme = p, duree_s = wall, debit, acceleration = acc, cpu_coeurs = cpu / wall, p50_ms = Pct(lat, 50), p95_ms = Pct(lat, 95), echecs = lot - ok });
        }
        Out();
    }
    json["threads"] = rows;
}

// 3) Threads OpenMP internes à ngspice (BSIM4) combinés au parallélisme des runs.
async Task OpenMp()
{
    Out("## 3. Threads OpenMP de ngspice × runs parallèles (CMOS BSIM4)");
    Out();
    Out("ngspice parallélise l'évaluation des modèles BSIM3/BSIM4 avec OpenMP, 2 threads par défaut. " +
        "On compare un run seul (latence) et un lot (débit) selon ce réglage. « auto » = réglage choisi par `ParallelSimulator` " +
        "(cœurs / runs simultanés). En lot, les threads fixés à la main tournent avec OMP_WAIT_POLICY=PASSIVE.");
    Out();
    var f = Circuits.Famille("cmos");
    int taille = rapide ? 100 : 200;
    var nts = new List<int?> { null }.Concat(new SortedSet<int> { 1, 2, coeurs }.Select(x => (int?)x)).ToList();
    var rows = new List<object>();
    Out($"| Threads OpenMP/run | Runs parallèles | Lot | Durée (s) | Débit (runs/s) | Latence p50 (ms) | CPU (cœurs) |");
    Out("|---:|---:|---:|---:|---:|---:|---:|");
    foreach (int par in new SortedSet<int> { 1, coeurs })
    {
        int lot = par == 1 ? (rapide ? 2 : 3) : 2 * coeurs;
        var circuits = Enumerable.Range(0, lot).Select(i => f.Creer(taille, i)).ToList();
        foreach (int? nt in nts)
        {
            var sim = new ParallelSimulator(Runner(nt), par);
            var (wall, lat, cpu, ok) = await Lot(sim, circuits);
            Out($"| {(nt is null ? $"auto ({sim.ThreadsPerRun})" : nt.ToString())} | {par} | {lot} | {wall:F2} | {ok / wall:F2} | {Pct(lat, 50):F0} | {(cpu is { } c ? (c / wall).ToString("F2") : "n/d")} |");
            rows.Add(new { taille, threads_openmp = sim.ThreadsPerRun, auto = nt is null, parallelisme = par, lot, duree_s = wall, debit = ok / wall, p50_ms = Pct(lat, 50), cpu_coeurs = cpu / wall });
        }
    }
    Out();
    json["openmp"] = rows;
}

// 4) Campagne type Monte Carlo : surcoût de l'orchestrateur et mémoire des résultats gardés.
async Task Campagne()
{
    Out("## 4. Campagne de runs : surcoût de l'orchestrateur et mémoire");
    Out();
    Out("Tous les résultats (.raw lus) sont gardés en mémoire, comme pour un post-traitement statistique. " +
        "Surcoût = durée réelle − (somme des temps ngspice / parallélisme).");
    Out();
    Out("| Cas | Runs | Parallélisme | Durée (s) | Débit (runs/s) | Surcoût orchestrateur | Mémoire .NET gardée (Mo) | Par run (Ko) |");
    Out("|---|---:|---:|---:|---:|---:|---:|---:|");
    var rows = new List<object>();
    var cas = new (string nom, int runs, Func<int, Circuit> fab)[]
    {
        ("RC court, 1 vecteur, 500 pts", rapide ? 300 : 2000, i => Circuits.RcSimple(1, i)),
        ("Échelle RC 100, tous les vecteurs, 2000 pts", rapide ? 50 : 200, i => Circuits.EchelleRc(100, i).Also(c => c.Saves.Clear())),
    };
    foreach (var (nom, runs, fab) in cas)
    {
        var circuits = Enumerable.Range(0, runs).Select(fab).ToList();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long avant = GC.GetTotalMemory(true);
        var sim = new ParallelSimulator(Runner(), coeurs);
        var ch = Channel.CreateUnbounded<RunProgress>();
        double somme = 0; int ok = 0;
        var lecteur = Task.Run(async () => { await foreach (var p in ch.Reader.ReadAllAsync()) if (p.Success) { somme += p.Elapsed.TotalMilliseconds; ok++; } });
        var sw = Stopwatch.StartNew();
        var res = await sim.RunAllAsync(circuits, ch.Writer);
        double wall = sw.Elapsed.TotalSeconds;
        await lecteur;
        long apres = GC.GetTotalMemory(true);
        double memMo = (apres - avant) / 1e6;
        GC.KeepAlive(res);
        double ideal = somme / 1000 / coeurs;
        double surcout = (wall - ideal) / wall;
        Out($"| {nom} | {runs} | {coeurs} | {wall:F2} | {ok / wall:F0} | {surcout:P0} | {memMo:F1} | {memMo * 1000 / runs:F0} |");
        rows.Add(new { cas = nom, runs, parallelisme = coeurs, duree_s = wall, debit = ok / wall, surcout, memoire_mo = memMo, echecs = runs - ok });
    }
    Out();
    json["campagne"] = rows;
}

// 5) Annulation d'un lot en cours : délai de retour et process ngspice orphelins.
async Task Annulation()
{
    Out("## 5. Annulation d'un lot en cours");
    Out();
    var f = Circuits.Famille("cmos");
    var circuits = Enumerable.Range(0, 4 * coeurs).Select(i => f.Creer(200, i)).ToList();
    int avantProc = CountNgspice();
    using var cts = new CancellationTokenSource();
    var sim = new ParallelSimulator(Runner(), coeurs);
    var t = sim.RunAllAsync(circuits, null, cts.Token);
    await Task.Delay(1500);
    var sw = Stopwatch.StartNew();
    cts.Cancel();
    try { await t; } catch (OperationCanceledException) { }
    double retour = sw.Elapsed.TotalMilliseconds;
    int orphelins = CountNgspice() - avantProc;
    await Task.Delay(500);
    int orphelins500 = CountNgspice() - avantProc;
    int dossiers = Directory.GetDirectories(work, "silx-*").Length;
    Out($"Lot de {circuits.Count} runs CMOS (≈ 1 à 4 s chacun), {coeurs} en parallèle, annulé après 1,5 s :");
    Out();
    Out($"- Retour de `RunAllAsync` après annulation : **{retour:F0} ms**");
    Out($"- Process ngspice encore vivants au retour : {orphelins} ; 500 ms après : {orphelins500}");
    Out($"- Répertoires de travail restants : {dossiers}");
    Out();
    json["annulation"] = new { runs = circuits.Count, parallelisme = coeurs, retour_ms = retour, orphelins, orphelins_500ms = orphelins500, dossiers_restants = dossiers };
}

// ================================================================ outils

async Task<(double wall, List<double> lat, double? cpu, int ok)> Lot(ParallelSimulator sim, IReadOnlyList<Circuit> circuits)
{
    var ch = Channel.CreateUnbounded<RunProgress>();
    var lat = new List<double>(); int ok = 0;
    var lecteur = Task.Run(async () => { await foreach (var p in ch.Reader.ReadAllAsync()) if (p.Success) { lat.Add(p.Elapsed.TotalMilliseconds); ok++; } });
    double? cpu0 = ChildCpuSeconds();
    var sw = Stopwatch.StartNew();
    await sim.RunAllAsync(circuits, ch.Writer);
    double wall = sw.Elapsed.TotalSeconds;
    double? cpu1 = ChildCpuSeconds();
    await lecteur;
    return (wall, lat, cpu1 - cpu0, ok);
}

static double Median(List<double> v) { var s = v.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList(); return s.Count == 0 ? double.NaN : s[s.Count / 2]; }
static double Pct(List<double> v, int p) { if (v.Count == 0) return double.NaN; var s = v.OrderBy(x => x).ToList(); return s[Math.Min(s.Count - 1, (int)Math.Ceiling(p / 100.0 * s.Count) - 1)]; }

static double? AnalysisMs(string log)
{
    var m = Regex.Match(log, @"Total analysis time \(seconds\) = ([0-9.eE+-]+)");
    return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 1000 : null;
}

static int CountNgspice() => Process.GetProcessesByName("ngspice").Length;

static string NgspiceVersion()
{
    try
    {
        var p = Process.Start(new ProcessStartInfo("ngspice", "-v") { RedirectStandardOutput = true, RedirectStandardError = true })!;
        string o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
        var m = Regex.Match(o, @"ngspice-[\w.+-]+"); return m.Success ? m.Value : "ngspice (version inconnue)";
    }
    catch { return "ngspice introuvable"; }
}

static string CpuName()
{
    try
    {
        if (OperatingSystem.IsLinux())
            return File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim() ?? "?";
        if (OperatingSystem.IsMacOS())
        {
            var p = Process.Start(new ProcessStartInfo("sysctl", "-n machdep.cpu.brand_string") { RedirectStandardOutput = true })!;
            string o = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); return o;
        }
        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "?";
    }
    catch { return "?"; }
}

/// <summary>Temps CPU cumulé (user+sys) des process enfants terminés (ngspice). Linux/macOS uniquement.</summary>
static double? ChildCpuSeconds()
{
    if (OperatingSystem.IsWindows()) return null;
    try
    {
        var r = new long[18];
        if (Native.getrusage(-1 /* RUSAGE_CHILDREN */, r) != 0) return null;
        long us(long v) => OperatingSystem.IsMacOS() ? (int)(v & 0xFFFFFFFF) : v;   // tv_usec est un int32 sur macOS
        return r[0] + us(r[1]) / 1e6 + r[2] + us(r[3]) / 1e6;
    }
    catch { return null; }
}

/// <summary>Pic de mémoire d'un run ngspice, échantillonné toutes les 10 ms (Linux/Windows).</summary>
double? PeakMemoryMb(string netlist)
{
    if (OperatingSystem.IsMacOS()) return null;
    string dir = Path.Combine(work, "mem-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(dir);
    try
    {
        File.WriteAllText(Path.Combine(dir, "run.cir"), netlist);
        File.WriteAllText(Path.Combine(dir, ".spiceinit"), "set num_threads=1\n");
        var psi = new ProcessStartInfo("ngspice") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var a in new[] { "-b", "-r", "run.raw", "-o", "run.log", "run.cir" }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        _ = p.StandardOutput.ReadToEndAsync(); _ = p.StandardError.ReadToEndAsync();
        long peak = 0;
        while (!p.WaitForExit(10))
        {
            try { p.Refresh(); peak = Math.Max(peak, p.PeakWorkingSet64); } catch { }
        }
        return peak > 0 ? peak / 1e6 : null;
    }
    finally { try { Directory.Delete(dir, true); } catch { } }
}

static class Native
{
    [DllImport("libc", SetLastError = true)]
    public static extern int getrusage(int who, [Out] long[] usage);
}
