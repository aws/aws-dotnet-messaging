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
    /// On timeout it returns quietly so the caller's own assertions report the actual (unmet) state.
    /// </summary>
    public static async Task WaitUntilAsync(Func<bool> condition, CancellationToken timeoutToken, int pollIntervalMs = 200)
    {
        while (!condition() && !timeoutToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(pollIntervalMs, timeoutToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
