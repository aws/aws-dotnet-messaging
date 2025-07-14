// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using Amazon.SQS;

namespace AWS.Messaging.Configuration;

/// <summary>
/// Configuration for polling messages from SQS
/// </summary>
public class SQSMessagePollerOptions
{
    /// <inheritdoc cref="SQSMessagePollerConfiguration.MaxNumberOfConcurrentMessages"/>
    public int MaxNumberOfConcurrentMessages { get; set; } = SQSMessagePollerConfiguration.DEFAULT_MAX_NUMBER_OF_CONCURRENT_MESSAGES;

    /// <inheritdoc cref="SQSMessagePollerConfiguration.VisibilityTimeout"/>
    public int VisibilityTimeout { get; set; } = SQSMessagePollerConfiguration.DEFAULT_VISIBILITY_TIMEOUT_SECONDS;

    /// <inheritdoc cref="SQSMessagePollerConfiguration.WaitTimeSeconds"/>
    public int WaitTimeSeconds { get; set; } = SQSMessagePollerConfiguration.DEFAULT_WAIT_TIME_SECONDS;

    /// <inheritdoc cref="SQSMessagePollerConfiguration.VisibilityTimeoutExtensionThreshold"/>
    public int VisibilityTimeoutExtensionThreshold { get; set; } = SQSMessagePollerConfiguration.DEFAULT_VISIBILITY_TIMEOUT_EXTENSION_THRESHOLD_SECONDS;

    /// <inheritdoc cref="SQSMessagePollerConfiguration.VisibilityTimeoutExtensionHeartbeatInterval"/>
    public int VisibilityTimeoutExtensionHeartbeatInterval { get; set; } = SQSMessagePollerConfiguration.DEFAULT_VISIBILITY_TIMEOUT_EXTENSION_HEARTBEAT_INTERVAL;

    /// <inheritdoc cref="SQSMessagePollerConfiguration.IsExceptionFatal" />
    public Func<Exception, bool> IsExceptionFatal { get; set; } = SQSMessagePollerConfiguration.DefaultIsExceptionFatal;

    /// <summary>
    /// Validates that the options are valid against the message framework's and/or SQS limits
    /// </summary>
    /// <exception cref="InvalidSQSMessagePollerOptionsException">Thrown when one or more invalid options are found</exception>
    internal void Validate()
    {
        var errorMessages = new List<string>();
       
        if (MaxNumberOfConcurrentMessages <= 0)
        {
            errorMessages.Add($"{nameof(MaxNumberOfConcurrentMessages)} must be greater than 0. Current value: {MaxNumberOfConcurrentMessages}.");
        }

        // https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-visibility-timeout.html
        if (VisibilityTimeout < 0 || VisibilityTimeout > TimeSpan.FromHours(12).TotalSeconds)
        {
            errorMessages.Add($"{nameof(VisibilityTimeout)} must be between 0 seconds and 12 hours. Current value: {VisibilityTimeout}.");
        }

        // https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-short-and-long-polling.html#sqs-long-polling
        if (WaitTimeSeconds < 0 || WaitTimeSeconds > 20)
        {
            errorMessages.Add($"{nameof(WaitTimeSeconds)} must be between 0 seconds and 20 seconds. Current value: {WaitTimeSeconds}.");
        }

        if (VisibilityTimeoutExtensionThreshold <= 0)
        {
            errorMessages.Add($"{nameof(VisibilityTimeoutExtensionThreshold)} must be greater than 0. Current value: {VisibilityTimeoutExtensionThreshold}.");
        }

        if (VisibilityTimeoutExtensionThreshold >= VisibilityTimeout)
        {
            errorMessages.Add($"{nameof(VisibilityTimeoutExtensionThreshold)} ({VisibilityTimeoutExtensionThreshold} seconds) " +
                $"must be less than {nameof(VisibilityTimeout)} ({VisibilityTimeout} seconds), " +
                $"or else other consumers may receive the message while it is still being processed.");
        }

        if (VisibilityTimeoutExtensionHeartbeatInterval <= 0)
        {
            errorMessages.Add($"{nameof(VisibilityTimeoutExtensionHeartbeatInterval)} must be greater than 0 seconds. " +
                $"Current value: {VisibilityTimeoutExtensionHeartbeatInterval}.");
        }

        if (VisibilityTimeoutExtensionHeartbeatInterval >= VisibilityTimeoutExtensionThreshold)
        {
            errorMessages.Add($"{nameof(VisibilityTimeoutExtensionHeartbeatInterval)} ({VisibilityTimeoutExtensionHeartbeatInterval}) " +
                            $"must be less than {nameof(VisibilityTimeoutExtensionThreshold)} ({VisibilityTimeoutExtensionThreshold}), " +
                            $"or else other consumers may receive the message while it is still being processed.");
        }

        if (errorMessages.Count != 0)
        {
            throw new InvalidSQSMessagePollerOptionsException(string.Join(Environment.NewLine, errorMessages));
        }
    }
}

