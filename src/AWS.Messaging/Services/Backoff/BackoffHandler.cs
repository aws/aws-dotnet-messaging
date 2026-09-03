// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.\r
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.ExceptionServices;
using AWS.Messaging.Configuration;
using Microsoft.Extensions.Logging;

namespace AWS.Messaging.Services.Backoff;

/// <summary>
/// A backoff handler that is responsible for performing back-offs in the event that a given delegate throws an exception.
/// The delegate will be retried after backing off for a certain period of time.
/// The <see cref="BackoffHandler"/> is used with a <see cref="IBackoffPolicy"/> to determine whether a backoff should occur
/// and how long to wait between back-offs.
/// </summary>
internal class BackoffHandler : IBackoffHandler
{
    private readonly IBackoffPolicy _backoffPolicy;
    private readonly ILogger<BackoffHandler> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Constructs an instance of <see cref="BackoffHandler"/>
    /// </summary>
    /// <param name="backoffPolicy">The backoff policy that determines whether a backoff should occur
    /// and how long to wait between back-offs.</param>
    /// <param name="logger">Logger for debugging information</param>
    /// <param name="timeProvider">Provides time abstractions.</param>
    public BackoffHandler(IBackoffPolicy backoffPolicy, ILogger<BackoffHandler> logger, TimeProvider timeProvider)
    {
        _backoffPolicy = backoffPolicy;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Performs a back off in the event that a given delegate throws an exception.
    /// The delegate will be retried after backing off for a certain period of time.
    /// </summary>
    /// <param name="task">The delegate for which to perform a backoff.</param>
    /// <param name="configuration">Internal configuration for polling messages from SQS.</param>
    /// <param name="token">The cancellation token used to cancel the request.</param>
    public async Task<T> BackoffAsync<T>(Func<Task<T>> task, SQSMessagePollerConfiguration configuration, CancellationToken token = default)
    {
        bool shouldRetry;
        var retries = 0;
        do
        {
            ExceptionDispatchInfo capturedException;
            shouldRetry = false;

            try
            {
                return await task.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unknown exception occurred while polling '{SubscriberEndpoint}'.", configuration.SubscriberEndpoint);
                capturedException = ExceptionDispatchInfo.Capture(ex);
            }

            // Checking the backoff policy whether a backoff should be attempted.
            shouldRetry = _backoffPolicy.ShouldBackoff(capturedException.SourceException, configuration);

            if (!shouldRetry)
            {
                capturedException.Throw();
            }
            else
            {
                // Checking the backoff policy for how long to backoff before attempting to poll SQS for messages again.
                var waitTime = _backoffPolicy.RetrieveBackoffTime(retries);
                _logger.LogWarning("Backing off polling from SQS for messages for {WaitTime}s before trying again...", waitTime.TotalSeconds);

                await Task.Delay(waitTime, _timeProvider, token);

                retries++;
                _logger.LogWarning("Attempt #{Retry} to poll SQS for messages...", retries);
            }

        } while (
            shouldRetry &&
            !token.IsCancellationRequested);

        return default!;
    }
}
