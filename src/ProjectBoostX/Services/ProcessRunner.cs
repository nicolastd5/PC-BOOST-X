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
        bool elevated = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        try
        {
            if (!process.Start())
                return new CommandResult(-1, string.Empty, "Falha ao iniciar processo", false);

            var stdout = elevated ? Task.FromResult(string.Empty) : process.StandardOutput.ReadToEndAsync();
            var stderr = elevated ? Task.FromResult(string.Empty) : process.StandardError.ReadToEndAsync();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch { /* Preserve the timeout even if the process cannot be stopped. */ }
                cancellationToken.ThrowIfCancellationRequested();
                return new CommandResult(-1,
                    stdout.IsCompletedSuccessfully ? stdout.Result : string.Empty,
                    stderr.IsCompletedSuccessfully ? stderr.Result : string.Empty, true);
            }

            return new CommandResult(process.ExitCode,
                await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false), false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new CommandResult(-1, string.Empty, ex.Message, false);
        }
    }

    public static async Task<CommandResult> RunCheckedAsync(
        string fileName, string arguments, int timeoutMs = 30_000, bool elevated = false,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(fileName, arguments, timeoutMs, elevated, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(fileName, result);
        return result;
    }

    private static void EnsureSuccess(string command, CommandResult result)
    {
        if (result.Success) return;
        var detail = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut.Trim() : result.StdErr.Trim();
        if (result.TimedOut)
            throw new TimeoutException($"{command}: tempo limite excedido. {detail}".Trim());
        throw new InvalidOperationException($"{command}: falha (código {result.ExitCode}). {detail}".Trim());
    }

    public static Task<CommandResult> RunPowerShellResultAsync(string script, int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        var checkedScript = $$"""
            [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
            $OutputEncoding = [Console]::OutputEncoding
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            $LASTEXITCODE = 0
            try {
                & {
            {{script}}
                }
                if ($LASTEXITCODE -ne 0) { throw "Comando nativo falhou (código $LASTEXITCODE)." }
            } catch {
                [Console]::Error.WriteLine($_.Exception.Message)
                exit 1
            }
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(checkedScript));
        return RunAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -OutputFormat Text -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            timeoutMs, cancellationToken: cancellationToken);
    }

    public static async Task<string> RunPowerShellAsync(string script, int timeoutMs = 30_000,
        CancellationToken cancellationToken = default)
    {
        var result = await RunPowerShellResultAsync(script, timeoutMs, cancellationToken).ConfigureAwait(false);
        EnsureSuccess("PowerShell", result);
        return result.StdOut.Trim();
    }
}
