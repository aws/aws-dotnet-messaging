// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Tests.Common;

public static class AsyncTestUtilities
{
    /// <summary>
    /// Polls <paramref name="condition"/> until it returns <c>true</c> or <paramref name="timeoutToken"/>
    /// is cancelled (e.g. a deadline set via <see cref="CancellationTokenSource.CancelAfter(int)"/>). This
    /// lets a test proceed as soon as the expected state is reached instead of always waiting the full
    /// timeout, which matters most on the in-memory backend where messages are processed near-instantly.
    /// </summary>
    /// <returns>
    /// <c>true</c> if <paramref name="condition"/> was met, <c>false</c> if the timeout elapsed first. No
    /// exception is thrown on timeout: callers can either assert on the result for an explicit "timed out"
    /// failure, or assert on the actual state so the failure message shows how far it got.
    /// </returns>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, CancellationToken timeoutToken, int pollIntervalMs = 200)
    {
        while (!condition())
        {
            if (timeoutToken.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                await Task.Delay(pollIntervalMs, timeoutToken);
            }
            catch (OperationCanceledException)
            {
                // Deadline hit mid-delay; loop once more for a final check before reporting the timeout.
            }
        }

        return true;
    }
}
