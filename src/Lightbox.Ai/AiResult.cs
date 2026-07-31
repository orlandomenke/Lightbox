namespace Lightbox.Ai;

public enum AiOutcome
{
    Success,
    Refused,
    Truncated,
    Error,
}

/// <summary>
/// The result of an AI call. AI methods never throw across the app boundary —
/// every failure mode becomes a value the UI can present.
/// </summary>
public sealed class AiResult<T>
{
    public AiOutcome Outcome { get; }

    public T? Value { get; }

    public string? Message { get; }

    /// <summary>True when retrying the same request may succeed (rate limit, network, overload).</summary>
    public bool Retryable { get; }

    private AiResult(AiOutcome outcome, T? value, string? message, bool retryable)
    {
        Outcome = outcome;
        Value = value;
        Message = message;
        Retryable = retryable;
    }

    public static AiResult<T> Success(T value) => new(AiOutcome.Success, value, null, false);

    public static AiResult<T> Refused(string message) => new(AiOutcome.Refused, default, message, false);

    public static AiResult<T> Truncated(string message) => new(AiOutcome.Truncated, default, message, false);

    public static AiResult<T> Error(string message, bool retryable) => new(AiOutcome.Error, default, message, retryable);
}
