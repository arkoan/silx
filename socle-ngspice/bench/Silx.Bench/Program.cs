using System.Diagnostics;
using Silx.Bench;
using Silx.Core.Circuits;
using Silx.Core.Netlist;
using Silx.Core.Simulation;

// Circuit de référence : RC, transitoire 5 ms, pas 10 µs (~500 points) — petit, donc dominé par l'overhead.
Circuit Make(int i) => new Circuit("bench").V("V1", "in", "0", "PULSE(0 1 0 1n 1n 1 2)")
    .R("R1", "in", "out", $"{1000 + i}").C("C1", "out", "0", "1u")
    .Also(c => c.Analysis = new TranAnalysis(10e-6, double.Parse(Environment.GetEnvironmentVariable("TSTOP") ?? "5e-3", System.Globalization.CultureInfo.InvariantCulture)));

int n = args.Length > 0 ? int.Parse(args[0]) : 200;
int par = Environment.ProcessorCount;
Console.WriteLine($"Cœurs logiques: {par}, runs: {n}");
var circuits = Enumerable.Range(0, n).Select(Make).ToList();
var runner = new NgspiceRunner();

// Warm-up
await runner.RunAsync(circuits[0]);

// 1) CLI séquentiel, bout en bout (netlist + process + lecture .raw)
var sw = Stopwatch.StartNew();
foreach (var c in circuits) await runner.RunAsync(c);
double cliSeq = sw.Elapsed.TotalMilliseconds / n;

// 2) CLI parallèle
sw.Restart();
await new ParallelSimulator(runner, par).RunAllAsync(circuits);
double cliPar = sw.Elapsed.TotalMilliseconds / n;

// 3) Coût de démarrage seul : ngspice -b sur netlist triviale (.op)
var trivial = new Circuit("t").V("V1", "a", "0", "DC 1").R("R1", "a", "0", "1k").Also(c => c.Analysis = new OpAnalysis());
await runner.RunAsync(trivial);
sw.Restart();
for (int i = 0; i < 50; i++) await runner.RunAsync(trivial);
double startup = sw.Elapsed.TotalMilliseconds / 50;

// 4) libngspice in-process, séquentiel (une seule instance par process)
LibNgspice.Init();
LibNgspice.RunAndGet(NetlistWriter.Write(circuits[0]), "out");
sw.Restart();
double check = 0;
foreach (var c in circuits) check += LibNgspice.RunAndGet(NetlistWriter.Write(c), "out")[^1];
double libSeq = sw.Elapsed.TotalMilliseconds / n;

Console.WriteLine($"CLI séquentiel        : {cliSeq,8:F2} ms/run");
Console.WriteLine($"CLI parallèle x{par}     : {cliPar,8:F2} ms/run  (débit {1000 / cliPar:F0} runs/s)");
Console.WriteLine($"CLI circuit trivial   : {startup,8:F2} ms/run  (≈ coût fixe process+I/O)");
Console.WriteLine($"libngspice séquentiel : {libSeq,8:F2} ms/run  (débit {1000 / libSeq:F0} runs/s)  [chk {check:F3}]");
Console.WriteLine($"Surcoût CLI/lib (séq) : {cliSeq - libSeq,8:F2} ms/run, x{cliSeq / libSeq:F1}");
Console.WriteLine($"Débit CLI parallèle / lib séquentiel : x{libSeq / cliPar:F2}");

static class Ext { public static T Also<T>(this T t, Action<T> a) { a(t); return t; } }
