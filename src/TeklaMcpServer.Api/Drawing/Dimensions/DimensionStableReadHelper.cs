using System;
using System.Collections.Generic;
using System.Threading;

namespace TeklaMcpServer.Api.Drawing;

internal static class DimensionStableReadHelper
{
    private static readonly int[] DefaultRetryDelaysMs = [50, 150];

    public static T ReadStable<T>(
        Func<T> read,
        Func<T, string> fingerprint,
        IReadOnlyList<int>? retryDelaysMs = null,
        Action<int>? sleep = null,
        Action<int, long>? readCompleted = null)
    {
        if (read == null)
            throw new ArgumentNullException(nameof(read));
        if (fingerprint == null)
            throw new ArgumentNullException(nameof(fingerprint));

        sleep ??= static delayMs =>
        {
            if (delayMs > 0)
                Thread.Sleep(delayMs);
        };

        var readIndex = 0;
        T ReadWithTrace()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var value = read();
            readCompleted?.Invoke(++readIndex, timer.ElapsedMilliseconds);
            return value;
        }

        var current = ReadWithTrace();
        var currentFingerprint = fingerprint(current);
        var delays = retryDelaysMs ?? DefaultRetryDelaysMs;

        foreach (var delayMs in delays)
        {
            sleep(delayMs);
            var next = ReadWithTrace();
            var nextFingerprint = fingerprint(next);
            if (string.Equals(currentFingerprint, nextFingerprint, StringComparison.Ordinal))
                return next;

            current = next;
            currentFingerprint = nextFingerprint;
        }

        return current;
    }
}
