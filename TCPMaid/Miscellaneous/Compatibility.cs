#if NETSTANDARD

using System.Runtime.CompilerServices;

namespace TCPMaid {
    /// <summary>
    /// Extensions for compatibility with .NET Standard.
    /// </summary>
    internal static class Compatibility {
        /// <summary>
        /// Waits for a task to complete or cancel.
        /// </summary>
        public static async Task<T> WaitAsync<T>(this Task<T> Task, CancellationToken CancelToken = default) {
            return await Task.ContinueWith(Task => Task.GetAwaiter().GetResult(), CancelToken);
        }
    }
    /// <summary>
    /// Interlocked methods for compatibility with .NET Standard.
    /// </summary>
    internal static class Interlocked {
        /// <summary>
        /// Increments a <see cref="ulong"/> and stores the result, as an atomic operation.
        /// </summary>
        public static ulong Increment(ref ulong Location) {
            return (ulong)Increment(ref Unsafe.As<ulong, long>(ref Location));
        }
        /// <summary>
        /// Increments a <see cref="long"/> and stores the result, as an atomic operation.
        /// </summary>
        public static long Increment(ref long Location) {
            return System.Threading.Interlocked.Increment(ref Location);
        }
    }
}

#endif