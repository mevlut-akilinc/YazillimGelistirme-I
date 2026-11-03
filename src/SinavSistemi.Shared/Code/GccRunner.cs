using System.Diagnostics;
using System.IO;
using SinavSistemi.Shared.Models;

namespace SinavSistemi.Shared.Code;

/// <summary>
/// C kodunu gcc ile derleyip test case'ler üzerinde çalıştırır.
/// Ogrenci.exe: "Derle ve test et" (yalnızca görünür testler).
/// Egitmen.exe: teslimden sonra puanlama (tüm testler) + final'de karmaşıklık ölçümü.
/// </summary>
public class GccRunner
{
    /// <summary>gcc PATH'te değilse tam yol verin (ör. C:\Program Files\CodeBlocks\MinGW\bin\gcc.exe).</summary>
    public string GccPath { get; set; } = "gcc";
    public TimeSpan CompileTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan RunTimeout { get; set; } = TimeSpan.FromSeconds(2);

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var r = await RunProcessAsync(GccPath, "--version", null, TimeSpan.FromSeconds(5));
            return r.ExitCode == 0;
        }
        catch { return false; }
    }

    public async Task<CodeRunResult> CompileAndTestAsync(string code, IReadOnlyList<TestCase> tests)
    {
        var dir = Path.Combine(Path.GetTempPath(), "SinavSistemi", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, "main.c");
        var exe = Path.Combine(dir, "main.exe");
        await File.WriteAllTextAsync(src, code);

        try
        {
            ProcResult compile;
            try
            {
                compile = await RunProcessAsync(GccPath, $"-std=c11 -O0 \"{src}\" -o \"{exe}\" -lm", null, CompileTimeout);
            }
            catch (Exception ex)
            {
                return new CodeRunResult { Compiled = false, CompilerOutput = "gcc çalıştırılamadı: " + ex.Message };
            }

            if (compile.ExitCode != 0 || !File.Exists(exe))
                return new CodeRunResult
                {
                    Compiled = false,
                    // Geçici klasör yolunu kısaltıp öğrenciye sade hata göster
                    CompilerOutput = (compile.StdErr + compile.StdOut).Replace(src, "main.c").Trim()
                };

            int passed = 0;
            var details = new List<string>();
            foreach (var (t, i) in tests.Select((t, i) => (t, i + 1)))
            {
                var run = await RunProcessAsync(exe, "", t.Input, RunTimeout);
                bool ok = !run.TimedOut && Normalize(run.StdOut) == Normalize(t.ExpectedOutput);
                if (ok) passed++;
                details.Add(run.TimedOut ? $"Test {i}: süre aşımı" : ok ? $"Test {i}: geçti" : $"Test {i}: yanlış çıktı");
            }

            return new CodeRunResult
            {
                Compiled = true,
                CompilerOutput = string.Join("\n", details),
                PassedTests = passed,
                TotalTests = tests.Count
            };
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* dosya kilitliyse sonra silinir */ }
        }
    }

    /// <summary>Satır sonu ve sondaki boşluk farklarını yok say.</summary>
    private static string Normalize(string s)
        => string.Join("\n", s.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim();

    private record ProcResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

    private static async Task<ProcResult> RunProcessAsync(string file, string args, string? stdin, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException(file + " başlatılamadı");

        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (stdin is not null) await p.StandardInput.WriteAsync(stdin);
        p.StandardInput.Close();

        using var cts = new CancellationTokenSource(timeout);
        bool timedOut = false;
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { p.Kill(true); } catch { }
        }

        return new ProcResult(timedOut ? -1 : p.ExitCode, await outTask, await errTask, timedOut);
    }
}
