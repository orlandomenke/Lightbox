namespace Lightbox.Core.Documents;

public static class Ids
{
    private static long _counter;

    /// <summary>Stable, human-scannable unique id: prefix_timestamp_counter.</summary>
    public static string NewId(string prefix)
    {
        var n = Interlocked.Increment(ref _counter);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return $"{prefix}_{ts:x}_{n:x}";
    }
}
