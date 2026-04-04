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
        Action<int>? sleep = null)
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

        var current = read();
        var currentFingerprint = fingerprint(current);
        var delays = retryDelaysMs ?? DefaultRetryDelaysMs;

        foreach (var delayMs in delays)
        {
            sleep(delayMs);
            var next = read();
            var nextFingerprint = fingerprint(next);
            if (string.Equals(currentFingerprint, nextFingerprint, StringComparison.Ordinal))
                return next;

            current = next;
            currentFingerprint = nextFingerprint;
        }

        return current;
    }
}
