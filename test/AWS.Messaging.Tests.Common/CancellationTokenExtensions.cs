// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

namespace AWS.Messaging.Tests.Common;

public static class CancellationTokenExtensions
{
    /// <summary>
    /// Returns a task that completes when the token is cancelled, allowing tests to
    /// await a cancellation deadline (e.g. set via <see cref="CancellationTokenSource.CancelAfter(int)"/>)
    /// without polling.
    /// </summary>
    public static async Task WaitForCancellationAsync(this CancellationToken token)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Dispose the registration once the wait completes so it doesn't linger on the
        // token's source. Disposal runs on the awaiter's continuation (not inside the
        // callback, thanks to RunContinuationsAsynchronously), so it can't deadlock.
        await using (token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), tcs))
        {
            await tcs.Task;
        }
    }
}
