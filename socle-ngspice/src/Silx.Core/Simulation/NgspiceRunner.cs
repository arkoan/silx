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
    /// Mode de compatibilité ngspice (« psa » pour les modèles fabricant écrits pour PSpice : TEMP, accolades imbriquées…).
    /// Écrit « set ngbehavior=… » dans un .spiceinit du dossier de run. null = comportement par défaut.
    /// </summary>
    public string? Compatibilite { get; init; }

    public async Task<SimulationResult> RunAsync(Circuit circuit, CancellationToken ct = default)
    {
        string runId = Guid.NewGuid().ToString("N")[..12];
        string dir = Path.Combine(WorkRoot ?? Path.GetTempPath(), "silx-" + runId);
        Directory.CreateDirectory(dir);
        try
        {
            string cir = Path.Combine(dir, "run.cir"), raw = Path.Combine(dir, "run.raw"), log = Path.Combine(dir, "run.log");
            await File.WriteAllTextAsync(cir, NetlistWriter.Write(circuit), ct);
            if (Compatibilite is { Length: > 0 } compat)
                await File.WriteAllTextAsync(Path.Combine(dir, ".spiceinit"), $"set ngbehavior={compat}\n", ct);

            var psi = new ProcessStartInfo(Executable)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                UseShellExecute = false, WorkingDirectory = dir
            };
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
