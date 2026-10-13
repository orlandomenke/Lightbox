using System.Diagnostics;
using System.Text;

namespace Lightbox.Ai.Cli;

/// <summary>
/// <see cref="ICliRunner"/> over a real child process: one launch per request,
/// nothing left running between them.
/// </summary>
/// <remarks>
/// <para>
/// Each run gets a fresh private folder of its own as its working directory,
/// deleted afterwards — not wherever Lightbox was started, so a tool that reads
/// instructions from its working directory finds none belonging to whatever
/// project the artist has open, and not the shared temp folder itself, which
/// on Linux is <c>/tmp</c> and the one place another user could plant one.
/// </para>
/// <para>
/// A batch file is refused rather than run. .NET hands a <c>.cmd</c> to
/// <c>cmd.exe</c>, which re-parses every argument — and the arguments here
/// include a system prompt full of quotes and newlines. That is a quoting
/// hazard at best and an injection at worst (S7), so the person is asked for
/// the real executable instead.
/// </para>
/// </remarks>
/// <param name="timeout">
/// How long a run may take before it is stopped. A program waiting on a login
/// prompt or a dead network otherwise waits forever, and the artist with it —
/// the HTTP providers get this from their client, a process has to be given it.
/// </param>
public sealed class ProcessCliRunner(TimeSpan? timeout = null) : ICliRunner
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    /// <summary>Test seam: told the id of each process as it starts.</summary>
    internal Action<int>? Started { get; init; }

    public async Task<CliOutput> RunAsync(CliInvocation invocation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(invocation.Command);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException(
                $"“{invocation.Command}” is a batch file, which cannot be handed a prompt safely. "
                + "Point at the program itself — claude.exe from Claude Code's native installer.");
        }

        var info = new ProcessStartInfo(invocation.Command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var arg in invocation.Arguments) info.ArgumentList.Add(arg);
        foreach (var (name, value) in invocation.Environment ?? new Dictionary<string, string?>())
        {
            if (value is null) info.Environment.Remove(name);
            else info.Environment[name] = value;
        }

        var workingDirectory = Directory.CreateTempSubdirectory("lightbox-cli-");
        info.WorkingDirectory = workingDirectory.FullName;
        try
        {
            return await RunAsync(info, invocation, ct);
        }
        finally
        {
            try { workingDirectory.Delete(recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left for the system's own temp cleanup; never worth failing a request over.
            }
        }
    }

    private async Task<CliOutput> RunAsync(ProcessStartInfo info, CliInvocation invocation, CancellationToken ct)
    {
        Process process;
        try
        {
            process = Process.Start(info)
                      ?? throw new CliException($"Could not start “{invocation.Command}”.");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new CliException($"Could not start “{invocation.Command}”: {e.Message}");
        }
        Started?.Invoke(process.Id);

        using (process)
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            limit.CancelAfter(_timeout);
            try
            {
                // Read both pipes while writing: a program that answers before
                // it has finished reading would otherwise fill its output pipe
                // and wait on us while we wait on it.
                var output = process.StandardOutput.ReadToEndAsync(limit.Token);
                var error = process.StandardError.ReadToEndAsync(limit.Token);
                try
                {
                    await process.StandardInput.WriteAsync(invocation.Input.AsMemory(), limit.Token);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // It exited without reading. Its exit code and stderr say why.
                }

                await process.WaitForExitAsync(limit.Token);
                return new CliOutput(process.ExitCode, await output, await error);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new CliTimeoutException(
                    $"“{invocation.Command}” did not answer within {_timeout.TotalMinutes:0.#} minutes and was stopped.");
            }
            finally
            {
                // Whatever went wrong, nothing is left running.
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (Exception e) when (e is InvalidOperationException or NotSupportedException
                                              or System.ComponentModel.Win32Exception)
                {
                }
            }
        }
    }
}
