using System.Diagnostics;
using Silx.Core.Circuits;
using Silx.Core.Netlist;
using Silx.Core.Raw;

namespace Silx.Core.Simulation;

public sealed record SimulationResult(string RunId, RawFile Raw, string Log, TimeSpan Elapsed, int ExitCode);

public sealed class NgspiceException(string message, string log) : Exception(message)
{
    public string Log { get; } = log;
}

/// <summary>Exécute ngspice en CLI (<c>ngspice -b -r</c>) : un process par run.</summary>
public sealed class NgspiceRunner
{
    public string Executable { get; init; } = "ngspice";
    public string? WorkRoot { get; init; }
    /// <summary>
    /// Threads OpenMP par run ngspice (modèles BSIM3/BSIM4 uniquement). ngspice en utilise 2 par défaut :
    /// en exécution parallèle, cela surcharge les cœurs. null = défaut ngspice.
    /// Écrit un <c>.spiceinit</c> dans le répertoire du run, qui prime sur celui de l'utilisateur.
    /// </summary>
    public int? NumThreads { get; init; }

    public Task<SimulationResult> RunAsync(Circuit circuit, CancellationToken ct = default) =>
        RunAsync(circuit, NumThreads, passiveWait: false, ct);

    /// <param name="numThreads">threads OpenMP pour ce run (null = défaut ngspice).</param>
    /// <param name="passiveWait">
    /// OMP_WAIT_POLICY=PASSIVE : les threads OpenMP en attente dorment au lieu de tourner à vide.
    /// Indispensable quand plusieurs runs multi-threads se partagent les cœurs (sinon jusqu'à ×100 plus lent,
    /// voir bench/Silx.Perf) ; coûte ~25 % sur un run seul, d'où son usage limité aux lots.
    /// </param>
    internal async Task<SimulationResult> RunAsync(Circuit circuit, int? numThreads, bool passiveWait, CancellationToken ct)
    {
        string runId = Guid.NewGuid().ToString("N")[..12];
        string dir = Path.Combine(WorkRoot ?? Path.GetTempPath(), "silx-" + runId);
        Directory.CreateDirectory(dir);
        try
        {
            string cir = Path.Combine(dir, "run.cir"), raw = Path.Combine(dir, "run.raw"), log = Path.Combine(dir, "run.log");
            await File.WriteAllTextAsync(cir, NetlistWriter.Write(circuit), ct);
            if (numThreads is int nt)
                await File.WriteAllTextAsync(Path.Combine(dir, ".spiceinit"), $"set num_threads={Math.Max(1, nt)}\n", ct);

            var psi = new ProcessStartInfo(Executable)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                UseShellExecute = false, WorkingDirectory = dir
            };
            if (passiveWait && Environment.GetEnvironmentVariable("OMP_WAIT_POLICY") is null)
                psi.Environment["OMP_WAIT_POLICY"] = "PASSIVE";
            foreach (var a in new[] { "-b", "-r", raw, "-o", log, cir }) psi.ArgumentList.Add(a);

            var sw = Stopwatch.StartNew();
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de lancer ngspice.");
            p.StandardInput.Close();
            // Vider stdout/stderr pour éviter tout blocage sur tampon plein.
            var so = p.StandardOutput.ReadToEndAsync(ct);
            var se = p.StandardError.ReadToEndAsync(ct);
            try { await p.WaitForExitAsync(ct); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* déjà terminé */ }
                throw;
            }
            await Task.WhenAll(so, se);
            sw.Stop();

            string logText = File.Exists(log) ? await File.ReadAllTextAsync(log, ct) : await so + await se;
            if (!File.Exists(raw) || new FileInfo(raw).Length == 0)
                throw new NgspiceException($"ngspice n'a pas produit de .raw (code {p.ExitCode}).", logText);
            var rf = RawFileReader.Read(raw);
            return new SimulationResult(runId, rf, logText, sw.Elapsed, p.ExitCode);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }
}
