namespace Lightbox.Ai.Cli;

/// <summary>One launch of a command-line program: what to run and what to feed it.</summary>
/// <param name="Input">
/// Written to standard input and then closed. Everything that scales with the
/// drawing goes here rather than in <paramref name="Arguments"/>, because a
/// Windows command line stops at 32,767 characters and two keyframes do not.
/// </param>
/// <param name="Environment">
/// Changes to the inherited environment: a value sets a variable, null removes
/// it. Absent means inherit everything.
/// </param>
public sealed record CliInvocation(
    string Command,
    IReadOnlyList<string> Arguments,
    string Input,
    IReadOnlyDictionary<string, string?>? Environment = null);

public sealed record CliOutput(int ExitCode, string Output, string Error);

/// <summary>
/// Runs a program to completion. An interface so an artist built on a command
/// line can be tested against scripted output without a model, a login or a
/// process.
/// </summary>
public interface ICliRunner
{
    /// <exception cref="CliException">The program could not be started.</exception>
    /// <exception cref="CliTimeoutException">It started and did not finish in time.</exception>
    Task<CliOutput> RunAsync(CliInvocation invocation, CancellationToken ct);
}

public class CliException(string message) : Exception(message);

/// <summary>Started, then stopped for taking too long — not a missing program.</summary>
public sealed class CliTimeoutException(string message) : CliException(message);
