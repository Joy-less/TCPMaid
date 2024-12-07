using System.Diagnostics;

namespace TCPMaid;

/// <summary>
/// Extensions for compatibility with .NET Standard.
/// </summary>
internal static class CompatibilityExtensions {
    /// <summary>
    /// Waits for a task to complete or cancel.
    /// </summary>
    public static async Task<T> WaitAsync<T>(this Task<T> Task, CancellationToken CancelToken = default) {
        return await Task.ContinueWith(Task => Task.GetAwaiter().GetResult(), CancelToken);
    }
    /// <summary>
    /// Gets the elapsed time between two timestamps retrieved using <see cref="Stopwatch.GetTimestamp()"/>.
    /// </summary>
    public static TimeSpan GetElapsedTime(long StartTimestamp, long EndTimestamp) {
        double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
        long Delta = EndTimestamp - StartTimestamp;
        long Ticks = (long)(TimestampToTicks * Delta);
        return new TimeSpan(Ticks);
    }
}