using System;
using System.Threading;

namespace gEmuera.GodotHost;

/// <summary>
/// Defines the one safe boundary for releasing legacy thread-owned resources:
/// the worker must have terminated first. A timeout is deliberately terminal
/// for the current lifecycle operation; callers may retry cleanup later, but
/// they must not start a replacement worker or dispose its wait handle.
/// </summary>
internal static class LegacyThreadQuiescence
{
    public static void WaitForStopOrThrow(Thread worker, TimeSpan timeout, string operation)
    {
        ArgumentNullException.ThrowIfNull(worker);
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Thread join timeout must not be negative.");
        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("Lifecycle operation is required.", nameof(operation));

        if (!worker.IsAlive)
            return;
        if (!worker.Join(timeout))
        {
            throw new TimeoutException(
                $"Legacy worker did not stop within {timeout.TotalMilliseconds:0} ms while {operation}. " +
                "Its resources remain owned by the current session.");
        }
    }
}
