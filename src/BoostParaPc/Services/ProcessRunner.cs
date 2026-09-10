using System.Diagnostics;
using System.Text;

namespace BoostParaPc.Services;

public static class ProcessRunner
{
    public sealed record CommandResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)
    {
        public bool Success => ExitCode == 0 && !TimedOut;
    }

    public static async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        int timeoutMs = 30_000,
        bool elevated = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (elevated)
        {
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.RedirectStandardOutput = false;
            psi.RedirectStandardError = false;
            psi.CreateNoWindow = false;
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        if (!elevated)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) stderr.AppendLine(e.Data);
            };
        }

        try
        {
            if (!process.Start())
                return new CommandResult(-1, string.Empty, "Falha ao iniciar processo", false);

            if (!elevated)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new CommandResult(-1, stdout.ToString(), stderr.ToString(), true);
            }

            return new CommandResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false);
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, string.Empty, ex.Message, false);
        }
    }

    public static async Task<string> RunPowerShellAsync(string script, int timeoutMs = 30_000)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var result = await RunAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            timeoutMs);
        return result.Success ? result.StdOut.Trim() : result.StdErr.Trim();
    }
}
